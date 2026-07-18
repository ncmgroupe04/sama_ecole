using FluentAssertions;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Npgsql;
using Xunit;

namespace SamaEcole.IntegrationTests.Attendance;

/// <summary>
/// Ticket JGK-D06 — l'isolation des fiches d'appel tient-elle dans la BASE ?
///
/// Comme pour les enseignants et les notes : tout est en SQL BRUT, avec le rôle applicatif et sans
/// EF Core. Le Global Query Filter n'entre donc pas en jeu — seule la policy RLS fait foi.
/// </summary>
[Trait("Category", "MultiTenant")]
public class AttendanceIsolationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid ClasseA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid ClasseB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid MatiereA = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");
    private static readonly Guid MatiereB = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");
    private static readonly Guid AnneeA = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid AnneeB = Guid.Parse("22222222-0000-0000-0000-000000000002");
    private static readonly Guid EleveA = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000e");
    private static readonly Guid EleveB = Guid.Parse("ffffffff-0000-0000-0000-00000000000f");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        // Jeu de données posé par le PROPRIÉTAIRE (exempté de RLS) : de quoi écrire une fiche d'appel
        // VALIDE dans chaque école. Toutes les entités référencées sont réelles, sans quoi une
        // insertion échouerait sur une clé étrangère (23503) et non sur la RLS.
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

        owner.Students.AddRange(
            new Student { Id = EleveA, SchoolId = EcoleA, Matricule = "ELEV-2026-0001", FullName = "Awa Fall", BirthDate = new DateOnly(2015, 3, 12), Gender = "F", ClassroomId = ClasseA },
            new Student { Id = EleveB, SchoolId = EcoleB, Matricule = "ELEV-2026-0001", FullName = "Modou Diop", BirthDate = new DateOnly(2014, 8, 2), Gender = "M", ClassroomId = ClasseB });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task RawSqlQuery_Should_Never_Return_Other_School_Attendance_Sheets()
    {
        await InsertSheetWithLineAsync(EcoleA, ClasseA, MatiereA, AnneeA, EleveA, "Matin");
        await InsertSheetWithLineAsync(EcoleB, ClasseB, MatiereB, AnneeB, EleveB, "Matin");

        var periods = await ReadSheetClassroomsAsync(EcoleA);

        periods.Should().ContainSingle().Which.Should().Be(ClasseA);
        periods.Should().NotContain(ClasseB, "la RLS doit masquer les fiches d'appel des autres écoles");
    }

    [Fact]
    public async Task Session_Without_Tenant_Should_See_No_Attendance_At_All()
    {
        await InsertSheetWithLineAsync(EcoleA, ClasseA, MatiereA, AnneeA, EleveA, "Matin");

        var sheets = await ReadSheetClassroomsAsync(schoolId: null);
        sheets.Should().BeEmpty();

        var lines = await ReadStudentAttendanceCountAsync(schoolId: null);
        lines.Should().Be(0);
    }

    [Fact]
    public async Task Writing_A_Sheet_Into_Another_School_Should_Be_Rejected()
    {
        // Session sur l'École A qui tente d'écrire une fiche pour l'École B, en visant des entités
        // RÉELLES de B : seul le WITH CHECK de la policy peut alors refuser la ligne.
        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO attendance_sheets
                ("Id", "SchoolId", "ClassroomId", "SubjectId", "SchoolYearId", "Date", "Period", "TakenByUserId", "CreatedAt", "IsDeleted")
            VALUES (gen_random_uuid(), @schoolId, @classroomId, @subjectId, @schoolYearId, DATE '2026-11-05', 'Matin', gen_random_uuid(), NOW(), FALSE);
            """;
        command.Parameters.AddWithValue("schoolId", EcoleB);
        command.Parameters.AddWithValue("classroomId", ClasseB);
        command.Parameters.AddWithValue("subjectId", MatiereB);
        command.Parameters.AddWithValue("schoolYearId", AnneeB);

        var act = async () => await command.ExecuteNonQueryAsync();

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task Same_Class_Subject_Date_And_Period_Cannot_Be_Recorded_Twice()
    {
        await InsertSheetWithLineAsync(EcoleA, ClasseA, MatiereA, AnneeA, EleveA, "Matin");

        var act = async () => await InsertSheetAsync(EcoleA, ClasseA, MatiereA, AnneeA, "Matin");

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.UniqueViolation);
    }

    private async Task<Guid> InsertSheetAsync(Guid schoolId, Guid classroomId, Guid subjectId, Guid schoolYearId, string period)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO attendance_sheets
                ("Id", "SchoolId", "ClassroomId", "SubjectId", "SchoolYearId", "Date", "Period", "TakenByUserId", "CreatedAt", "IsDeleted")
            VALUES (@id, @schoolId, @classroomId, @subjectId, @schoolYearId, DATE '2026-11-05', @period, gen_random_uuid(), NOW(), FALSE);
            """;
        var id = Guid.NewGuid();
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("schoolId", schoolId);
        command.Parameters.AddWithValue("classroomId", classroomId);
        command.Parameters.AddWithValue("subjectId", subjectId);
        command.Parameters.AddWithValue("schoolYearId", schoolYearId);
        command.Parameters.AddWithValue("period", period);

        await command.ExecuteNonQueryAsync();
        return id;
    }

    private async Task InsertSheetWithLineAsync(
        Guid schoolId, Guid classroomId, Guid subjectId, Guid schoolYearId, Guid studentId, string period)
    {
        var sheetId = await InsertSheetAsync(schoolId, classroomId, subjectId, schoolYearId, period);

        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO student_attendances
                ("Id", "SchoolId", "AttendanceSheetId", "StudentId", "Status", "LateMinutes", "CreatedAt", "IsDeleted")
            VALUES (gen_random_uuid(), @schoolId, @sheetId, @studentId, 'Present', 0, NOW(), FALSE);
            """;
        command.Parameters.AddWithValue("schoolId", schoolId);
        command.Parameters.AddWithValue("sheetId", sheetId);
        command.Parameters.AddWithValue("studentId", studentId);

        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<Guid>> ReadSheetClassroomsAsync(Guid? schoolId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);

        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "ClassroomId" FROM attendance_sheets ORDER BY "ClassroomId";""";

        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    private async Task<long> ReadStudentAttendanceCountAsync(Guid? schoolId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);

        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT COUNT(*) FROM student_attendances;""";

        return (long)(await command.ExecuteScalarAsync())!;
    }
}
