using FluentAssertions;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Npgsql;
using Xunit;

namespace SamaEcole.IntegrationTests.Teachers;

/// <summary>
/// Ticket JGK-D04 — l'isolation des attributions enseignant tient-elle dans la BASE ?
///
/// Comme pour les enseignants et les matières : tout est en SQL BRUT, avec le rôle applicatif et
/// sans EF Core. Le Global Query Filter n'entre donc pas en jeu — seule la policy RLS fait foi.
/// </summary>
[Trait("Category", "MultiTenant")]
public class TeacherAssignmentIsolationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid TeacherA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid TeacherB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid ClasseA = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");
    private static readonly Guid ClasseB = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");
    private static readonly Guid MatiereA = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000e");
    private static readonly Guid MatiereB = Guid.Parse("ffffffff-0000-0000-0000-00000000000f");
    private static readonly Guid AnneeA = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid AnneeB = Guid.Parse("22222222-0000-0000-0000-000000000002");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        // Jeu de données posé par le PROPRIÉTAIRE (exempté de RLS) : une attribution valide dans
        // chaque école — TOUTES les entités référencées sont réelles, sans quoi l'insertion échouerait
        // sur la clé étrangère (23503) et non sur la RLS, ce qui rendrait le test vert à tort.
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

        owner.SchoolYears.AddRange(
            new SchoolYear { Id = AnneeA, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true },
            new SchoolYear { Id = AnneeB, SchoolId = EcoleB, Label = "2026-2027", StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true });

        owner.Teachers.AddRange(
            new Teacher { Id = TeacherA, SchoolId = EcoleA, Matricule = "ENS-2026-001", FullName = "Moussa Ndiaye", Email = "moussa@ecole-a.sn", BirthDate = new DateOnly(1985, 4, 12) },
            new Teacher { Id = TeacherB, SchoolId = EcoleB, Matricule = "ENS-2026-001", FullName = "Fatou Sarr", Email = "fatou@ecole-b.sn", BirthDate = new DateOnly(1990, 9, 3) });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task RawSqlQuery_Should_Never_Return_Other_School_Assignments()
    {
        await InsertAssignmentAsync(EcoleA, TeacherA, ClasseA, MatiereA, AnneeA);
        await InsertAssignmentAsync(EcoleB, TeacherB, ClasseB, MatiereB, AnneeB);

        // SQL brut, sans EF : si une attribution de l'École B remonte ici, la RLS ne protège rien.
        var teacherIds = await ReadAssignedTeacherIdsAsync(EcoleA);

        teacherIds.Should().ContainSingle().Which.Should().Be(TeacherA);
        teacherIds.Should().NotContain(TeacherB, "la RLS doit masquer les attributions des autres écoles");
    }

    [Fact]
    public async Task Session_Without_Tenant_Should_See_No_Assignment_At_All()
    {
        await InsertAssignmentAsync(EcoleA, TeacherA, ClasseA, MatiereA, AnneeA);

        // Aucun app.current_school_id : la RLS échoue en FERMETURE, zéro ligne.
        var teacherIds = await ReadAssignedTeacherIdsAsync(schoolId: null);

        teacherIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Writing_An_Assignment_Into_Another_School_Should_Be_Rejected()
    {
        // Session sur l'École A qui tente d'écrire pour l'École B, en visant des entités RÉELLES de
        // l'École B : seul le WITH CHECK de la policy peut alors refuser la ligne.
        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO teacher_assignments
                ("Id", "SchoolId", "TeacherId", "ClassroomId", "SubjectId", "SchoolYearId", "CreatedAt", "IsDeleted")
            VALUES (gen_random_uuid(), @schoolId, @teacherId, @classroomId, @subjectId, @schoolYearId, NOW(), FALSE);
            """;
        command.Parameters.AddWithValue("schoolId", EcoleB);
        command.Parameters.AddWithValue("teacherId", TeacherB);
        command.Parameters.AddWithValue("classroomId", ClasseB);
        command.Parameters.AddWithValue("subjectId", MatiereB);
        command.Parameters.AddWithValue("schoolYearId", AnneeB);

        var act = async () => await command.ExecuteNonQueryAsync();

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task Same_Teacher_Cannot_Be_Assigned_Twice_To_The_Same_Classroom_Subject_And_Year()
    {
        await InsertAssignmentAsync(EcoleA, TeacherA, ClasseA, MatiereA, AnneeA);

        var act = async () => await InsertAssignmentAsync(EcoleA, TeacherA, ClasseA, MatiereA, AnneeA);

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.UniqueViolation);
    }

    private async Task InsertAssignmentAsync(
        Guid schoolId, Guid teacherId, Guid classroomId, Guid subjectId, Guid schoolYearId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO teacher_assignments
                ("Id", "SchoolId", "TeacherId", "ClassroomId", "SubjectId", "SchoolYearId", "CreatedAt", "IsDeleted")
            VALUES (gen_random_uuid(), @schoolId, @teacherId, @classroomId, @subjectId, @schoolYearId, NOW(), FALSE);
            """;
        command.Parameters.AddWithValue("schoolId", schoolId);
        command.Parameters.AddWithValue("teacherId", teacherId);
        command.Parameters.AddWithValue("classroomId", classroomId);
        command.Parameters.AddWithValue("subjectId", subjectId);
        command.Parameters.AddWithValue("schoolYearId", schoolYearId);

        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<Guid>> ReadAssignedTeacherIdsAsync(Guid? schoolId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);

        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "TeacherId" FROM teacher_assignments ORDER BY "TeacherId";""";

        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }
}
