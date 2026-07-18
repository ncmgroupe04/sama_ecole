using FluentAssertions;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Npgsql;
using Xunit;

namespace SamaEcole.IntegrationTests.Teachers;

/// <summary>
/// Ticket JGK-D03 — l'isolation des enseignants tient-elle dans la BASE ?
///
/// Comme pour les élèves et les matières : tout est en SQL BRUT, avec le rôle applicatif et sans
/// EF Core. Le Global Query Filter n'entre donc pas en jeu — seule la policy RLS fait foi.
/// </summary>
[Trait("Category", "MultiTenant")]
public class TeacherIsolationTests : IAsyncLifetime
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
    public async Task RawSqlQuery_Should_Never_Return_Other_School_Teachers()
    {
        await InsertTeacherAsync(EcoleA, "ENS-2026-001", "Moussa Ndiaye", "moussa@ecole-a.sn");
        await InsertTeacherAsync(EcoleB, "ENS-2026-001", "Fatou Sarr", "fatou@ecole-b.sn");

        // SQL brut, sans EF : si un enseignant de l'École B remonte ici, la RLS ne protège rien.
        var names = await ReadTeacherNamesAsync(EcoleA);

        names.Should().ContainSingle().Which.Should().Be("Moussa Ndiaye");
        names.Should().NotContain("Fatou Sarr", "la RLS doit masquer les enseignants des autres écoles");
    }

    [Fact]
    public async Task Session_Without_Tenant_Should_See_No_Teacher_At_All()
    {
        await InsertTeacherAsync(EcoleA, "ENS-2026-001", "Moussa Ndiaye", "moussa@ecole-a.sn");

        // Aucun app.current_school_id : la RLS échoue en FERMETURE, zéro ligne.
        var names = await ReadTeacherNamesAsync(schoolId: null);

        names.Should().BeEmpty();
    }

    [Fact]
    public async Task Writing_A_Teacher_Into_Another_School_Should_Be_Rejected()
    {
        // Session sur l'École A qui tente d'écrire pour l'École B : le WITH CHECK de la policy refuse.
        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO teachers ("Id", "SchoolId", "Matricule", "FullName", "Email", "Status", "CreatedAt", "IsDeleted")
            VALUES (gen_random_uuid(), @schoolId, 'ENS-2026-999', 'Intrus', 'intrus@ecole-b.sn', 'Active', NOW(), FALSE);
            """;
        command.Parameters.AddWithValue("schoolId", EcoleB);

        var act = async () => await command.ExecuteNonQueryAsync();

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task Same_Matricule_Is_Allowed_Across_Two_Different_Schools()
    {
        // Un matricule est unique PAR ÉCOLE, pas globalement (même règle que Student).
        await InsertTeacherAsync(EcoleA, "ENS-2026-001", "Moussa Ndiaye", "moussa@ecole-a.sn");

        var act = async () => await InsertTeacherAsync(EcoleB, "ENS-2026-001", "Fatou Sarr", "fatou@ecole-b.sn");

        await act.Should().NotThrowAsync();
    }

    private async Task InsertTeacherAsync(Guid schoolId, string matricule, string fullName, string email)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO teachers ("Id", "SchoolId", "Matricule", "FullName", "Email", "Status", "CreatedAt", "IsDeleted")
            VALUES (gen_random_uuid(), @schoolId, @matricule, @fullName, @email, 'Active', NOW(), FALSE);
            """;
        command.Parameters.AddWithValue("schoolId", schoolId);
        command.Parameters.AddWithValue("matricule", matricule);
        command.Parameters.AddWithValue("fullName", fullName);
        command.Parameters.AddWithValue("email", email);

        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<string>> ReadTeacherNamesAsync(Guid? schoolId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);

        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "FullName" FROM teachers ORDER BY "FullName";""";

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
