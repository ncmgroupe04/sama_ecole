using FluentAssertions;
using Npgsql;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.OptionalSubjects;

/// <summary>
/// Matières optionnelles — la table <c>enrollment_subject_exemptions</c> tient-elle son isolation, son
/// unicité et ses purges DANS LA BASE ? SQL BRUT sous le rôle applicatif, sans EF Core : seuls la
/// policy RLS, les index et les fonctions de purge font foi (même méthode que
/// <c>CoefficientOverrideIsolationTests</c>).
/// </summary>
[Trait("Category", "MultiTenant")]
public class EnrollmentExemptionIsolationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("91111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("92222222-2222-2222-2222-222222222222");

    private static readonly Guid AnneeA1 = Guid.Parse("91111111-0000-0000-0000-000000000001");
    private static readonly Guid AnneeA2 = Guid.Parse("91111111-0000-0000-0000-000000000002");
    private static readonly Guid AnneeB = Guid.Parse("92222222-0000-0000-0000-000000000001");

    private static readonly Guid ClasseA = Guid.Parse("9aaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid ClasseB = Guid.Parse("9bbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid EleveA = Guid.Parse("9eeeeeee-0000-0000-0000-0000000000a1");
    private static readonly Guid EleveB = Guid.Parse("9eeeeeee-0000-0000-0000-0000000000b1");

    private static readonly Guid InscriptionA1 = Guid.Parse("9fffffff-0000-0000-0000-0000000000a1");
    private static readonly Guid InscriptionA2 = Guid.Parse("9fffffff-0000-0000-0000-0000000000a2");
    private static readonly Guid InscriptionB = Guid.Parse("9fffffff-0000-0000-0000-0000000000b1");

    private static readonly Guid ArabeA = Guid.Parse("9ccccccc-0000-0000-0000-0000000000a1");
    private static readonly Guid ArabeB = Guid.Parse("9ccccccc-0000-0000-0000-0000000000b1");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        owner.SchoolYears.AddRange(
            new SchoolYear { Id = AnneeA1, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true },
            new SchoolYear { Id = AnneeA2, SchoolId = EcoleA, Label = "2025-2026", StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) },
            new SchoolYear { Id = AnneeB, SchoolId = EcoleB, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true });

        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 });

        owner.Subjects.AddRange(
            new Subject { Id = ArabeA, SchoolId = EcoleA, Name = "Arabe", Level = "Collège", Coefficient = 2, IsOptional = true, OptionGroup = "LV2" },
            new Subject { Id = ArabeB, SchoolId = EcoleB, Name = "Arabe", Level = "Collège", Coefficient = 2, IsOptional = true, OptionGroup = "LV2" });

        owner.Students.AddRange(
            NewStudent(EleveA, EcoleA, "ELEV-A1", "Awa A", ClasseA),
            NewStudent(EleveB, EcoleB, "ELEV-B1", "Awa B", ClasseB));

        owner.Enrollments.AddRange(
            NewEnrollment(InscriptionA1, EcoleA, EleveA, AnneeA1, ClasseA, "R-A1"),
            NewEnrollment(InscriptionA2, EcoleA, EleveA, AnneeA2, ClasseA, "R-A2"),
            NewEnrollment(InscriptionB, EcoleB, EleveB, AnneeB, ClasseB, "R-B1"));

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // 1 — la RLS masque les dispenses des autres écoles.
    [Fact]
    public async Task RawSqlQuery_Should_Never_Return_Other_School_Exemptions()
    {
        await InsertAsync(EcoleA, EcoleA, InscriptionA1, ArabeA);
        await InsertAsync(EcoleB, EcoleB, InscriptionB, ArabeB);

        (await CountAsync(EcoleA)).Should().Be(1);
        (await CountAsync(EcoleB)).Should().Be(1);
    }

    // 2 — sans tenant, rien n'est visible.
    [Fact]
    public async Task Session_Without_Tenant_Should_See_No_Exemption_At_All()
    {
        await InsertAsync(EcoleA, EcoleA, InscriptionA1, ArabeA);

        (await CountAsync(schoolId: null)).Should().Be(0);
    }

    // 3 — le WITH CHECK refuse d'écrire dans une autre école.
    [Fact]
    public async Task Writing_An_Exemption_Into_Another_School_Should_Be_Rejected()
    {
        var act = async () => await InsertAsync(sessionSchool: EcoleA, schoolId: EcoleB, InscriptionB, ArabeB);

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    // 4 — unicité par (inscription, matière) ; la suppression logique libère la clé.
    [Fact]
    public async Task An_Exemption_Is_Unique_Per_Enrollment_And_Subject_Until_It_Is_Soft_Deleted()
    {
        var first = await InsertAsync(EcoleA, EcoleA, InscriptionA1, ArabeA);

        var duplicate = async () => await InsertAsync(EcoleA, EcoleA, InscriptionA1, ArabeA);
        await duplicate.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.UniqueViolation);

        await SoftDeleteAsync(EcoleA, first);
        await InsertAsync(EcoleA, EcoleA, InscriptionA1, ArabeA); // recréation permise
    }

    // 5 — la purge d'une année (mode test) emporte les dispenses de ses inscriptions, et elles seules.
    [Fact]
    public async Task Deleting_A_School_Year_Removes_Its_Exemptions_And_Keeps_The_Other_Years()
    {
        await InsertAsync(EcoleA, EcoleA, InscriptionA1, ArabeA);
        await InsertAsync(EcoleA, EcoleA, InscriptionA2, ArabeA);

        await using (var connection = await _db.OpenRawAppConnectionAsync(EcoleA))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT count(*) FROM delete_school_year(@school, @year);";
            command.Parameters.AddWithValue("school", EcoleA);
            command.Parameters.AddWithValue("year", AnneeA2);
            await command.ExecuteScalarAsync();
        }

        (await CountAsync(EcoleA)).Should().Be(1);
    }

    // 6 — « Réinitialiser les données » emporte les dispenses AVANT inscriptions et matières (FK RESTRICT).
    [Fact]
    public async Task Resetting_The_School_Data_Removes_The_Exemptions_Without_A_Foreign_Key_Error()
    {
        await InsertAsync(EcoleA, EcoleA, InscriptionA1, ArabeA);

        await using (var connection = await _db.OpenRawAppConnectionAsync(EcoleA))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT count(*) FROM reset_school_data(@school);";
            command.Parameters.AddWithValue("school", EcoleA);
            await command.ExecuteScalarAsync();
        }

        (await CountAsync(EcoleA)).Should().Be(0);
    }

    // 7 — le motif est borné À LA BASE (varchar(200)) : un client qui oublierait la validation ne l'élargit pas.
    [Fact]
    public async Task A_Reason_Is_Stored_Up_To_Two_Hundred_Characters_And_Longer_Is_Rejected_By_The_Database()
    {
        await InsertAsync(EcoleA, EcoleA, InscriptionA1, ArabeA, reason: new string('x', 200));

        var tooLong = async () => await InsertAsync(EcoleA, EcoleA, InscriptionA2, ArabeA, reason: new string('x', 201));

        await tooLong.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.StringDataRightTruncation);
    }

    private async Task<Guid> InsertAsync(Guid sessionSchool, Guid schoolId, Guid enrollmentId, Guid subjectId, string? reason = null)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(sessionSchool);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO enrollment_subject_exemptions
                ("Id", "SchoolId", "EnrollmentId", "SubjectId", "Reason", "CreatedAt", "IsDeleted")
            VALUES (@id, @schoolId, @enrollmentId, @subjectId, @reason, NOW(), FALSE);
            """;
        var id = Guid.NewGuid();
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("schoolId", schoolId);
        command.Parameters.AddWithValue("enrollmentId", enrollmentId);
        command.Parameters.AddWithValue("subjectId", subjectId);
        command.Parameters.AddWithValue("reason", (object?)reason ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private async Task SoftDeleteAsync(Guid schoolId, Guid id)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = """UPDATE enrollment_subject_exemptions SET "IsDeleted" = TRUE, "DeletedAt" = NOW() WHERE "Id" = @id;""";
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> CountAsync(Guid? schoolId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT count(*) FROM enrollment_subject_exemptions WHERE NOT "IsDeleted";""";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static Student NewStudent(Guid id, Guid schoolId, string matricule, string name, Guid classroomId) => new()
    {
        Id = id, SchoolId = schoolId, Matricule = matricule, FullName = name,
        BirthDate = new DateOnly(2011, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = classroomId
    };

    private static Enrollment NewEnrollment(Guid id, Guid schoolId, Guid studentId, Guid yearId, Guid classroomId, string receipt) => new()
    {
        Id = id, SchoolId = schoolId, StudentId = studentId, SchoolYearId = yearId, ClassroomId = classroomId,
        Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, ReceiptNumber = receipt
    };
}
