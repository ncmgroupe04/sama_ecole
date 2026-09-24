using FluentAssertions;
using Npgsql;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Coefficients;

/// <summary>
/// Évolution N°4 — la table <c>subject_coefficient_overrides</c> tient-elle son isolation, ses
/// contraintes et ses purges DANS LA BASE ? Tout est en SQL BRUT sous le rôle applicatif, sans EF Core :
/// le Global Query Filter n'entre pas en jeu, seuls la policy RLS, les index et les fonctions de purge
/// font foi (même méthode que <c>AttendanceIsolationTests</c>).
/// </summary>
[Trait("Category", "MultiTenant")]
public class CoefficientOverrideIsolationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("71111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("72222222-2222-2222-2222-222222222222");

    private static readonly Guid ClasseA = Guid.Parse("7aaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid ClasseB = Guid.Parse("7bbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid MatiereA = Guid.Parse("7ccccccc-0000-0000-0000-00000000000c");
    private static readonly Guid MatiereB = Guid.Parse("7ddddddd-0000-0000-0000-00000000000d");
    private static readonly Guid AnneeA1 = Guid.Parse("71111111-0000-0000-0000-000000000001");
    private static readonly Guid AnneeA2 = Guid.Parse("71111111-0000-0000-0000-000000000002");
    private static readonly Guid AnneeB = Guid.Parse("72222222-0000-0000-0000-000000000002");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "Terminale S2 A", Level = "Lycée", Capacity = 40, Series = "S2" },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "Terminale S2 B", Level = "Lycée", Capacity = 40, Series = "S2" });

        owner.Subjects.AddRange(
            new Subject { Id = MatiereA, SchoolId = EcoleA, Name = "Mathématiques", Level = "Lycée", Coefficient = 4 },
            new Subject { Id = MatiereB, SchoolId = EcoleB, Name = "Mathématiques", Level = "Lycée", Coefficient = 4 });

        owner.SchoolYears.AddRange(
            new SchoolYear { Id = AnneeA1, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true },
            new SchoolYear { Id = AnneeA2, SchoolId = EcoleA, Label = "2025-2026", StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) },
            new SchoolYear { Id = AnneeB, SchoolId = EcoleB, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true });

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // 1 — la RLS masque les surcharges des autres écoles.
    [Fact]
    public async Task RawSqlQuery_Should_Never_Return_Other_School_Overrides()
    {
        await InsertAsync(EcoleA, AnneeA1, MatiereA, series: "S2", coefficient: 6m);
        await InsertAsync(EcoleB, AnneeB, MatiereB, series: "S2", coefficient: 9m);

        var seen = await ReadCoefficientsAsync(EcoleA);

        seen.Should().ContainSingle().Which.Should().Be(6m);
        seen.Should().NotContain(9m, "la RLS doit masquer les surcharges des autres écoles");
    }

    // 2 — sans tenant, rien n'est visible.
    [Fact]
    public async Task Session_Without_Tenant_Should_See_No_Override_At_All()
    {
        await InsertAsync(EcoleA, AnneeA1, MatiereA, series: "S2", coefficient: 6m);

        (await ReadCoefficientsAsync(schoolId: null)).Should().BeEmpty();
    }

    // 3 — le WITH CHECK refuse d'écrire dans une autre école.
    [Fact]
    public async Task Writing_An_Override_Into_Another_School_Should_Be_Rejected()
    {
        var act = async () => await InsertAsync(
            sessionSchool: EcoleA, schoolId: EcoleB, yearId: AnneeB, subjectId: MatiereB, series: "S2", coefficient: 6m);

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    // 4 — unicité : une surcharge de série par (année, matière, série) ; la suppression logique libère la clé.
    [Fact]
    public async Task A_Series_Override_Is_Unique_Per_Year_And_Subject_Until_It_Is_Soft_Deleted()
    {
        var first = await InsertAsync(EcoleA, AnneeA1, MatiereA, series: "S2", coefficient: 6m);

        var duplicate = async () => await InsertAsync(EcoleA, AnneeA1, MatiereA, series: "S2", coefficient: 7m);
        await duplicate.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.UniqueViolation);

        // Autre année, autre série : deux clés différentes, aucun conflit.
        await InsertAsync(EcoleA, AnneeA2, MatiereA, series: "S2", coefficient: 7m);
        await InsertAsync(EcoleA, AnneeA1, MatiereA, series: "L2", coefficient: 2m);

        await SoftDeleteAsync(EcoleA, first);
        await InsertAsync(EcoleA, AnneeA1, MatiereA, series: "S2", coefficient: 8m); // recréation permise
    }

    [Fact]
    public async Task A_Classroom_Override_Is_Unique_Per_Year_And_Subject()
    {
        await InsertAsync(EcoleA, AnneeA1, MatiereA, classroomId: ClasseA, coefficient: 8m);

        var duplicate = async () => await InsertAsync(EcoleA, AnneeA1, MatiereA, classroomId: ClasseA, coefficient: 9m);

        await duplicate.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.UniqueViolation);
    }

    // 5 — exactement UNE portée : classe OU série.
    [Fact]
    public async Task An_Override_Must_Have_Exactly_One_Scope()
    {
        var both = async () => await InsertAsync(EcoleA, AnneeA1, MatiereA, series: "S2", classroomId: ClasseA, coefficient: 6m);
        await both.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.CheckViolation);

        var none = async () => await InsertAsync(EcoleA, AnneeA1, MatiereA, coefficient: 6m);
        await none.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.CheckViolation);
    }

    // 6 — la purge d'une année (mode test) emporte ses surcharges, et elles seules.
    [Fact]
    public async Task Deleting_A_School_Year_Removes_Its_Overrides_And_Keeps_The_Other_Years()
    {
        await InsertAsync(EcoleA, AnneeA1, MatiereA, series: "S2", coefficient: 6m);
        await InsertAsync(EcoleA, AnneeA2, MatiereA, series: "S2", coefficient: 7m);

        await using (var connection = await _db.OpenRawAppConnectionAsync(EcoleA))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT count(*) FROM delete_school_year(@school, @year);";
            command.Parameters.AddWithValue("school", EcoleA);
            command.Parameters.AddWithValue("year", AnneeA2);
            await command.ExecuteScalarAsync();
        }

        (await ReadCoefficientsAsync(EcoleA)).Should().ContainSingle().Which.Should().Be(6m);
    }

    // 7 — « Réinitialiser les données » emporte les surcharges AVANT matières et classes (FK RESTRICT).
    [Fact]
    public async Task Resetting_The_School_Data_Removes_The_Overrides_Without_A_Foreign_Key_Error()
    {
        await InsertAsync(EcoleA, AnneeA1, MatiereA, series: "S2", coefficient: 6m);
        await InsertAsync(EcoleA, AnneeA1, MatiereA, classroomId: ClasseA, coefficient: 8m);

        await using (var connection = await _db.OpenRawAppConnectionAsync(EcoleA))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT count(*) FROM reset_school_data(@school);";
            command.Parameters.AddWithValue("school", EcoleA);
            await command.ExecuteScalarAsync();
        }

        (await ReadCoefficientsAsync(EcoleA)).Should().BeEmpty();
    }

    private Task<Guid> InsertAsync(
        Guid schoolId, Guid yearId, Guid subjectId, string? series = null, Guid? classroomId = null, decimal coefficient = 1m)
        => InsertAsync(schoolId, schoolId, yearId, subjectId, series, classroomId, coefficient);

    private Task<Guid> InsertAsync(
        Guid sessionSchool, Guid schoolId, Guid yearId, Guid subjectId, string? series, decimal coefficient)
        => InsertAsync(sessionSchool, schoolId, yearId, subjectId, series, null, coefficient);

    private async Task<Guid> InsertAsync(
        Guid sessionSchool, Guid schoolId, Guid yearId, Guid subjectId, string? series, Guid? classroomId, decimal coefficient)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(sessionSchool);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO subject_coefficient_overrides
                ("Id", "SchoolId", "SchoolYearId", "SubjectId", "ClassroomId", "Series", "Coefficient", "CreatedAt", "IsDeleted")
            VALUES (@id, @schoolId, @yearId, @subjectId, @classroomId, @series, @coefficient, NOW(), FALSE);
            """;
        var id = Guid.NewGuid();
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("schoolId", schoolId);
        command.Parameters.AddWithValue("yearId", yearId);
        command.Parameters.AddWithValue("subjectId", subjectId);
        command.Parameters.AddWithValue("classroomId", (object?)classroomId ?? DBNull.Value);
        command.Parameters.AddWithValue("series", (object?)series ?? DBNull.Value);
        command.Parameters.AddWithValue("coefficient", coefficient);

        await command.ExecuteNonQueryAsync();
        return id;
    }

    private async Task SoftDeleteAsync(Guid schoolId, Guid id)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = """UPDATE subject_coefficient_overrides SET "IsDeleted" = TRUE, "DeletedAt" = NOW() WHERE "Id" = @id;""";
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<decimal>> ReadCoefficientsAsync(Guid? schoolId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "Coefficient" FROM subject_coefficient_overrides WHERE NOT "IsDeleted" ORDER BY "Coefficient";""";

        var values = new List<decimal>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetDecimal(0));
        }

        return values;
    }
}
