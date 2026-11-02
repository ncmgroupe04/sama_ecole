using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Boarding.Deletion;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Boarding;

/// <summary>
/// « Rompre les liens et supprimer » un pavillon, une chambre ou un lit : l'analyse liste les élèves à réaffecter et les lits libres
/// ailleurs ; la suppression archive l'élément (restaurable) et remet les élèves « en attente de lit » (séjour actif, sans lit).
/// </summary>
[Trait("Category", "MultiTenant")]
public class BoardingDeletionTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("ec111111-1111-1111-1111-111111111111");
    private static readonly Guid Annee = Guid.Parse("ec111111-0000-0000-0000-000000000001");
    private static readonly Guid Classe = Guid.Parse("ec111111-0000-0000-0000-0000000000c1");
    private static readonly Guid PavA = Guid.Parse("ec111111-0000-0000-0000-00000000b001");
    private static readonly Guid PavB = Guid.Parse("ec111111-0000-0000-0000-00000000b002");
    private static readonly Guid ChambreA = Guid.Parse("ec111111-0000-0000-0000-00000000a101");
    private static readonly Guid ChambreB = Guid.Parse("ec111111-0000-0000-0000-00000000a201");
    private static readonly Guid LitA1 = Guid.Parse("ec111111-0000-0000-0000-0000000b1011");
    private static readonly Guid LitA2 = Guid.Parse("ec111111-0000-0000-0000-0000000b1012");
    private static readonly Guid LitB1 = Guid.Parse("ec111111-0000-0000-0000-0000000b2011");
    private static readonly Guid Acteur = Guid.Parse("ec111111-0000-0000-0000-0000000000aa");

    private Guid _stayId;

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
        owner.Dormitories.AddRange(
            new Dormitory { Id = PavA, SchoolId = Ecole, Name = "Pavillon A", Gender = DormitoryGender.Garcons },
            new Dormitory { Id = PavB, SchoolId = Ecole, Name = "Pavillon B", Gender = DormitoryGender.Garcons });
        owner.DormitoryRooms.AddRange(
            new DormitoryRoom { Id = ChambreA, SchoolId = Ecole, DormitoryId = PavA, Name = "Chambre A1" },
            new DormitoryRoom { Id = ChambreB, SchoolId = Ecole, DormitoryId = PavB, Name = "Chambre B1" });
        owner.Beds.AddRange(
            new Bed { Id = LitA1, SchoolId = Ecole, DormitoryRoomId = ChambreA, BedNumber = 1 },
            new Bed { Id = LitA2, SchoolId = Ecole, DormitoryRoomId = ChambreA, BedNumber = 2 },
            new Bed { Id = LitB1, SchoolId = Ecole, DormitoryRoomId = ChambreB, BedNumber = 1 });

        var student = new Student
        {
            Id = Guid.NewGuid(), SchoolId = Ecole, Matricule = "M-1", FullName = "Awa Diop", BirthDate = new DateOnly(2014, 1, 1),
            BirthPlace = "Dakar", Gender = "M", ClassroomId = Classe
        };
        var enrollment = new Enrollment
        {
            Id = Guid.NewGuid(), SchoolId = Ecole, StudentId = student.Id, SchoolYearId = Annee, ClassroomId = Classe,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, TotalDue = 10_000m,
            ReceiptNumber = "REC-1", EnrolledAt = DateTimeOffset.UtcNow
        };
        var stay = new BoardingEnrollment
        {
            Id = Guid.NewGuid(), SchoolId = Ecole, StudentId = student.Id, EnrollmentId = enrollment.Id,
            Regime = BoardingRegime.Interne, BedId = LitA1, StartDate = new DateOnly(2026, 10, 14), IsActive = true
        };
        _stayId = stay.Id;
        owner.Students.Add(student);
        owner.Enrollments.Add(enrollment);
        owner.BoardingEnrollments.Add(stay);
        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<uint> RowVersionOfDormitoryAsync(Guid id)
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var dormitory = await ctx.Dormitories.FirstAsync(d => d.Id == id);
        return ctx.Entry(dormitory).Property<uint>("xmin").CurrentValue;
    }

    [Fact]
    public async Task The_Impact_Lists_The_Students_To_Reassign_And_The_Free_Beds_Elsewhere()
    {
        await using var ctx = _db.NewAppContext(Ecole);

        var impact = await new GetBoardingDeletionImpactQueryHandler(ctx)
            .Handle(new GetBoardingDeletionImpactQuery(BoardingDeletionKind.Dormitory, PavA), CancellationToken.None);

        (impact.Label, impact.RoomCount, impact.BedCount).Should().Be(("Pavillon A", 1, 2));
        var occupant = impact.Occupants.Should().ContainSingle().Subject;
        (occupant.StudentName, occupant.BoarderId).Should().Be(("Awa Diop", _stayId));
        impact.FreeBeds.Select(b => b.BedId).Should().Equal(LitB1)
            .And.NotContain(new[] { LitA1, LitA2 }, "les lits du pavillon qu'on supprime ne sont jamais proposés");
    }

    [Fact]
    public async Task Breaking_The_Links_Of_A_Dormitory_Archives_It_And_Leaves_The_Student_Waiting_For_A_Bed()
    {
        var rowVersion = await RowVersionOfDormitoryAsync(PavA);
        await using (var ctx = _db.NewAppContext(Ecole))
        {
            await new BreakLinksAndDeleteCommandHandler(ctx, new TestCurrentUser(Acteur))
                .Handle(new BreakLinksAndDeleteCommand(BoardingDeletionKind.Dormitory, PavA, rowVersion), CancellationToken.None);
        }

        await using var owner = _db.NewOwnerContext();
        (await owner.Dormitories.IgnoreQueryFilters().SingleAsync(d => d.Id == PavA)).IsDeleted.Should().BeTrue();
        (await owner.DormitoryRooms.IgnoreQueryFilters().SingleAsync(r => r.Id == ChambreA)).IsDeleted.Should().BeTrue();
        (await owner.Beds.IgnoreQueryFilters().Where(b => b.DormitoryRoomId == ChambreA).ToListAsync())
            .Should().OnlyContain(b => b.IsDeleted);

        var stay = await owner.BoardingEnrollments.IgnoreQueryFilters().SingleAsync(b => b.Id == _stayId);
        (stay.IsActive, stay.BedId, stay.IsDeleted).Should().Be((true, null, false), "l'élève garde son séjour, sans lit : en attente de lit");

        (await owner.Dormitories.IgnoreQueryFilters().SingleAsync(d => d.Id == PavB)).IsDeleted
            .Should().BeFalse("les autres pavillons ne sont pas touchés");
        (await owner.Beds.IgnoreQueryFilters().SingleAsync(b => b.Id == LitB1)).IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Breaking_The_Links_Of_A_Bed_Only_Archives_That_Bed()
    {
        uint rowVersion;
        await using (var ctx = _db.NewAppContext(Ecole))
        {
            var bed = await ctx.Beds.FirstAsync(b => b.Id == LitA1);
            rowVersion = ctx.Entry(bed).Property<uint>("xmin").CurrentValue;
        }

        await using (var ctx = _db.NewAppContext(Ecole))
        {
            await new BreakLinksAndDeleteCommandHandler(ctx, new TestCurrentUser(Acteur))
                .Handle(new BreakLinksAndDeleteCommand(BoardingDeletionKind.Bed, LitA1, rowVersion), CancellationToken.None);
        }

        await using var owner = _db.NewOwnerContext();
        (await owner.Beds.IgnoreQueryFilters().SingleAsync(b => b.Id == LitA1)).IsDeleted.Should().BeTrue();
        (await owner.Beds.IgnoreQueryFilters().SingleAsync(b => b.Id == LitA2)).IsDeleted.Should().BeFalse();
        (await owner.DormitoryRooms.IgnoreQueryFilters().SingleAsync(r => r.Id == ChambreA)).IsDeleted.Should().BeFalse();
        (await owner.BoardingEnrollments.IgnoreQueryFilters().SingleAsync(b => b.Id == _stayId)).BedId.Should().BeNull();
    }
}
