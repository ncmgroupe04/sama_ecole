using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Boarding.Assignments;
using SamaEcole.Application.Boarding.Boarders.DeleteBoarder;
using SamaEcole.Application.Boarding.Leaves.DeleteLeave;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments.Commands.CancelEnrollment;
using SamaEcole.Application.Enrollments.Commands.ChangeEnrollmentStatus;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Boarding;

/// <summary>
/// Annuler une inscription (ou la passer en abandon / transfert) libère le lit d'internat de l'élève. Avant, le séjour restait
/// actif sur une inscription annulée : le lit restait « occupé » par un élève qui n'était plus inscrit (lit fantôme).
/// Une annulation rompt aussi tout ce qui était rattaché (séjour, sorties, lignes de frais) ; un abandon ne fait que clore le séjour.
/// </summary>
[Trait("Category", "MultiTenant")]
public class EnrollmentEndsStayTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("eb111111-1111-1111-1111-111111111111");
    private static readonly Guid Annee = Guid.Parse("eb111111-0000-0000-0000-000000000001");
    private static readonly Guid Classe = Guid.Parse("eb111111-0000-0000-0000-0000000000c1");
    private static readonly Guid CatPension = Guid.Parse("eb111111-0000-0000-0000-0000000000f0");
    private static readonly Guid Pavillon = Guid.Parse("eb111111-0000-0000-0000-00000000b001");
    private static readonly Guid Chambre = Guid.Parse("eb111111-0000-0000-0000-00000000a101");
    private static readonly Guid Lit1 = Guid.Parse("eb111111-0000-0000-0000-0000000b1011");
    private static readonly Guid Acteur = Guid.Parse("eb111111-0000-0000-0000-0000000000aa");

    private readonly Dictionary<string, Guid> _enrollments = new();
    private readonly Dictionary<string, Guid> _stays = new();

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = Ecole, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1),
            EndDate = new DateOnly(2027, 6, 30), IsActive = true
        });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "CM2", Level = "Primaire", Capacity = 60 });
        owner.FeeCategories.Add(new FeeCategory { Id = CatPension, SchoolId = Ecole, Name = "Pension", IsRecurring = true, IsBoardingFee = true });
        owner.Dormitories.Add(new Dormitory { Id = Pavillon, SchoolId = Ecole, Name = "Pavillon", Gender = DormitoryGender.Garcons });
        owner.DormitoryRooms.Add(new DormitoryRoom { Id = Chambre, SchoolId = Ecole, DormitoryId = Pavillon, Name = "Chambre 1" });
        owner.Beds.Add(new Bed { Id = Lit1, SchoolId = Ecole, DormitoryRoomId = Chambre, BedNumber = 1 });

        Seed(owner, "annulee", withOpenLeave: true);
        Seed(owner, "abandon", withOpenLeave: false);
        Seed(owner, "abandonEnSortie", withOpenLeave: true);
        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    /// <summary>Un seul lit pour trois élèves : seul « annulee » le tient au départ, les deux autres sont logés sans lit (demi-pension).</summary>
    private void Seed(Persistence.ApplicationDbContext owner, string key, bool withOpenLeave)
    {
        var student = new Student
        {
            Id = Guid.NewGuid(), SchoolId = Ecole, Matricule = $"M-{key}", FullName = $"Élève {key}",
            BirthDate = new DateOnly(2014, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = Classe
        };
        var enrollment = new Enrollment
        {
            Id = Guid.NewGuid(), SchoolId = Ecole, StudentId = student.Id, SchoolYearId = Annee, ClassroomId = Classe,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, TotalDue = 20_000m,
            ReceiptNumber = $"REC-{key}", EnrolledAt = DateTimeOffset.UtcNow
        };
        var holdsTheBed = key == "annulee";
        var stay = new BoardingEnrollment
        {
            Id = Guid.NewGuid(), SchoolId = Ecole, StudentId = student.Id, EnrollmentId = enrollment.Id,
            Regime = holdsTheBed ? BoardingRegime.Interne : BoardingRegime.DemiPensionnaire,
            BedId = holdsTheBed ? Lit1 : null, StartDate = new DateOnly(2026, 10, 14), IsActive = true
        };
        _enrollments[key] = enrollment.Id;
        _stays[key] = stay.Id;
        owner.Students.Add(student);
        owner.Enrollments.Add(enrollment);
        owner.EnrollmentFeeLines.Add(new EnrollmentFeeLine
        {
            SchoolId = Ecole, EnrollmentId = enrollment.Id, FeeCategoryId = CatPension, Designation = "Pension",
            IsRecurring = true, UnitAmount = 20_000m, Months = 1, LineTotal = 20_000m
        });
        owner.BoardingEnrollments.Add(stay);

        if (withOpenLeave)
        {
            owner.BoardingLeaves.Add(new BoardingLeave
            {
                SchoolId = Ecole, BoardingEnrollmentId = stay.Id, LeaveDate = new DateOnly(2026, 10, 20),
                ExpectedReturnDate = new DateOnly(2026, 10, 22), Reason = BoardingLeaveReason.Famille,
                AccompaniedBy = "Parent"
            });
        }
    }

    private async Task<uint> RowVersionAsync(string key)
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var enrollment = await ctx.Enrollments.FirstAsync(e => e.Id == _enrollments[key]);
        return ctx.Entry(enrollment).Property<uint>("xmin").CurrentValue;
    }

    private async Task CancelAsync(string key)
    {
        var rowVersion = await RowVersionAsync(key);
        await using var ctx = _db.NewAppContext(Ecole);
        await new CancelEnrollmentCommandHandler(ctx, new NoOpKpiCache(), new TestCurrentUser(Acteur))
            .Handle(new CancelEnrollmentCommand(_enrollments[key], rowVersion), CancellationToken.None);
    }

    private async Task DropOutAsync(string key)
    {
        var rowVersion = await RowVersionAsync(key);
        await using var ctx = _db.NewAppContext(Ecole);
        await new ChangeEnrollmentStatusCommandHandler(
                ctx, new NoOpKpiCache(), new BoardingAssignmentService(ctx, TimeProvider.System))
            .Handle(new ChangeEnrollmentStatusCommand(_enrollments[key], EnrollmentStatus.DroppedOut, rowVersion), CancellationToken.None);
    }

    [Fact]
    public async Task Cancelling_An_Enrollment_Frees_The_Bed_And_Archives_Everything_Attached()
    {
        await CancelAsync("annulee");

        await using var owner = _db.NewOwnerContext();
        var enrollment = await owner.Enrollments.IgnoreQueryFilters().SingleAsync(e => e.Id == _enrollments["annulee"]);
        enrollment.Status.Should().Be(EnrollmentStatus.Cancelled);
        enrollment.IsDeleted.Should().BeFalse("l'inscription reste visible, au statut Annulée : le numéro de reçu est sans trou");

        var stay = await owner.BoardingEnrollments.IgnoreQueryFilters().SingleAsync(b => b.Id == _stays["annulee"]);
        (stay.IsActive, stay.BedId, stay.IsDeleted).Should().Be((false, null, true));
        stay.EndDate.Should().NotBeNull();

        (await owner.BoardingLeaves.IgnoreQueryFilters().SingleAsync(l => l.BoardingEnrollmentId == stay.Id)).IsDeleted
            .Should().BeTrue("la sortie en cours est archivée avec le séjour : rien ne bloque l'annulation");
        (await owner.EnrollmentFeeLines.IgnoreQueryFilters().SingleAsync(l => l.EnrollmentId == enrollment.Id)).IsDeleted
            .Should().BeTrue();

        await using var app = _db.NewAppContext(Ecole);
        (await BoardingOccupancyHelper.ActiveBedIdsAsync(app)).Should().NotContain(Lit1, "le lit est de nouveau libre");
    }

    [Fact]
    public async Task Dropping_Out_Ends_The_Stay_And_Keeps_The_History()
    {
        await DropOutAsync("abandon");

        await using var owner = _db.NewOwnerContext();
        var stay = await owner.BoardingEnrollments.IgnoreQueryFilters().SingleAsync(b => b.Id == _stays["abandon"]);
        (stay.IsActive, stay.IsDeleted).Should().Be((false, false));
        stay.EndDate.Should().NotBeNull();
        (await owner.EnrollmentFeeLines.IgnoreQueryFilters().SingleAsync(l => l.EnrollmentId == _enrollments["abandon"])).IsDeleted
            .Should().BeFalse("un abandon est une scolarité réelle : les frais restent dus");
    }

    [Fact]
    public async Task Dropping_Out_Is_Refused_While_A_Leave_Is_Open()
    {
        var act = async () => await DropOutAsync("abandonEnSortie");

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("retour de la sortie");

        await using var owner = _db.NewOwnerContext();
        (await owner.BoardingEnrollments.IgnoreQueryFilters().SingleAsync(b => b.Id == _stays["abandonEnSortie"])).IsActive
            .Should().BeTrue("rien n'est modifié quand l'opération est refusée");
    }

    [Fact]
    public async Task Deleting_A_Stay_Frees_The_Bed_And_Archives_Its_Leaves_But_Keeps_The_Fees()
    {
        uint stayVersion;
        await using (var ctx = _db.NewAppContext(Ecole))
        {
            var stay = await ctx.BoardingEnrollments.FirstAsync(b => b.Id == _stays["annulee"]);
            stayVersion = ctx.Entry(stay).Property<uint>("xmin").CurrentValue;
        }

        await using (var ctx = _db.NewAppContext(Ecole))
        {
            await new DeleteBoarderCommandHandler(ctx, new TestCurrentUser(Acteur))
                .Handle(new DeleteBoarderCommand(_stays["annulee"], stayVersion), CancellationToken.None);
        }

        await using var owner = _db.NewOwnerContext();
        var deleted = await owner.BoardingEnrollments.IgnoreQueryFilters().SingleAsync(b => b.Id == _stays["annulee"]);
        (deleted.IsActive, deleted.BedId, deleted.IsDeleted).Should().Be((false, null, true));
        (await owner.BoardingLeaves.IgnoreQueryFilters().SingleAsync(l => l.BoardingEnrollmentId == deleted.Id)).IsDeleted.Should().BeTrue();
        (await owner.EnrollmentFeeLines.IgnoreQueryFilters().SingleAsync(l => l.EnrollmentId == _enrollments["annulee"])).IsDeleted
            .Should().BeFalse("la pension déjà facturée reste due");
        (await owner.Enrollments.IgnoreQueryFilters().SingleAsync(e => e.Id == _enrollments["annulee"])).Status
            .Should().Be(EnrollmentStatus.Confirmed, "seul le séjour est supprimé, pas l'inscription");

        await using var app = _db.NewAppContext(Ecole);
        (await BoardingOccupancyHelper.ActiveBedIdsAsync(app)).Should().NotContain(Lit1);
    }

    [Fact]
    public async Task Cancelling_A_Leave_Archives_Only_That_Leave_And_Unblocks_The_Release()
    {
        Guid leaveId;
        uint leaveVersion;
        await using (var ctx = _db.NewAppContext(Ecole))
        {
            var leave = await ctx.BoardingLeaves.FirstAsync(l => l.BoardingEnrollmentId == _stays["abandonEnSortie"]);
            leaveId = leave.Id;
            leaveVersion = ctx.Entry(leave).Property<uint>("xmin").CurrentValue;
        }

        await using (var ctx = _db.NewAppContext(Ecole))
        {
            await new DeleteLeaveCommandHandler(ctx, new TestCurrentUser(Acteur))
                .Handle(new DeleteLeaveCommand(leaveId, leaveVersion), CancellationToken.None);
        }

        await using var owner = _db.NewOwnerContext();
        (await owner.BoardingLeaves.IgnoreQueryFilters().SingleAsync(l => l.Id == leaveId)).IsDeleted.Should().BeTrue();
        (await owner.BoardingEnrollments.IgnoreQueryFilters().SingleAsync(b => b.Id == _stays["abandonEnSortie"])).IsActive
            .Should().BeTrue("annuler une sortie ne touche pas au séjour");

        // Plus de sortie ouverte : l'abandon, refusé tant qu'elle existait, passe maintenant.
        await DropOutAsync("abandonEnSortie");
    }

    private static class BoardingOccupancyHelper
    {
        public static Task<List<Guid>> ActiveBedIdsAsync(IApplicationDbContext db) =>
            SamaEcole.Application.Boarding.BoardingOccupancy.ActiveBedIds(db).ToListAsync();
    }

    private sealed class NoOpKpiCache : IKpiCacheService
    {
        public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken) =>
            factory(cancellationToken);

        public void Invalidate(string key) { }
    }
}
