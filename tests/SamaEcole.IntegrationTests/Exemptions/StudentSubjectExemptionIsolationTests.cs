using FluentAssertions;
using Npgsql;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Exemptions;

/// <summary>
/// Dispense d'une matière obligatoire — la table <c>student_subject_exemptions</c> tient-elle son isolation, son
/// unicité et ses purges DANS LA BASE ? SQL BRUT sous le rôle applicatif, sans EF Core : seuls la policy RLS, les
/// index et les fonctions de purge font foi.
/// </summary>
[Trait("Category", "MultiTenant")]
public class StudentSubjectExemptionIsolationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("a1111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("a2222222-2222-2222-2222-222222222222");
    private static readonly Guid AnneeA1 = Guid.Parse("a1111111-0000-0000-0000-000000000001");
    private static readonly Guid AnneeA2 = Guid.Parse("a1111111-0000-0000-0000-000000000002");
    private static readonly Guid AnneeB = Guid.Parse("a2222222-0000-0000-0000-000000000001");
    private static readonly Guid ClasseA = Guid.Parse("a1111111-0000-0000-0000-0000000000c1");
    private static readonly Guid ClasseB = Guid.Parse("a2222222-0000-0000-0000-0000000000c1");
    private static readonly Guid EpsA = Guid.Parse("a1111111-0000-0000-0000-0000000000b1");
    private static readonly Guid EpsB = Guid.Parse("a2222222-0000-0000-0000-0000000000b1");
    private static readonly Guid EleveA = Guid.Parse("a1111111-0000-0000-0000-0000000000e1");
    private static readonly Guid EleveB = Guid.Parse("a2222222-0000-0000-0000-0000000000e1");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" });
        owner.SchoolYears.AddRange(
            new SchoolYear { Id = AnneeA1, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true },
            new SchoolYear { Id = AnneeA2, SchoolId = EcoleA, Label = "2025-2026", StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) },
            new SchoolYear { Id = AnneeB, SchoolId = EcoleB, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true });
        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 });
        owner.Subjects.AddRange(
            new Subject { Id = EpsA, SchoolId = EcoleA, Name = "EPS", Level = "Collège", Coefficient = 1 },
            new Subject { Id = EpsB, SchoolId = EcoleB, Name = "EPS", Level = "Collège", Coefficient = 1 });
        owner.Students.AddRange(
            new Student { Id = EleveA, SchoolId = EcoleA, Matricule = "ELEV-A1", FullName = "Awa A", BirthDate = new DateOnly(2011, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA },
            new Student { Id = EleveB, SchoolId = EcoleB, Matricule = "ELEV-B1", FullName = "Awa B", BirthDate = new DateOnly(2011, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseB });

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task RawSqlQuery_Should_Never_Return_Other_School_Exemptions()
    {
        await InsertAsync(EcoleA, EcoleA, EleveA, EpsA, AnneeA1);
        await InsertAsync(EcoleB, EcoleB, EleveB, EpsB, AnneeB);

        (await CountAsync(EcoleA)).Should().Be(1);
        (await CountAsync(EcoleB)).Should().Be(1);
    }

    [Fact]
    public async Task Session_Without_Tenant_Should_See_No_Exemption_At_All()
    {
        await InsertAsync(EcoleA, EcoleA, EleveA, EpsA, AnneeA1);

        (await CountAsync(schoolId: null)).Should().Be(0);
    }

    [Fact]
    public async Task Writing_An_Exemption_Into_Another_School_Should_Be_Rejected()
    {
        var act = async () => await InsertAsync(EcoleA, EcoleB, EleveB, EpsB, AnneeB);

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task An_Exemption_Is_Unique_Per_Student_Subject_And_Year_Until_It_Is_Soft_Deleted()
    {
        var first = await InsertAsync(EcoleA, EcoleA, EleveA, EpsA, AnneeA1);

        var duplicate = async () => await InsertAsync(EcoleA, EcoleA, EleveA, EpsA, AnneeA1);
        await duplicate.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.UniqueViolation);

        await InsertAsync(EcoleA, EcoleA, EleveA, EpsA, AnneeA2);   // autre année : aucun conflit

        await SoftDeleteAsync(EcoleA, first);
        await InsertAsync(EcoleA, EcoleA, EleveA, EpsA, AnneeA1);   // recréation permise
    }

    [Fact]
    public async Task The_Reason_Is_Mandatory_And_Bounded_By_The_Database()
    {
        await InsertAsync(EcoleA, EcoleA, EleveA, EpsA, AnneeA1, reason: new string('x', 200));

        var tooLong = async () => await InsertAsync(EcoleA, EcoleA, EleveA, EpsA, AnneeA2, reason: new string('x', 201));
        await tooLong.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.StringDataRightTruncation);

        var missing = async () => await InsertAsync(EcoleA, EcoleA, EleveA, EpsA, AnneeA2, reason: null);
        await missing.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.NotNullViolation);
    }

    [Fact]
    public async Task Deleting_A_School_Year_Removes_Its_Exemptions_And_Keeps_The_Other_Years()
    {
        await InsertAsync(EcoleA, EcoleA, EleveA, EpsA, AnneeA1);
        var oldYear = await InsertAsync(EcoleA, EcoleA, EleveA, EpsA, AnneeA2);

        await using (var connection = await _db.OpenRawAppConnectionAsync(EcoleA))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT count(*) FROM delete_school_year(@school, @year);";
            command.Parameters.AddWithValue("school", EcoleA);
            command.Parameters.AddWithValue("year", AnneeA2);
            await command.ExecuteScalarAsync();
        }

        (await CountAsync(EcoleA)).Should().Be(1);
        (await YearsOfSurvivorsAsync(EcoleA)).Should().Equal(AnneeA1);
        oldYear.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Resetting_The_School_Data_Removes_The_Exemptions_Without_A_Foreign_Key_Error()
    {
        await InsertAsync(EcoleA, EcoleA, EleveA, EpsA, AnneeA1);

        await using (var connection = await _db.OpenRawAppConnectionAsync(EcoleA))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT count(*) FROM reset_school_data(@school);";
            command.Parameters.AddWithValue("school", EcoleA);
            await command.ExecuteScalarAsync();
        }

        (await CountAsync(EcoleA)).Should().Be(0);
    }

    private async Task<Guid> InsertAsync(
        Guid sessionSchool, Guid schoolId, Guid studentId, Guid subjectId, Guid yearId, string? reason = "Inaptitude médicale")
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(sessionSchool);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO student_subject_exemptions
                ("Id", "SchoolId", "StudentId", "SubjectId", "SchoolYearId", "Reason", "CreatedAt", "IsDeleted")
            VALUES (@id, @schoolId, @studentId, @subjectId, @yearId, @reason, NOW(), FALSE);
            """;
        var id = Guid.NewGuid();
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("schoolId", schoolId);
        command.Parameters.AddWithValue("studentId", studentId);
        command.Parameters.AddWithValue("subjectId", subjectId);
        command.Parameters.AddWithValue("yearId", yearId);
        command.Parameters.AddWithValue("reason", (object?)reason ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private async Task SoftDeleteAsync(Guid schoolId, Guid id)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = """UPDATE student_subject_exemptions SET "IsDeleted" = TRUE, "DeletedAt" = NOW() WHERE "Id" = @id;""";
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> CountAsync(Guid? schoolId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT count(*) FROM student_subject_exemptions WHERE NOT "IsDeleted";""";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<List<Guid>> YearsOfSurvivorsAsync(Guid schoolId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "SchoolYearId" FROM student_subject_exemptions WHERE NOT "IsDeleted";""";
        var years = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            years.Add(reader.GetGuid(0));
        }

        return years;
    }
}
