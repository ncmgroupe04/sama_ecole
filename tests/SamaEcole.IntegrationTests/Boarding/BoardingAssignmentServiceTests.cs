using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Boarding.Assignments;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.Boarding;

/// <summary>
/// Règle d'affectation unique (lot C, tâche 2) — créer/transférer/clore le séjour d'une inscription, contrôler le lit
/// (maintenance, genre, occupation) et ajouter la pension une seule fois. Contre un vrai PostgreSQL : les index uniques
/// partiels du lot A tranchent les courses.
/// </summary>
[Trait("Category", "MultiTenant")]
public class BoardingAssignmentServiceTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("e8111111-1111-1111-1111-111111111111");
    private static readonly Guid AnneeA = Guid.Parse("e8111111-0000-0000-0000-000000000001");
    private static readonly Guid ClasseA = Guid.Parse("e8111111-0000-0000-0000-0000000000c1");
    private static readonly Guid CatPension = Guid.Parse("e8111111-0000-0000-0000-0000000000f0");
    private static readonly Guid PavGarcons = Guid.Parse("e8111111-0000-0000-0000-00000000b001");
    private static readonly Guid PavFilles = Guid.Parse("e8111111-0000-0000-0000-00000000b002");
    private static readonly Guid PavMixte = Guid.Parse("e8111111-0000-0000-0000-00000000b003");
    private static readonly Guid R101 = Guid.Parse("e8111111-0000-0000-0000-00000000a101");
    private static readonly Guid R201 = Guid.Parse("e8111111-0000-0000-0000-00000000a201");
    private static readonly Guid R301 = Guid.Parse("e8111111-0000-0000-0000-00000000a301");
    private static readonly Guid B101a = Guid.Parse("e8111111-0000-0000-0000-0000000b1011");
    private static readonly Guid B101b = Guid.Parse("e8111111-0000-0000-0000-0000000b1012");
    private static readonly Guid B201Libre = Guid.Parse("e8111111-0000-0000-0000-0000000b2011");
    private static readonly Guid B201Maintenance = Guid.Parse("e8111111-0000-0000-0000-0000000b2012");
    private static readonly Guid B301 = Guid.Parse("e8111111-0000-0000-0000-0000000b3011");

    private readonly Dictionary<string, Guid> _enrollments = new();
    private readonly Dictionary<string, Guid> _students = new();

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = EcoleA, Name = "École A" });
        owner.SchoolYears.Add(new SchoolYear { Id = AnneeA, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true });
        owner.Classrooms.Add(new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 60 });
        owner.FeeCategories.Add(new FeeCategory { Id = CatPension, SchoolId = EcoleA, Name = "Pension", IsRecurring = true, IsBoardingFee = true });
        owner.ClassFees.Add(new ClassFee { SchoolId = EcoleA, FeeCategoryId = CatPension, ClassroomId = ClasseA, Amount = 20_000m });

        owner.Dormitories.AddRange(
            new Dormitory { Id = PavGarcons, SchoolId = EcoleA, Name = "Garçons", Gender = DormitoryGender.Garcons },
            new Dormitory { Id = PavFilles, SchoolId = EcoleA, Name = "Filles", Gender = DormitoryGender.Filles },
            new Dormitory { Id = PavMixte, SchoolId = EcoleA, Name = "Mixte", Gender = DormitoryGender.Mixte });
        owner.DormitoryRooms.AddRange(
            new DormitoryRoom { Id = R101, SchoolId = EcoleA, DormitoryId = PavGarcons, Name = "101" },
            new DormitoryRoom { Id = R201, SchoolId = EcoleA, DormitoryId = PavFilles, Name = "201" },
            new DormitoryRoom { Id = R301, SchoolId = EcoleA, DormitoryId = PavMixte, Name = "301" });
        owner.Beds.AddRange(
            new Bed { Id = B101a, SchoolId = EcoleA, DormitoryRoomId = R101, BedNumber = 1 },
            new Bed { Id = B101b, SchoolId = EcoleA, DormitoryRoomId = R101, BedNumber = 2 },
            new Bed { Id = B201Libre, SchoolId = EcoleA, DormitoryRoomId = R201, BedNumber = 1 },
            new Bed { Id = B201Maintenance, SchoolId = EcoleA, DormitoryRoomId = R201, BedNumber = 2, Status = BedStatus.Maintenance },
            new Bed { Id = B301, SchoolId = EcoleA, DormitoryRoomId = R301, BedNumber = 1 });

        AddStudent(owner, "m1", "M");
        AddStudent(owner, "m2", "M");
        AddStudent(owner, "m3", "M");
        AddStudent(owner, "f1", "F");
        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private void AddStudent(ApplicationDbContext owner, string key, string gender)
    {
        var student = new Student
        {
            Id = Guid.NewGuid(), SchoolId = EcoleA, Matricule = $"M-{key}", FullName = key,
            BirthDate = new DateOnly(2014, 1, 1), BirthPlace = "Dakar", Gender = gender, ClassroomId = ClasseA
        };
        var enrollment = new Enrollment
        {
            Id = Guid.NewGuid(), SchoolId = EcoleA, StudentId = student.Id, SchoolYearId = AnneeA, ClassroomId = ClasseA,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, TotalDue = 15_000m,
            ReceiptNumber = $"REC-{key}", EnrolledAt = DateTimeOffset.UtcNow
        };
        _students[key] = student.Id;
        _enrollments[key] = enrollment.Id;
        owner.Students.Add(student);
        owner.Enrollments.Add(enrollment);
    }

    private static BoardingAssignmentService ServiceFor(ApplicationDbContext ctx) => new(ctx, TimeProvider.System);

    /// <summary>Affecte et enregistre dans un contexte neuf ; retourne l'identifiant du séjour.</summary>
    private async Task<Guid> AssignAsync(
        string key, BoardingRegime regime, Guid? bed = null, Guid? room = null, uint? rowVersion = null, bool require = false)
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var enrollment = await ctx.Enrollments.SingleAsync(e => e.Id == _enrollments[key]);
        var stay = await ServiceFor(ctx).AssignAsync(enrollment, regime, bed, room, rowVersion, require, CancellationToken.None);
        await BoardingConflicts.SaveAsync(ctx, CancellationToken.None);
        return stay.Id;
    }

    private async Task<BoardingEnrollment> StayAsync(Guid id)
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        return await ctx.BoardingEnrollments.AsNoTracking().SingleAsync(b => b.Id == id);
    }

    private async Task<uint> StayRowVersionAsync(Guid id)
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        return await ctx.BoardingEnrollments.Where(b => b.Id == id).Select(b => EF.Property<uint>(b, "xmin")).SingleAsync();
    }

    [Fact]
    public async Task Interne_With_A_Bed_Creates_An_Active_Stay_Starting_Today()
    {
        var id = await AssignAsync("m1", BoardingRegime.Interne, bed: B101a);

        var stay = await StayAsync(id);
        (stay.IsActive, stay.Regime, stay.BedId, stay.EndDate).Should().Be((true, BoardingRegime.Interne, (Guid?)B101a, (DateOnly?)null));
        stay.StartDate.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow));
        stay.StudentId.Should().Be(_students["m1"]);
    }

    [Fact]
    public async Task Interne_With_A_Room_Takes_The_Lowest_Free_Bed_Until_The_Room_Is_Full()
    {
        (await StayAsync(await AssignAsync("m1", BoardingRegime.Interne, room: R101))).BedId.Should().Be(B101a);
        (await StayAsync(await AssignAsync("m2", BoardingRegime.Interne, room: R101))).BedId.Should().Be(B101b);

        var full = async () => await AssignAsync("m3", BoardingRegime.Interne, room: R101);

        (await full.Should().ThrowAsync<ValidationException>())
            .Which.Errors["RoomId"].Should().Contain("Cette chambre a atteint sa capacité maximale.");
    }

    [Fact]
    public async Task Re_Assigning_To_The_Same_Room_Keeps_The_Current_Bed()
    {
        var id = await AssignAsync("m2", BoardingRegime.Interne, bed: B101b);

        var again = await AssignAsync("m2", BoardingRegime.Interne, room: R101);

        again.Should().Be(id, "même séjour");
        (await StayAsync(id)).BedId.Should().Be(B101b, "il est déjà dans cette chambre : rien ne bouge");
    }

    [Fact]
    public async Task Half_Board_Never_Gets_A_Bed_And_Ignores_A_Room()
    {
        var withRoom = await AssignAsync("m1", BoardingRegime.DemiPensionnaire, room: R101);
        (await StayAsync(withRoom)).BedId.Should().BeNull("la chambre envoyée par l'ancien écran est ignorée");

        var withBed = async () => await AssignAsync("m2", BoardingRegime.DemiPensionnaire, bed: B101a);
        await withBed.Should().ThrowAsync<ValidationException>().Where(e => e.Errors.ContainsKey("BedId"));
    }

    [Fact]
    public async Task Interne_Without_Bed_Nor_Room_Is_Refused()
    {
        var act = async () => await AssignAsync("m1", BoardingRegime.Interne);

        await act.Should().ThrowAsync<ValidationException>().Where(e => e.Errors.ContainsKey("BedId"));
    }

    [Fact]
    public async Task A_Bed_Held_By_Another_Boarder_Is_A_409_BED_UNAVAILABLE()
    {
        await AssignAsync("m1", BoardingRegime.Interne, bed: B101a);

        var act = async () => await AssignAsync("m2", BoardingRegime.Interne, bed: B101a);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("BED_UNAVAILABLE");
    }

    [Fact]
    public async Task A_Bed_In_Maintenance_Is_Refused_With_422()
    {
        var act = async () => await AssignAsync("f1", BoardingRegime.Interne, bed: B201Maintenance);

        await act.Should().ThrowAsync<ValidationException>().Where(e => e.Errors.ContainsKey("BedId"));
    }

    [Fact]
    public async Task A_Boy_Cannot_Take_A_Bed_In_A_Girls_Dormitory_But_A_Mixte_Dormitory_Accepts_Anyone()
    {
        var act = async () => await AssignAsync("m1", BoardingRegime.Interne, bed: B201Libre);
        await act.Should().ThrowAsync<ValidationException>().Where(e => e.Errors.ContainsKey("BedId"));

        await AssignAsync("m1", BoardingRegime.Interne, bed: B301);   // pavillon Mixte : accepté
        await AssignAsync("f1", BoardingRegime.Interne, bed: B201Libre);   // fille dans le pavillon des filles
    }

    [Fact]
    public async Task Transfer_Updates_The_Same_Stay_And_Frees_The_Old_Bed()
    {
        var id = await AssignAsync("m1", BoardingRegime.Interne, bed: B101a);
        var token = await StayRowVersionAsync(id);

        var moved = await AssignAsync("m1", BoardingRegime.Interne, bed: B101b, rowVersion: token, require: true);

        moved.Should().Be(id);
        (await StayAsync(id)).BedId.Should().Be(B101b);
        await AssignAsync("m2", BoardingRegime.Interne, bed: B101a);   // l'ancien lit est de nouveau libre
    }

    [Fact]
    public async Task Transfer_Requires_The_Stay_RowVersion_And_Rejects_A_Stale_One()
    {
        var id = await AssignAsync("m1", BoardingRegime.Interne, bed: B101a);
        var stale = await StayRowVersionAsync(id);
        await AssignAsync("m1", BoardingRegime.Interne, bed: B101b, rowVersion: stale, require: true);   // le jeton change

        var missing = async () => await AssignAsync("m1", BoardingRegime.Interne, bed: B101a, require: true);
        await missing.Should().ThrowAsync<ValidationException>().Where(e => e.Errors.ContainsKey("RowVersion"));

        var old = async () => await AssignAsync("m1", BoardingRegime.Interne, bed: B101a, rowVersion: stale, require: true);
        await old.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task Ending_Closes_The_Stay_And_Keeps_The_Boarding_Fee_Line()
    {
        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var enrollment = await ctx.Enrollments.SingleAsync(e => e.Id == _enrollments["m1"]);
            var service = ServiceFor(ctx);
            await service.AssignAsync(enrollment, BoardingRegime.Interne, B101a, null, null, false, CancellationToken.None);
            await service.AddMissingBoardingFeeAsync(enrollment, CancellationToken.None);
            await BoardingConflicts.SaveAsync(ctx, CancellationToken.None);
        }

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var ended = await ServiceFor(ctx).EndAsync(_enrollments["m1"], CancellationToken.None);
            await ctx.SaveChangesAsync();
            ended.Should().NotBeNull();
        }

        await using var check = _db.NewAppContext(EcoleA);
        var stay = await check.BoardingEnrollments.AsNoTracking().SingleAsync(b => b.EnrollmentId == _enrollments["m1"]);
        (stay.IsActive, stay.BedId).Should().Be((false, (Guid?)null));
        stay.EndDate.Should().NotBeNull();
        (await check.EnrollmentFeeLines.AsNoTracking().CountAsync(l => l.EnrollmentId == _enrollments["m1"] && l.FeeCategoryId == CatPension))
            .Should().Be(1, "la pension déjà facturée reste due");
    }

    [Fact]
    public async Task Ending_Is_Refused_While_A_Leave_Is_Open_And_Returns_Null_Without_An_Active_Stay()
    {
        var id = await AssignAsync("m1", BoardingRegime.Interne, bed: B101a);
        await using (var owner = _db.NewOwnerContext())
        {
            owner.BoardingLeaves.Add(new BoardingLeave
            {
                SchoolId = EcoleA, BoardingEnrollmentId = id, LeaveDate = new DateOnly(2026, 10, 2),
                ExpectedReturnDate = new DateOnly(2026, 10, 4), Reason = BoardingLeaveReason.Weekend, AccompaniedBy = "Parent"
            });
            await owner.SaveChangesAsync();
        }

        await using var ctx = _db.NewAppContext(EcoleA);
        var service = ServiceFor(ctx);

        var open = async () => await service.EndAsync(_enrollments["m1"], CancellationToken.None);
        (await open.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("LEAVE_IN_PROGRESS");

        (await service.EndAsync(_enrollments["m2"], CancellationToken.None)).Should().BeNull("m2 n'a aucun séjour actif");
    }

    [Fact]
    public async Task The_Boarding_Fee_Is_Added_Once_Whatever_The_Number_Of_Calls()
    {
        for (var i = 0; i < 2; i++)
        {
            await using var ctx = _db.NewAppContext(EcoleA);
            var enrollment = await ctx.Enrollments.SingleAsync(e => e.Id == _enrollments["m1"]);
            await ServiceFor(ctx).AddMissingBoardingFeeAsync(enrollment, CancellationToken.None);
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewAppContext(EcoleA);
        (await check.EnrollmentFeeLines.AsNoTracking().CountAsync(l => l.EnrollmentId == _enrollments["m1"])).Should().Be(1);
        (await check.Enrollments.AsNoTracking().SingleAsync(e => e.Id == _enrollments["m1"])).TotalDue
            .Should().Be(15_000m + 20_000m * 9, "pension mensuelle × 9 mois, ajoutée une seule fois");
    }

    [Fact]
    public async Task Two_Concurrent_Assignments_On_The_Same_Bed_One_Wins_The_Other_Gets_BED_UNAVAILABLE()
    {
        await using var ctx1 = _db.NewAppContext(EcoleA);
        await using var ctx2 = _db.NewAppContext(EcoleA);
        var e1 = await ctx1.Enrollments.SingleAsync(e => e.Id == _enrollments["m1"]);
        var e2 = await ctx2.Enrollments.SingleAsync(e => e.Id == _enrollments["m2"]);

        // Les deux contrôles de disponibilité passent AVANT que l'un ou l'autre n'écrive.
        await ServiceFor(ctx1).AssignAsync(e1, BoardingRegime.Interne, B101a, null, null, false, CancellationToken.None);
        await ServiceFor(ctx2).AssignAsync(e2, BoardingRegime.Interne, B101a, null, null, false, CancellationToken.None);

        await BoardingConflicts.SaveAsync(ctx1, CancellationToken.None);
        var loser = async () => await BoardingConflicts.SaveAsync(ctx2, CancellationToken.None);

        (await loser.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("BED_UNAVAILABLE");
        await using var check = _db.NewAppContext(EcoleA);
        (await check.BoardingEnrollments.AsNoTracking().CountAsync(b => b.BedId == B101a && b.IsActive)).Should().Be(1);
    }
}
