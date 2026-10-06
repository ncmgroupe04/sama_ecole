using FluentAssertions;
using Npgsql;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Boarding;

/// <summary>
/// Modèle Pavillon/Lit de l'Internat (spec 2026-10-06 §3) — les six tables tiennent-elles leur isolation, leurs
/// unicités et leurs bornes DANS LA BASE ? SQL brut sous le rôle applicatif : seuls la policy RLS, les index
/// partiels et les CHECK font foi.
/// </summary>
[Trait("Category", "MultiTenant")]
public class BoardingSchemaIsolationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();
    private BoardingSqlSeed _sql = null!;

    private static readonly Guid EcoleA = Guid.Parse("c1111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("c2222222-2222-2222-2222-222222222222");
    private static readonly Guid AnneeA = Guid.Parse("c1111111-0000-0000-0000-000000000001");
    private static readonly Guid AnneeB = Guid.Parse("c2222222-0000-0000-0000-000000000001");
    private static readonly Guid ClasseA = Guid.Parse("c1111111-0000-0000-0000-0000000000c1");
    private static readonly Guid ClasseB = Guid.Parse("c2222222-0000-0000-0000-0000000000c1");
    private static readonly Guid EleveA = Guid.Parse("c1111111-0000-0000-0000-0000000000e1");
    private static readonly Guid EleveB = Guid.Parse("c2222222-0000-0000-0000-0000000000e1");
    private static readonly Guid InscriptionA = Guid.Parse("c1111111-0000-0000-0000-0000000000f1");
    private static readonly Guid InscriptionB = Guid.Parse("c2222222-0000-0000-0000-0000000000f1");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        _sql = new BoardingSqlSeed(_db);

        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" });
        owner.SchoolYears.AddRange(
            new SchoolYear { Id = AnneeA, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true },
            new SchoolYear { Id = AnneeB, SchoolId = EcoleB, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true });
        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 });
        owner.Students.AddRange(
            new Student { Id = EleveA, SchoolId = EcoleA, Matricule = "ELEV-A1", FullName = "Awa A", BirthDate = new DateOnly(2011, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA },
            new Student { Id = EleveB, SchoolId = EcoleB, Matricule = "ELEV-B1", FullName = "Awa B", BirthDate = new DateOnly(2011, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseB });
        owner.Enrollments.AddRange(
            new Enrollment { Id = InscriptionA, SchoolId = EcoleA, StudentId = EleveA, SchoolYearId = AnneeA, ClassroomId = ClasseA, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, ReceiptNumber = "REC-A-0001", EnrolledAt = DateTimeOffset.UtcNow },
            new Enrollment { Id = InscriptionB, SchoolId = EcoleB, StudentId = EleveB, SchoolYearId = AnneeB, ClassroomId = ClasseB, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, ReceiptNumber = "REC-B-0001", EnrolledAt = DateTimeOffset.UtcNow });
        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<(Guid Dormitory, Guid Room, Guid Bed)> ChainAAsync(int bedNumber = 1)
    {
        var dormitory = await _sql.InsertDormitoryAsync(EcoleA, EcoleA);
        var room = await _sql.InsertRoomAsync(EcoleA, EcoleA, dormitory);
        var bed = await _sql.InsertBedAsync(EcoleA, EcoleA, room, bedNumber);
        return (dormitory, room, bed);
    }

    // ------------------------------------------------------------------ RLS

    [Fact]
    public async Task Raw_Query_Should_Never_Return_Other_School_Dormitories()
    {
        await _sql.InsertDormitoryAsync(EcoleA, EcoleA);
        await _sql.InsertDormitoryAsync(EcoleB, EcoleB);

        (await _sql.CountAsync(EcoleA, "dormitories")).Should().Be(1);
        (await _sql.CountAsync(EcoleB, "dormitories")).Should().Be(1);
        (await _sql.CountAsync(null, "dormitories")).Should().Be(0, "sans tenant dans la session, rien n'est visible");
    }

    [Fact]
    public async Task Writing_A_Dormitory_Into_Another_School_Should_Be_Rejected()
    {
        var act = async () => await _sql.InsertDormitoryAsync(EcoleA, EcoleB);

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    [Theory]
    [InlineData("dormitories")]
    [InlineData("dormitory_rooms")]
    [InlineData("beds")]
    [InlineData("boarding_enrollments")]
    [InlineData("boarding_leaves")]
    [InlineData("boarding_attendances")]
    public async Task The_Application_Role_Cannot_Physically_Delete_Any_Boarding_Row(string table)
    {
        var act = async () => await _sql.ExecuteAsync(EcoleA, $"DELETE FROM {table}");

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege, "aucune suppression physique (règle #6)");
    }

    // ------------------------------------------------------------------ Unicités

    [Fact]
    public async Task A_Dormitory_Name_Is_Unique_Per_School_Until_Soft_Deleted()
    {
        var first = await _sql.InsertDormitoryAsync(EcoleA, EcoleA, "Pavillon Oustaz Ahmad");

        var duplicate = async () => await _sql.InsertDormitoryAsync(EcoleA, EcoleA, "Pavillon Oustaz Ahmad");
        await duplicate.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.UniqueViolation);

        await _sql.InsertDormitoryAsync(EcoleB, EcoleB, "Pavillon Oustaz Ahmad"); // autre école : aucun conflit

        await _sql.ExecuteAsync(EcoleA, """UPDATE dormitories SET "IsDeleted" = TRUE WHERE "Id" = @id""", ("id", first));
        await _sql.InsertDormitoryAsync(EcoleA, EcoleA, "Pavillon Oustaz Ahmad"); // recréation permise
    }

    [Fact]
    public async Task A_Bed_Can_Be_Held_By_One_Active_Boarder_Only()
    {
        var (_, _, bed) = await ChainAAsync();
        var first = await _sql.InsertBoarderAsync(EcoleA, EcoleA, EleveA, InscriptionA, bed: bed);

        // Second séjour actif sur le même lit (autre inscription, donc seul l'index du lit peut l'arrêter).
        var otherEnrollment = await SeedExtraEnrollmentAsync();
        var duplicate = async () => await _sql.InsertBoarderAsync(EcoleA, EcoleA, EleveA, otherEnrollment, bed: bed);
        await duplicate.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.UniqueViolation);

        // Clore le premier séjour libère le lit.
        await _sql.ExecuteAsync(EcoleA, """
            UPDATE boarding_enrollments SET "IsActive" = FALSE, "EndDate" = DATE '2026-10-01', "BedId" = NULL WHERE "Id" = @id
            """, ("id", first));
        await _sql.InsertBoarderAsync(EcoleA, EcoleA, EleveA, otherEnrollment, bed: bed);
    }

    [Fact]
    public async Task An_Enrollment_Has_One_Active_Boarding_Stay()
    {
        await _sql.InsertBoarderAsync(EcoleA, EcoleA, EleveA, InscriptionA);

        var act = async () => await _sql.InsertBoarderAsync(EcoleA, EcoleA, EleveA, InscriptionA);

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task A_Boarder_Has_At_Most_One_Open_Leave()
    {
        var boarder = await _sql.InsertBoarderAsync(EcoleA, EcoleA, EleveA, InscriptionA);
        var first = await _sql.InsertLeaveAsync(EcoleA, EcoleA, boarder);

        var second = async () => await _sql.InsertLeaveAsync(EcoleA, EcoleA, boarder, "2026-10-10", "2026-10-12");
        await second.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.UniqueViolation);

        await _sql.ExecuteAsync(EcoleA, """UPDATE boarding_leaves SET "ActualReturnDate" = DATE '2026-10-04' WHERE "Id" = @id""", ("id", first));
        await _sql.InsertLeaveAsync(EcoleA, EcoleA, boarder, "2026-10-10", "2026-10-12");
    }

    [Fact]
    public async Task Attendance_Is_Unique_Per_Boarder_And_Night_Until_Soft_Deleted()
    {
        var boarder = await _sql.InsertBoarderAsync(EcoleA, EcoleA, EleveA, InscriptionA);
        var first = await _sql.InsertAttendanceAsync(EcoleA, EcoleA, boarder);

        var duplicate = async () => await _sql.InsertAttendanceAsync(EcoleA, EcoleA, boarder);
        await duplicate.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.UniqueViolation);

        await _sql.InsertAttendanceAsync(EcoleA, EcoleA, boarder, "2026-10-03"); // autre nuit : aucun conflit

        await _sql.ExecuteAsync(EcoleA, """UPDATE boarding_attendances SET "IsDeleted" = TRUE WHERE "Id" = @id""", ("id", first));
        await _sql.InsertAttendanceAsync(EcoleA, EcoleA, boarder);
    }

    // ------------------------------------------------------------------ CHECK

    [Fact]
    public async Task A_Half_Boarder_Cannot_Hold_A_Bed()
    {
        var (_, _, bed) = await ChainAAsync();

        var act = async () => await _sql.InsertBoarderAsync(EcoleA, EcoleA, EleveA, InscriptionA, "DemiPensionnaire", bed);

        await act.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task An_Inactive_Stay_Cannot_Hold_A_Bed_And_Must_Have_An_End_Date()
    {
        var (_, _, bed) = await ChainAAsync();

        var withBed = async () => await _sql.InsertBoarderAsync(EcoleA, EcoleA, EleveA, InscriptionA, bed: bed, active: false);
        await withBed.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.CheckViolation);

        var noEnd = async () => await _sql.ExecuteAsync(EcoleA, """
            INSERT INTO boarding_enrollments ("Id","SchoolId","StudentId","EnrollmentId","Regime","StartDate","IsActive","AllowedExitPersons","CreatedAt","IsDeleted")
            VALUES (gen_random_uuid(),@school,@student,@enrollment,'Interne',DATE '2026-09-15',FALSE,'[]'::jsonb,NOW(),FALSE)
            """, ("school", EcoleA), ("student", EleveA), ("enrollment", InscriptionA));
        await noEnd.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task A_Bed_Cannot_Be_Stored_As_Occupied()
    {
        var (_, room, _) = await ChainAAsync();

        var act = async () => await _sql.InsertBedAsync(EcoleA, EcoleA, room, 2, "Occupied");
        await act.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.CheckViolation);

        await _sql.InsertBedAsync(EcoleA, EcoleA, room, 2, "Maintenance"); // statut stockable
    }

    [Fact]
    public async Task A_Leave_Cannot_Return_Before_It_Starts()
    {
        var boarder = await _sql.InsertBoarderAsync(EcoleA, EcoleA, EleveA, InscriptionA);

        var act = async () => await _sql.InsertLeaveAsync(EcoleA, EcoleA, boarder, "2026-10-05", "2026-10-04");

        await act.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.CheckViolation);
    }

    // ------------------------------------------------------------------ FK composites anti cross-tenant

    [Fact]
    public async Task A_Boarder_Cannot_Reference_A_Bed_Of_Another_School()
    {
        var dormitoryB = await _sql.InsertDormitoryAsync(EcoleB, EcoleB);
        var roomB = await _sql.InsertRoomAsync(EcoleB, EcoleB, dormitoryB);
        var bedB = await _sql.InsertBedAsync(EcoleB, EcoleB, roomB);

        var act = async () => await _sql.InsertBoarderAsync(EcoleA, EcoleA, EleveA, InscriptionA, bed: bedB);

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task A_Room_Cannot_Reference_A_Dormitory_Of_Another_School()
    {
        var dormitoryB = await _sql.InsertDormitoryAsync(EcoleB, EcoleB);

        var act = async () => await _sql.InsertRoomAsync(EcoleA, EcoleA, dormitoryB);

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.ForeignKeyViolation);
    }

    /// <summary>Seconde inscription de l'école A (autre élève) pour isoler l'index « un séjour actif par lit ».</summary>
    private async Task<Guid> SeedExtraEnrollmentAsync()
    {
        var student = Guid.NewGuid();
        var enrollment = Guid.NewGuid();
        await using var owner = _db.NewOwnerContext();
        owner.Students.Add(new Student { Id = student, SchoolId = EcoleA, Matricule = "ELEV-A2", FullName = "Binta A", BirthDate = new DateOnly(2011, 2, 2), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA });
        owner.Enrollments.Add(new Enrollment { Id = enrollment, SchoolId = EcoleA, StudentId = student, SchoolYearId = AnneeA, ClassroomId = ClasseA, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, ReceiptNumber = "REC-A-0002", EnrolledAt = DateTimeOffset.UtcNow });
        await owner.SaveChangesAsync();
        return enrollment;
    }
}
