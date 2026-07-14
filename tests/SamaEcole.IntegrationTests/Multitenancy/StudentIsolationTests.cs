using FluentAssertions;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Npgsql;
using Xunit;

namespace SamaEcole.IntegrationTests.Multitenancy;

/// <summary>
/// Test obligatoire référencé par AGENTS.md, .cursor/rules/database.mdc et
/// docs/Volume_8_Test_Strategy.md §5 — ticket JGK-A03.
///
/// Principe : prouver qu'AUCUNE requête ne peut retourner la donnée d'une autre école, même en
/// simulant l'oubli du Global Query Filter EF Core (requête SQL brute). Seule la policy RLS
/// PostgreSQL doit alors faire foi — d'où des requêtes en SQL direct, hors EF Core.
/// </summary>
[Trait("Category", "MultiTenant")]
public class StudentIsolationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        // Jeu de données posé par le PROPRIÉTAIRE (exempté de RLS) : un élève dans chaque école.
        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        owner.Students.AddRange(
            new Student
            {
                SchoolId = EcoleA, Matricule = "ELEV-2026-0001", FullName = "Awa Fall",
                BirthDate = new DateOnly(2015, 3, 12), Gender = "F", ClassroomId = Guid.NewGuid()
            },
            new Student
            {
                SchoolId = EcoleB, Matricule = "ELEV-2026-0001", FullName = "Modou Diop",
                BirthDate = new DateOnly(2014, 8, 2), Gender = "M", ClassroomId = Guid.NewGuid()
            });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task RawSqlQuery_Should_Never_Return_Other_School_Data_Even_Without_EfCore_Filter()
    {
        // SQL BRUT, sans EF Core : le Global Query Filter n'entre pas en jeu. Si des données de
        // l'École B remontent ici, c'est que la RLS ne protège rien.
        var names = await QueryStudentNamesAsync(EcoleA);

        names.Should().ContainSingle().Which.Should().Be("Awa Fall");
        names.Should().NotContain("Modou Diop", "la RLS doit masquer les élèves des autres écoles");
    }

    [Fact]
    public async Task Session_Without_Tenant_Should_See_No_Student_At_All()
    {
        // Aucun app.current_school_id posé (ex. requête non authentifiée) : la RLS doit échouer en
        // FERMETURE — zéro ligne — et surtout pas exposer toute la base.
        var names = await QueryStudentNamesAsync(schoolId: null);

        names.Should().BeEmpty();
    }

    [Fact]
    public async Task Writing_A_Student_Into_Another_School_Should_Be_Rejected()
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);

        // Session positionnée sur l'École A qui tente d'écrire pour l'École B : le WITH CHECK de la
        // policy doit refuser. Sans lui, un bug applicatif pourrait injecter chez un concurrent.
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO students ("Id", "SchoolId", "Matricule", "FullName", "BirthDate", "Gender",
                                  "ClassroomId", "CreatedAt", "IsDeleted")
            VALUES (gen_random_uuid(), @schoolId, 'ELEV-2026-9999', 'Intrus', DATE '2015-01-01', 'M',
                    gen_random_uuid(), NOW(), FALSE);
            """;
        command.Parameters.AddWithValue("schoolId", EcoleB);

        var act = async () => await command.ExecuteNonQueryAsync();

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task Application_Role_Must_Not_Be_Able_To_Bypass_Rls()
    {
        // Garde-fou : si quelqu'un redonne un jour SUPERUSER ou BYPASSRLS au rôle applicatif, toutes
        // les policies deviennent décoratives. Ce test casse alors immédiatement.
        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT rolsuper OR rolbypassrls
            FROM pg_roles
            WHERE rolname = current_user;
            """;

        var canBypass = (bool)(await command.ExecuteScalarAsync())!;

        canBypass.Should().BeFalse(
            "le rôle applicatif doit être NOSUPERUSER et NOBYPASSRLS, sinon la RLS ne protège rien");
    }

    private async Task<List<string>> QueryStudentNamesAsync(Guid? schoolId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);

        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "FullName" FROM students ORDER BY "FullName";""";

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}