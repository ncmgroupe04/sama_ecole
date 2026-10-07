using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Boarding.Beds.GetDeletedBeds;
using SamaEcole.Application.Boarding.Dormitories.GetDeletedDormitories;
using SamaEcole.Application.Boarding.Dormitories.GetDormitory;
using SamaEcole.Application.Boarding.Dormitories.ListDormitories;
using SamaEcole.Application.Boarding.Rooms.GetDeletedDormitoryRooms;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Boarding;

/// <summary>Requêtes de liste, de détail et de corbeille (lot B, tâche 5) — contre un vrai PostgreSQL, RLS comprise.</summary>
[Trait("Category", "MultiTenant")]
public class BoardingQueriesTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("f5111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("f5222222-2222-2222-2222-222222222222");
    private static readonly Guid SurveillantA = Guid.Parse("f5111111-0000-0000-0000-0000000000a1");
    private static readonly Guid AnneeA = Guid.Parse("f5111111-0000-0000-0000-000000000001");
    private static readonly Guid ClasseA = Guid.Parse("f5111111-0000-0000-0000-0000000000c1");
    private static readonly Guid EleveA = Guid.Parse("f5111111-0000-0000-0000-0000000000e1");
    private static readonly Guid InscriptionA = Guid.Parse("f5111111-0000-0000-0000-0000000000f1");
    private static readonly Guid Garcons = Guid.Parse("f5111111-0000-0000-0000-00000000b001");
    private static readonly Guid Filles = Guid.Parse("f5111111-0000-0000-0000-00000000b002");
    private static readonly Guid Ancien = Guid.Parse("f5111111-0000-0000-0000-00000000b003");
    private static readonly Guid PavillonB = Guid.Parse("f5222222-0000-0000-0000-00000000b001");
    private static readonly Guid PavillonBSupprime = Guid.Parse("f5222222-0000-0000-0000-00000000b002");
    private static readonly Guid Ch101 = Guid.Parse("f5111111-0000-0000-0000-00000000a101");
    private static readonly Guid Ch102 = Guid.Parse("f5111111-0000-0000-0000-00000000a102");
    private static readonly Guid Ch103Supprimee = Guid.Parse("f5111111-0000-0000-0000-00000000a103");
    private static readonly Guid ChB = Guid.Parse("f5222222-0000-0000-0000-00000000a101");
    private static readonly Guid Lit101Occupe = Guid.Parse("f5111111-0000-0000-0000-0000000b1011");
    private static readonly Guid Lit101Libre = Guid.Parse("f5111111-0000-0000-0000-0000000b1012");
    private static readonly Guid Lit101Maintenance = Guid.Parse("f5111111-0000-0000-0000-0000000b1013");
    private static readonly Guid Lit102Supprime = Guid.Parse("f5111111-0000-0000-0000-0000000b1023");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" });
        owner.Users.Add(new User
        {
            Id = SurveillantA, SchoolId = EcoleA, Role = Role.Surveillant, Status = EntityStatus.Active,
            Email = "surv-a@test.sn", PasswordHash = "x", FullName = "Surv. A"
        });
        owner.SchoolYears.Add(new SchoolYear { Id = AnneeA, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true });
        owner.Classrooms.Add(new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 });
        owner.Students.Add(new Student { Id = EleveA, SchoolId = EcoleA, Matricule = "ELEV-A1", FullName = "Awa A", BirthDate = new DateOnly(2011, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA });
        owner.Enrollments.Add(new Enrollment { Id = InscriptionA, SchoolId = EcoleA, StudentId = EleveA, SchoolYearId = AnneeA, ClassroomId = ClasseA, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, ReceiptNumber = "REC-A-1", EnrolledAt = DateTimeOffset.UtcNow });

        owner.Dormitories.AddRange(
            new Dormitory { Id = Garcons, SchoolId = EcoleA, Name = "Garçons", Gender = DormitoryGender.Garcons, SupervisorUserId = SurveillantA, SupervisorPhone = "77 123 45 67" },
            new Dormitory { Id = Filles, SchoolId = EcoleA, Name = "Filles", Gender = DormitoryGender.Filles, SupervisorName = "Mme Diop" },
            new Dormitory { Id = Ancien, SchoolId = EcoleA, Name = "Ancien pavillon", Gender = DormitoryGender.Garcons },
            new Dormitory { Id = PavillonB, SchoolId = EcoleB, Name = "Pavillon B", Gender = DormitoryGender.Garcons },
            new Dormitory { Id = PavillonBSupprime, SchoolId = EcoleB, Name = "Supprimé B", Gender = DormitoryGender.Garcons });
        owner.DormitoryRooms.AddRange(
            new DormitoryRoom { Id = Ch101, SchoolId = EcoleA, DormitoryId = Garcons, Name = "Chambre 101" },
            new DormitoryRoom { Id = Ch102, SchoolId = EcoleA, DormitoryId = Garcons, Name = "Chambre 102" },
            new DormitoryRoom { Id = Ch103Supprimee, SchoolId = EcoleA, DormitoryId = Garcons, Name = "Chambre 103" },
            new DormitoryRoom { Id = ChB, SchoolId = EcoleB, DormitoryId = PavillonB, Name = "Chambre B" });
        owner.Beds.AddRange(
            new Bed { Id = Lit101Occupe, SchoolId = EcoleA, DormitoryRoomId = Ch101, BedNumber = 1 },
            new Bed { Id = Lit101Libre, SchoolId = EcoleA, DormitoryRoomId = Ch101, BedNumber = 2 },
            new Bed { Id = Lit101Maintenance, SchoolId = EcoleA, DormitoryRoomId = Ch101, BedNumber = 3, Status = BedStatus.Maintenance },
            new Bed { SchoolId = EcoleA, DormitoryRoomId = Ch102, BedNumber = 1 },
            new Bed { SchoolId = EcoleA, DormitoryRoomId = Ch102, BedNumber = 2 },
            new Bed { Id = Lit102Supprime, SchoolId = EcoleA, DormitoryRoomId = Ch102, BedNumber = 3 },
            new Bed { SchoolId = EcoleB, DormitoryRoomId = ChB, BedNumber = 1 });
        owner.BoardingEnrollments.Add(new BoardingEnrollment
        {
            SchoolId = EcoleA, StudentId = EleveA, EnrollmentId = InscriptionA, Regime = BoardingRegime.Interne,
            BedId = Lit101Occupe, StartDate = new DateOnly(2026, 9, 15), IsActive = true
        });
        await owner.SaveChangesAsync();

        // Éléments supprimés logiquement (corbeille) : un de chaque type dans l'école A, un pavillon dans l'école B.
        (await owner.Dormitories.IgnoreQueryFilters().SingleAsync(d => d.Id == Ancien)).SoftDelete("test");
        (await owner.DormitoryRooms.IgnoreQueryFilters().SingleAsync(r => r.Id == Ch103Supprimee)).SoftDelete("test");
        (await owner.Beds.IgnoreQueryFilters().SingleAsync(b => b.Id == Lit102Supprime)).SoftDelete("test");
        (await owner.Dormitories.IgnoreQueryFilters().SingleAsync(d => d.Id == PavillonBSupprime)).SoftDelete("test");
        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task The_List_Reports_Derived_Capacity_Occupancy_And_Rate_For_The_Current_School_Only()
    {
        await using var ctx = _db.NewAppContext(EcoleA);

        var list = await new ListDormitoriesQueryHandler(ctx).Handle(new ListDormitoriesQuery(), CancellationToken.None);

        list.Select(d => d.Name).Should().Equal("Filles", "Garçons");   // trié par nom, ni le supprimé ni l'école B

        var garcons = list.Single(d => d.Id == Garcons);
        garcons.RoomCount.Should().Be(2, "la chambre 103 supprimée ne compte pas");
        garcons.Capacity.Should().Be(5, "3 + 2 lits vivants, le lit supprimé ne compte pas");
        garcons.OccupiedBeds.Should().Be(1);
        garcons.MaintenanceBeds.Should().Be(1);
        garcons.OccupancyRate.Should().Be(0.25m, "1 occupé / (5 lits - 1 en maintenance)");
        garcons.SupervisorName.Should().Be("Surv. A", "le nom vient du compte lié");

        var filles = list.Single(d => d.Id == Filles);
        (filles.RoomCount, filles.Capacity, filles.OccupiedBeds, filles.MaintenanceBeds, filles.OccupancyRate)
            .Should().Be((0, 0, 0, 0, 0m));
        filles.SupervisorName.Should().Be("Mme Diop");
    }

    [Fact]
    public async Task The_List_Can_Be_Filtered_By_Gender()
    {
        await using var ctx = _db.NewAppContext(EcoleA);

        var list = await new ListDormitoriesQueryHandler(ctx)
            .Handle(new ListDormitoriesQuery(DormitoryGender.Filles), CancellationToken.None);

        list.Should().ContainSingle().Which.Id.Should().Be(Filles);
    }

    [Fact]
    public async Task The_Detail_Lists_Rooms_And_Beds_With_Computed_Status_And_Occupant()
    {
        await using var ctx = _db.NewAppContext(EcoleA);

        var detail = await new GetDormitoryQueryHandler(ctx).Handle(new GetDormitoryQuery(Garcons), CancellationToken.None);

        detail.Rooms.Select(r => r.Name).Should().Equal("Chambre 101", "Chambre 102");
        detail.Capacity.Should().Be(5);
        detail.OccupiedBeds.Should().Be(1);

        var beds = detail.Rooms[0].Beds;
        beds.Select(b => b.BedNumber).Should().Equal(1, 2, 3);
        beds.Select(b => b.Status).Should().Equal(BedStatus.Occupied, BedStatus.Available, BedStatus.Maintenance);
        beds[0].OccupantName.Should().Be("Awa A");
        beds[1].OccupantName.Should().BeNull();
        detail.Rooms[0].Capacity.Should().Be(3);
        detail.Rooms[1].Beds.Should().HaveCount(2, "le lit supprimé n'apparaît pas");
    }

    [Fact]
    public async Task A_Dormitory_Of_Another_School_Or_A_Deleted_One_Is_Not_Found()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetDormitoryQueryHandler(ctx);

        var other = async () => await handler.Handle(new GetDormitoryQuery(PavillonB), CancellationToken.None);
        await other.Should().ThrowAsync<KeyNotFoundException>();

        var deleted = async () => await handler.Handle(new GetDormitoryQuery(Ancien), CancellationToken.None);
        await deleted.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task The_Trash_Lists_Only_The_Deleted_Items_Of_The_Current_School()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var tenant = new BoardingTenant(EcoleA);

        var dormitories = await new GetDeletedDormitoriesQueryHandler(ctx, tenant)
            .Handle(new GetDeletedDormitoriesQuery(), CancellationToken.None);
        dormitories.Should().ContainSingle().Which.Name.Should().Be("Ancien pavillon");

        var rooms = await new GetDeletedDormitoryRoomsQueryHandler(ctx, tenant)
            .Handle(new GetDeletedDormitoryRoomsQuery(), CancellationToken.None);
        var room = rooms.Should().ContainSingle().Subject;
        room.Name.Should().Be("Chambre 103");
        room.ParentId.Should().Be(Garcons);

        var beds = await new GetDeletedBedsQueryHandler(ctx, tenant)
            .Handle(new GetDeletedBedsQuery(), CancellationToken.None);
        var bed = beds.Should().ContainSingle().Subject;
        bed.Name.Should().Be("Lit 3");
        bed.ParentId.Should().Be(Ch102);
    }
}
