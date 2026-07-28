using FluentAssertions;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Npgsql;
using Xunit;

namespace SamaEcole.IntegrationTests.SchoolYears;

/// <summary>
/// Ticket JGK-C01 — les deux invariants de l'année scolaire tiennent-ils dans la BASE ?
///
/// Tout est ici en SQL BRUT, avec le rôle applicatif et sans EF Core : le Global Query Filter et les
/// contrôles du Handler n'entrent pas en jeu. Ce qui passe ou ne passe pas dans ces tests, c'est ce
/// que PostgreSQL lui-même autorise. C'est le seul niveau qui compte : un `if` dans un Handler ne
/// protège que le code qui passe par ce Handler.
/// </summary>
[Trait("Category", "MultiTenant")]
public class SchoolYearTests : IAsyncLifetime
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
    public async Task Two_Active_Years_In_The_Same_School_Must_Be_Refused_By_The_Database()
    {
        // LE test du ticket : « une seule année active à la fois ». Deux Directeurs qui activent
        // chacun une année au même instant liraient tous deux « aucun conflit » avant d'écrire — un
        // contrôle applicatif ne les départagerait pas. L'index unique partiel, lui, tranche.
        await InsertYearAsync(EcoleA, "2026-2027", isActive: true);

        var act = async () => await InsertYearAsync(EcoleA, "2027-2028", isActive: true);

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.UniqueViolation,
                "l'index UX_school_years_single_active doit refuser une seconde année active");
    }

    [Fact]
    public async Task Several_Inactive_Years_Should_Coexist_Without_Any_Problem()
    {
        // L'index est PARTIEL : il ne contraint que les lignes actives. Une école garde autant
        // d'années passées ou à venir qu'elle veut — sans quoi elle n'aurait aucun historique.
        await InsertYearAsync(EcoleA, "2025-2026", isActive: false);
        await InsertYearAsync(EcoleA, "2026-2027", isActive: true);
        await InsertYearAsync(EcoleA, "2027-2028", isActive: false);

        var labels = await ReadYearLabelsAsync(EcoleA);

        labels.Should().HaveCount(3);
    }

    [Fact]
    public async Task Deactivating_The_Current_Year_Should_Free_The_Slot()
    {
        // C'est exactement la manœuvre d'ActivateSchoolYearCommandHandler : désactiver l'ancienne
        // AVANT d'activer la nouvelle. Si ce test échouait, c'est que l'ordre des deux écritures ne
        // suffirait pas et que le basculement d'année serait impossible.
        await InsertYearAsync(EcoleA, "2026-2027", isActive: true);

        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);

        await using var deactivate = connection.CreateCommand();
        deactivate.CommandText = """UPDATE school_years SET "IsActive" = FALSE WHERE "Label" = '2026-2027';""";
        await deactivate.ExecuteNonQueryAsync();

        await using var activate = connection.CreateCommand();
        activate.CommandText = """
            INSERT INTO school_years ("Id", "SchoolId", "Label", "StartDate", "EndDate", "IsActive",
                                      "CreatedAt", "IsDeleted")
            VALUES (gen_random_uuid(), @schoolId, '2027-2028', DATE '2027-10-01', DATE '2028-07-31',
                    TRUE, NOW(), FALSE);
            """;
        activate.Parameters.AddWithValue("schoolId", EcoleA);

        var act = async () => await activate.ExecuteNonQueryAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Each_School_Should_Keep_Its_Own_Active_Year()
    {
        // L'unicité porte sur (école), pas sur la table entière : deux écoles voisines ont chacune
        // leur année active, et rien n'empêche qu'elles portent le même libellé.
        await InsertYearAsync(EcoleA, "2026-2027", isActive: true);

        var act = async () => await InsertYearAsync(EcoleB, "2026-2027", isActive: true);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task School_Years_Of_Another_School_Must_Never_Be_Readable()
    {
        await InsertYearAsync(EcoleA, "2026-2027", isActive: true);
        await InsertYearAsync(EcoleB, "2026-2027 (École B)", isActive: true);

        var labels = await ReadYearLabelsAsync(EcoleA);

        labels.Should().ContainSingle().Which.Should().Be("2026-2027");
        labels.Should().NotContain("2026-2027 (École B)", "la RLS doit masquer les années des autres écoles");
    }

    [Fact]
    public async Task Session_Without_Tenant_Should_See_No_School_Year_At_All()
    {
        await InsertYearAsync(EcoleA, "2026-2027", isActive: true);

        // Aucun app.current_school_id posé : la RLS échoue en FERMETURE — zéro ligne — et surtout
        // n'expose pas toute la base.
        var labels = await ReadYearLabelsAsync(schoolId: null);

        labels.Should().BeEmpty();
    }

    /// <summary>Insertion en SQL brut avec le rôle applicatif : ni EF, ni Handler, ni filtre C#.</summary>
    private async Task InsertYearAsync(Guid schoolId, string label, bool isActive)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO school_years ("Id", "SchoolId", "Label", "StartDate", "EndDate", "IsActive",
                                      "CreatedAt", "IsDeleted")
            VALUES (gen_random_uuid(), @schoolId, @label, DATE '2026-10-01', DATE '2027-07-31',
                    @isActive, NOW(), FALSE);
            """;
        command.Parameters.AddWithValue("schoolId", schoolId);
        command.Parameters.AddWithValue("label", label);
        command.Parameters.AddWithValue("isActive", isActive);

        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<string>> ReadYearLabelsAsync(Guid? schoolId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);

        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "Label" FROM school_years ORDER BY "Label";""";

        var labels = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            labels.Add(reader.GetString(0));
        }

        return labels;
    }
}
