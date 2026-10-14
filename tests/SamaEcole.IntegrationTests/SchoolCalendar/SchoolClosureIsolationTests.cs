using FluentAssertions;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Npgsql;
using Xunit;

namespace SamaEcole.IntegrationTests.SchoolCalendar;

/// <summary>
/// Calendrier de fermeture (Franco-Arabe Bloc 5a) — l'isolation de <c>school_closures</c> tient-elle
/// dans la BASE ? Tout est en SQL BRUT, avec le rôle applicatif et sans EF Core : le Global Query
/// Filter n'entre pas en jeu, seule la policy RLS fait foi. S'y ajoutent les garde-fous propres à la
/// table : période cohérente (CHECK) et suppression physique refusée (le rôle n'a pas DELETE).
/// </summary>
[Trait("Category", "MultiTenant")]
public class SchoolClosureIsolationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AnneeA = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid AnneeB = Guid.Parse("22222222-0000-0000-0000-000000000002");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        owner.SchoolYears.AddRange(
            new SchoolYear { Id = AnneeA, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true },
            new SchoolYear { Id = AnneeB, SchoolId = EcoleB, Label = "2026-2027", StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task RawSqlQuery_Should_Never_Return_Other_School_Closures()
    {
        await InsertClosureAsync(EcoleA, AnneeA, "Tabaski A", "2027-06-06", "2027-06-06");
        await InsertClosureAsync(EcoleB, AnneeB, "Tabaski B", "2027-06-06", "2027-06-06");

        var labels = await ReadLabelsAsync(EcoleA);

        labels.Should().ContainSingle().Which.Should().Be("Tabaski A");
        labels.Should().NotContain("Tabaski B", "la RLS doit masquer les fermetures des autres écoles");
    }

    [Fact]
    public async Task Session_Without_Tenant_Should_See_No_Closure_At_All()
    {
        await InsertClosureAsync(EcoleA, AnneeA, "Tabaski A", "2027-06-06", "2027-06-06");

        (await ReadLabelsAsync(schoolId: null)).Should().BeEmpty();
    }

    [Fact]
    public async Task Writing_A_Closure_Into_Another_School_Should_Be_Rejected()
    {
        // Session sur l'École A qui tente d'écrire une fermeture pour l'École B, sur une année RÉELLE
        // de B : seul le WITH CHECK de la policy peut alors refuser la ligne.
        var act = async () => await InsertClosureAsync(
            EcoleB, AnneeB, "Intrus", "2027-06-06", "2027-06-06", sessionSchool: EcoleA);

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task A_Closure_Cannot_End_Before_It_Starts()
    {
        var act = async () => await InsertClosureAsync(EcoleA, AnneeA, "Incohérente", "2027-06-10", "2027-06-06");

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task A_Closure_Cannot_Point_To_The_Year_Of_Another_School()
    {
        // FK composite (SchoolId, SchoolYearId) : l'année B n'existe pas pour l'école A.
        var act = async () => await InsertClosureAsync(EcoleA, AnneeB, "Mauvaise année", "2027-06-06", "2027-06-06");

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task The_Application_Role_Must_Not_Be_Able_To_Physically_Delete_A_Closure()
    {
        await InsertClosureAsync(EcoleA, AnneeA, "Tabaski A", "2027-06-06", "2027-06-06");

        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM school_closures";

        var act = async () => await command.ExecuteNonQueryAsync();

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege,
                "la suppression est logique (règle #6) : le rôle applicatif n'a pas DELETE");
    }

    [Fact]
    public async Task The_Application_Role_Can_Soft_Delete_A_Closure()
    {
        await InsertClosureAsync(EcoleA, AnneeA, "Tabaski A", "2027-06-06", "2027-06-06");

        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE school_closures SET \"IsDeleted\" = TRUE, \"DeletedAt\" = NOW()";

        (await command.ExecuteNonQueryAsync()).Should().Be(1);
    }

    private async Task InsertClosureAsync(
        Guid schoolId, Guid schoolYearId, string label, string start, string end, Guid? sessionSchool = null)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(sessionSchool ?? schoolId);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO school_closures
                ("Id", "SchoolId", "SchoolYearId", "StartDate", "EndDate", "Kind", "Label", "CreatedAt", "IsDeleted")
            VALUES (gen_random_uuid(), @schoolId, @schoolYearId, @start::date, @end::date, 'Holiday', @label, NOW(), FALSE);
            """;
        command.Parameters.AddWithValue("schoolId", schoolId);
        command.Parameters.AddWithValue("schoolYearId", schoolYearId);
        command.Parameters.AddWithValue("start", start);
        command.Parameters.AddWithValue("end", end);
        command.Parameters.AddWithValue("label", label);

        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<string>> ReadLabelsAsync(Guid? schoolId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"Label\" FROM school_closures";

        var labels = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            labels.Add(reader.GetString(0));
        }

        return labels;
    }
}
