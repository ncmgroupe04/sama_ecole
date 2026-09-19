using FluentAssertions;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Npgsql;
using Xunit;

namespace SamaEcole.IntegrationTests.ClassJournal;

/// <summary>
/// Ticket JGK-P04 — l'isolation du cahier de texte tient-elle dans la BASE ? Comme pour l'appel :
/// tout est en SQL BRUT, avec le rôle applicatif et sans EF Core, pour que seule la policy RLS
/// fasse foi (le Global Query Filter n'entre pas en jeu ici).
/// </summary>
[Trait("Category", "MultiTenant")]
public class ClassJournalIsolationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid ClasseA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid ClasseB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid MatiereA = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");
    private static readonly Guid MatiereB = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");
    private static readonly Guid ProfA = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000e");
    private static readonly Guid ProfB = Guid.Parse("ffffffff-0000-0000-0000-00000000000f");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        // Jeu de données posé par le PROPRIÉTAIRE (exempté de RLS) : de quoi écrire une entrée VALIDE
        // dans chaque école — toutes les entités référencées sont réelles, sans quoi l'insertion
        // échouerait sur une clé étrangère (23503) et non sur la RLS.
        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2 A", Level = "Primaire", Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "6e B", Level = "Collège", Capacity = 45 });

        owner.Subjects.AddRange(
            new Subject { Id = MatiereA, SchoolId = EcoleA, Name = "Mathématiques", Level = "Primaire", Coefficient = 4 },
            new Subject { Id = MatiereB, SchoolId = EcoleB, Name = "Français", Level = "Collège", Coefficient = 4 });

        owner.Teachers.AddRange(
            new Teacher { Id = ProfA, SchoolId = EcoleA, Matricule = "ENS-2026-001", FullName = "Awa Fall", Email = "awa@ecole-a.sn", BirthDate = new DateOnly(1990, 1, 1) },
            new Teacher { Id = ProfB, SchoolId = EcoleB, Matricule = "ENS-2026-001", FullName = "Modou Diop", Email = "modou@ecole-b.sn", BirthDate = new DateOnly(1988, 1, 1) });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task RawSqlQuery_Should_Never_Return_Other_School_Journal_Entries()
    {
        await InsertEntryAsync(EcoleA, ClasseA, MatiereA, ProfA);
        await InsertEntryAsync(EcoleB, ClasseB, MatiereB, ProfB);

        var classroomIds = await ReadEntryClassroomsAsync(EcoleA);

        classroomIds.Should().ContainSingle().Which.Should().Be(ClasseA);
        classroomIds.Should().NotContain(ClasseB, "la RLS doit masquer les entrées de journal des autres écoles");
    }

    [Fact]
    public async Task Session_Without_Tenant_Should_See_No_Entry_At_All()
    {
        await InsertEntryAsync(EcoleA, ClasseA, MatiereA, ProfA);

        var entries = await ReadEntryClassroomsAsync(schoolId: null);

        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Writing_An_Entry_Into_Another_School_Should_Be_Rejected()
    {
        // Session sur l'École A qui tente d'écrire une entrée pour l'École B, en visant des entités
        // RÉELLES de B : seul le WITH CHECK de la policy peut alors refuser la ligne.
        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO class_journal_entries
                ("Id", "SchoolId", "ClassroomId", "SubjectId", "TeacherId", "SessionDate", "Topic", "Content", "CreatedAt", "IsDeleted")
            VALUES (gen_random_uuid(), @schoolId, @classroomId, @subjectId, @teacherId, DATE '2026-09-14', 'Sujet', 'Contenu', NOW(), FALSE);
            """;
        command.Parameters.AddWithValue("schoolId", EcoleB);
        command.Parameters.AddWithValue("classroomId", ClasseB);
        command.Parameters.AddWithValue("subjectId", MatiereB);
        command.Parameters.AddWithValue("teacherId", ProfB);

        var act = async () => await command.ExecuteNonQueryAsync();

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task Deleting_A_Journal_Entry_Row_Is_Refused_By_Privilege_Not_Just_Policy()
    {
        // Aucun rôle applicatif n'a jamais de DELETE sur une table métier (AGENTS.md règle #6) :
        // un DELETE brut échoue par privilège, avant même que la RLS n'entre en jeu.
        var id = await InsertEntryAsync(EcoleA, ClasseA, MatiereA, ProfA);

        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);
        await using var command = connection.CreateCommand();
        command.CommandText = """DELETE FROM class_journal_entries WHERE "Id" = @id;""";
        command.Parameters.AddWithValue("id", id);

        var act = async () => await command.ExecuteNonQueryAsync();

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    private async Task<Guid> InsertEntryAsync(Guid schoolId, Guid classroomId, Guid subjectId, Guid teacherId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO class_journal_entries
                ("Id", "SchoolId", "ClassroomId", "SubjectId", "TeacherId", "SessionDate", "Topic", "Content", "CreatedAt", "IsDeleted")
            VALUES (@id, @schoolId, @classroomId, @subjectId, @teacherId, DATE '2026-09-14', 'Sujet', 'Contenu', NOW(), FALSE);
            """;
        var id = Guid.NewGuid();
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("schoolId", schoolId);
        command.Parameters.AddWithValue("classroomId", classroomId);
        command.Parameters.AddWithValue("subjectId", subjectId);
        command.Parameters.AddWithValue("teacherId", teacherId);

        await command.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<List<Guid>> ReadEntryClassroomsAsync(Guid? schoolId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);

        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "ClassroomId" FROM class_journal_entries ORDER BY "ClassroomId";""";

        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }
}
