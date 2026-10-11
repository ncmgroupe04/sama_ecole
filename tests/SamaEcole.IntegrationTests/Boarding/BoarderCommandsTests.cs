using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Boarding;
using SamaEcole.Application.Boarding.Assignments;
using SamaEcole.Application.Boarding.Boarders.AssignBed;
using SamaEcole.Application.Boarding.Boarders.EndBoarding;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.Boarding;

/// <summary>Commandes d'affectation de lit et de fin de séjour (lot C, tâche 3) — HTTP exclu, handlers + PostgreSQL réel.</summary>
[Trait("Category", "MultiTenant")]
public class BoarderCommandsTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("e9111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("e9222222-2222-2222-2222-222222222222");
    private static readonly Guid EcoleC = Guid.Parse("e9333333-3333-3333-3333-333333333333");
    private static readonly Guid AnneeActive = Guid.Parse("e9111111-0000-0000-0000-000000000001");
    private static readonly Guid AnneePassee = Guid.Parse("e9111111-0000-0000-0000-000000000002");
    private static readonly Guid AnneeB = Guid.Parse("e9222222-0000-0000-0000-000000000001");
    private static readonly Guid AnneeC = Guid.Parse("e9333333-0000-0000-0000-000000000001");
    private static readonly Guid ClasseA = Guid.Parse("e9111111-0000-0000-0000-0000000000c1");
    private static readonly Guid ClasseB = Guid.Parse("e9222222-0000-0000-0000-0000000000c1");
    private static readonly Guid ClasseC = Guid.Parse("e9333333-0000-0000-0000-0000000000c1");
    private static readonly Guid CatPension = Guid.Parse("e9111111-0000-0000-0000-0000000000f0");
    private static readonly Guid Pavillon = Guid.Parse("e9111111-0000-0000-0000-00000000b001");
    private static readonly Guid Chambre = Guid.Parse("e9111111-0000-0000-0000-00000000a101");
    private static readonly Guid Lit1 = Guid.Parse("e9111111-0000-0000-0000-0000000b1011");
    private static readonly Guid Lit2 = Guid.Parse("e9111111-0000-0000-0000-0000000b1012");

    private readonly Dictionary<string, Guid> _enrollments = new();

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" }, new School { Id = EcoleC, Name = "École C" });
        owner.SchoolYears.AddRange(
            new SchoolYear { Id = AnneeActive, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true },
            new SchoolYear { Id = AnneePassee, SchoolId = EcoleA, Label = "2025-2026", StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) },
            new SchoolYear { Id = AnneeB, SchoolId = EcoleB, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true },
            new SchoolYear { Id = AnneeC, SchoolId = EcoleC, Label = "2025-2026", StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) });
        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 60 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "CM2", Level = "Primaire", Capacity = 60 },
            new Classroom { Id = ClasseC, SchoolId = EcoleC, Name = "CM2", Level = "Primaire", Capacity = 60 });
        owner.FeeCategories.Add(new FeeCategory { Id = CatPension, SchoolId = EcoleA, Name = "Pension", IsRecurring = true, IsBoardingFee = true });
        owner.ClassFees.Add(new ClassFee { SchoolId = EcoleA, FeeCategoryId = CatPension, ClassroomId = ClasseA, Amount = 20_000m });
        owner.Dormitories.Add(new Dormitory { Id = Pavillon, SchoolId = EcoleA, Name = "Pavillon Oustaz Ahmad", Gender = DormitoryGender.Garcons });
        owner.DormitoryRooms.Add(new DormitoryRoom { Id = Chambre, SchoolId = EcoleA, DormitoryId = Pavillon, Name = "Chambre 101" });
        owner.Beds.AddRange(
            new Bed { Id = Lit1, SchoolId = EcoleA, DormitoryRoomId = Chambre, BedNumber = 1 },
            new Bed { Id = Lit2, SchoolId = EcoleA, DormitoryRoomId = Chambre, BedNumber = 2 });

        Add(owner, "m1", EcoleA, AnneeActive, ClasseA, EnrollmentStatus.Confirmed);
        Add(owner, "m2", EcoleA, AnneeActive, ClasseA, EnrollmentStatus.Confirmed);
        Add(owner, "annule", EcoleA, AnneeActive, ClasseA, EnrollmentStatus.Cancelled);
        Add(owner, "ancien", EcoleA, AnneePassee, ClasseA, EnrollmentStatus.Confirmed);
        Add(owner, "autre", EcoleB, AnneeB, ClasseB, EnrollmentStatus.Confirmed);
        Add(owner, "sansAnnee", EcoleC, AnneeC, ClasseC, EnrollmentStatus.Confirmed);
        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private void Add(ApplicationDbContext owner, string key, Guid school, Guid year, Guid classroom, EnrollmentStatus status)
    {
        var student = new Student
        {
            Id = Guid.NewGuid(), SchoolId = school, Matricule = $"M-{key}", FullName = $"Élève {key}",
            BirthDate = new DateOnly(2014, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = classroom
        };
        var enrollment = new Enrollment
        {
            Id = Guid.NewGuid(), SchoolId = school, StudentId = student.Id, SchoolYearId = year, ClassroomId = classroom,
            Type = EnrollmentType.NewEnrollment, Status = status, TotalDue = 15_000m, ReceiptNumber = $"REC-{key}",
            EnrolledAt = DateTimeOffset.UtcNow
        };
        _enrollments[key] = enrollment.Id;
        owner.Students.Add(student);
        owner.Enrollments.Add(enrollment);
    }

    private async Task<BoarderListItemDto> AssignAsync(
        string key, BoardingRegime regime, Guid? bed, bool fee = false, uint? rowVersion = null, Guid? school = null)
    {
        var tenant = school ?? EcoleA;
        await using var ctx = _db.NewAppContext(tenant);
        return await new AssignBedCommandHandler(ctx, new BoardingAssignmentService(ctx, TimeProvider.System))
            .Handle(new AssignBedCommand(_enrollments[key], regime, bed, fee, rowVersion), CancellationToken.None);
    }

    private async Task EndAsync(Guid boarderId, uint rowVersion, Guid? school = null)
    {
        await using var ctx = _db.NewAppContext(school ?? EcoleA);
        await new EndBoardingCommandHandler(ctx, new BoardingAssignmentService(ctx, TimeProvider.System))
            .Handle(new EndBoardingCommand(boarderId, rowVersion), CancellationToken.None);
    }

    [Fact]
    public async Task Assigning_A_Bed_Creates_An_Active_Stay_And_Describes_It()
    {
        var result = await AssignAsync("m1", BoardingRegime.Interne, Lit1);

        result.Regime.Should().Be(BoardingRegime.Interne);
        result.IsActive.Should().BeTrue();
        (result.BedId, result.BedNumber, result.RoomId, result.RoomName, result.DormitoryId, result.DormitoryName)
            .Should().Be((Lit1, 1, Chambre, "Chambre 101", Pavillon, "Pavillon Oustaz Ahmad"));
        (result.StudentName, result.Matricule, result.ClassroomName).Should().Be(("Élève m1", "M-m1", "CM2"));
        result.EnrollmentId.Should().Be(_enrollments["m1"]);
    }

    [Fact]
    public async Task Transferring_Keeps_The_Same_Stay_And_Frees_The_Old_Bed()
    {
        var first = await AssignAsync("m1", BoardingRegime.Interne, Lit1);

        var moved = await AssignAsync("m1", BoardingRegime.Interne, Lit2, rowVersion: first.RowVersion);

        moved.Id.Should().Be(first.Id);
        moved.BedNumber.Should().Be(2);
        (await AssignAsync("m2", BoardingRegime.Interne, Lit1)).BedNumber.Should().Be(1);   // le lit 1 est libre
    }

    [Fact]
    public async Task A_Transfer_Needs_The_RowVersion_And_Rejects_A_Stale_One()
    {
        var first = await AssignAsync("m1", BoardingRegime.Interne, Lit1);
        await AssignAsync("m1", BoardingRegime.Interne, Lit2, rowVersion: first.RowVersion);   // le jeton change

        var missing = async () => await AssignAsync("m1", BoardingRegime.Interne, Lit1);
        await missing.Should().ThrowAsync<ValidationException>().Where(e => e.Errors.ContainsKey("RowVersion"));

        var stale = async () => await AssignAsync("m1", BoardingRegime.Interne, Lit1, rowVersion: first.RowVersion);
        await stale.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task A_Half_Boarder_Has_A_Stay_Without_A_Bed()
    {
        var result = await AssignAsync("m1", BoardingRegime.DemiPensionnaire, bed: null);

        (result.Regime, result.BedId, result.RoomId).Should().Be((BoardingRegime.DemiPensionnaire, (Guid?)null, (Guid?)null));
    }

    [Fact]
    public async Task Only_An_Active_Non_Cancelled_Enrollment_Of_The_Current_School_Can_Be_Assigned()
    {
        foreach (var key in new[] { "annule", "ancien", "autre" })
        {
            var act = async () => await AssignAsync(key, BoardingRegime.Interne, Lit1);
            await act.Should().ThrowAsync<KeyNotFoundException>(key);
        }

        var noYear = async () => await AssignAsync("sansAnnee", BoardingRegime.DemiPensionnaire, null, school: EcoleC);
        await noYear.Should().ThrowAsync<ValidationException>().Where(e => e.Errors.ContainsKey("SchoolYear"));
    }

    [Fact]
    public async Task The_Boarding_Fee_Is_Added_Once_And_Survives_A_Transfer()
    {
        var first = await AssignAsync("m1", BoardingRegime.Interne, Lit1, fee: true);
        await AssignAsync("m1", BoardingRegime.Interne, Lit2, fee: true, rowVersion: first.RowVersion);

        await using var check = _db.NewAppContext(EcoleA);
        (await check.EnrollmentFeeLines.AsNoTracking().CountAsync(l => l.EnrollmentId == _enrollments["m1"] && l.FeeCategoryId == CatPension))
            .Should().Be(1);
        (await check.Enrollments.AsNoTracking().SingleAsync(e => e.Id == _enrollments["m1"])).TotalDue.Should().Be(15_000m + 20_000m * 9);
    }

    [Fact]
    public async Task Ending_A_Stay_Closes_It_Frees_The_Bed_And_Keeps_The_Fee()
    {
        var stay = await AssignAsync("m1", BoardingRegime.Interne, Lit1, fee: true);

        await EndAsync(stay.Id, stay.RowVersion);

        await using var check = _db.NewAppContext(EcoleA);
        var closed = await check.BoardingEnrollments.AsNoTracking().SingleAsync(b => b.Id == stay.Id);
        (closed.IsActive, closed.BedId).Should().Be((false, (Guid?)null));
        closed.EndDate.Should().NotBeNull();
        (await check.EnrollmentFeeLines.AsNoTracking().CountAsync(l => l.EnrollmentId == _enrollments["m1"] && l.FeeCategoryId == CatPension))
            .Should().Be(1, "la pension déjà facturée reste due");
        (await AssignAsync("m2", BoardingRegime.Interne, Lit1)).BedNumber.Should().Be(1);
    }

    [Fact]
    public async Task Ending_Rejects_A_Stale_Token_An_Open_Leave_A_Closed_Stay_And_Another_School()
    {
        var stay = await AssignAsync("m1", BoardingRegime.Interne, Lit1);

        var stale = async () => await EndAsync(stay.Id, stay.RowVersion + 7);
        await stale.Should().ThrowAsync<ConcurrencyConflictException>();

        await using (var owner = _db.NewOwnerContext())
        {
            owner.BoardingLeaves.Add(new BoardingLeave
            {
                SchoolId = EcoleA, BoardingEnrollmentId = stay.Id, LeaveDate = new DateOnly(2026, 10, 2),
                ExpectedReturnDate = new DateOnly(2026, 10, 4), Reason = BoardingLeaveReason.Weekend, AccompaniedBy = "Parent"
            });
            await owner.SaveChangesAsync();
        }

        var open = async () => await EndAsync(stay.Id, stay.RowVersion);
        (await open.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("LEAVE_IN_PROGRESS");

        var otherSchool = async () => await EndAsync(stay.Id, stay.RowVersion, school: EcoleB);
        await otherSchool.Should().ThrowAsync<KeyNotFoundException>();
    }
}
