using FluentAssertions;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Npgsql;
using Xunit;

namespace SamaEcole.IntegrationTests.Subjects;

/// <summary>
/// Ticket JGK-C03 — l'isolation des matières tient-elle dans la BASE ?
///
/// Comme pour les élèves et les années scolaires : tout est en SQL BRUT, avec le rôle applicatif et
/// sans EF Core. Le Global Query Filter n'entre donc pas en jeu — seule la policy RLS fait foi.
/// </summary>
[Trait("Category", "MultiTenant")]
public class SubjectIsolationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task RawSqlQuery_Should_Never_Return_Other_School_Subjects()
    {
        await InsertSubjectAsync(EcoleA, "Mathématiques", "Primaire", 4);
        await InsertSubjectAsync(EcoleB, "Philosophie", "Terminale", 2);

        // SQL brut, sans EF : si une matière de l'École B remonte ici, la RLS ne protège rien.
        var names = await ReadSubjectNamesAsync(EcoleA);

        names.Should().ContainSingle().Which.Should().Be("Mathématiques");
        names.Should().NotContain("Philosophie", "la RLS doit masquer les matières des autres écoles");
    }

    [Fact]
    public async Task Session_Without_Tenant_Should_See_No_Subject_At_All()
    {
        await InsertSubjectAsync(EcoleA, "Mathématiques", "Primaire", 4);

        // Aucun app.current_school_id : la RLS échoue en FERMETURE, zéro ligne.
        var names = await ReadSubjectNamesAsync(schoolId: null);

        names.Should().BeEmpty();
    }

    [Fact]
    public async Task Writing_A_Subject_Into_Another_School_Should_Be_Rejected()
    {
        // Session sur l'École A qui tente d'écrire pour l'École B : le WITH CHECK de la policy refuse.
        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO subjects ("Id", "SchoolId", "Name", "Level", "Coefficient", "CreatedAt", "IsDeleted")
            VALUES (gen_random_uuid(), @schoolId, 'Intrus', 'Primaire', 3, NOW(), FALSE);
            """;
        command.Parameters.AddWithValue("schoolId", EcoleB);

        var act = async () => await command.ExecuteNonQueryAsync();

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task The_Same_Subject_May_Exist_At_Two_Different_Levels()
    {
        // « Maths » coef 4 au primaire ET « Maths » coef 6 en terminale : c'est le cas normal, l'index
        // unique porte sur (niveau, nom), pas sur le nom seul.
        await InsertSubjectAsync(EcoleA, "Mathématiques", "Primaire", 4);

        var act = async () => await InsertSubjectAsync(EcoleA, "Mathématiques", "Terminale S", 6);

        await act.Should().NotThrowAsync();
    }

    private async Task InsertSubjectAsync(Guid schoolId, string name, string level, decimal coefficient)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO subjects ("Id", "SchoolId", "Name", "Level", "Coefficient", "CreatedAt", "IsDeleted")
            VALUES (gen_random_uuid(), @schoolId, @name, @level, @coefficient, NOW(), FALSE);
            """;
        command.Parameters.AddWithValue("schoolId", schoolId);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("level", level);
        command.Parameters.AddWithValue("coefficient", coefficient);

        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<string>> ReadSubjectNamesAsync(Guid? schoolId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);

        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "Name" FROM subjects ORDER BY "Name";""";

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
