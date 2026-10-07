using FluentAssertions;
using SamaEcole.Application.Internat.Queries.GetInternatDashboard;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.Internat;

/// <summary>
/// Tableau de bord de l'ancien écran <c>/internat</c> : JSON inchangé, mais alimenté par les séjours du modèle
/// Pavillon/Lit (lot C). <c>roomId</c> est désormais l'identifiant d'une <c>DormitoryRoom</c>, <c>buildingName</c> le nom du
/// pavillon, <c>capacity</c> le nombre de lits hors maintenance.
/// </summary>
[Trait("Category", "MultiTenant")]
public class GetInternatDashboardQueryTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("44444444-1111-1111-1111-111111111111");
    private static readonly Guid Pavillon = Guid.Parse("44444444-0000-0000-0000-00000000000a");
    private static readonly Guid ChambrePleine = Guid.Parse("44444444-0000-0000-0000-00000000000b");
    private static readonly Guid ChambreLibre = Guid.Parse("44444444-0000-0000-0000-00000000000c");
    private static readonly Guid ChambreSupprimee = Guid.Parse("44444444-0000-0000-0000-0000000000a1");
    private static readonly Guid ClasseA = Guid.Parse("44444444-0000-0000-0000-00000000000d");
    private static readonly Guid AnneeA = Guid.Parse("44444444-0000-0000-0000-00000000000e");
    private static readonly Guid EleveInterne = Guid.Parse("44444444-0000-0000-0000-00000000000f");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = EcoleA, Name = "École A", Phone = "77 123 45 67" });
        owner.Classrooms.Add(new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = AnneeA, SchoolId = EcoleA, Label = "2026-2027",
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true
        });

        owner.Dormitories.Add(new Dormitory { Id = Pavillon, SchoolId = EcoleA, Name = "Pavillon A", Gender = DormitoryGender.Mixte });
        owner.DormitoryRooms.AddRange(
            new DormitoryRoom { Id = ChambrePleine, SchoolId = EcoleA, DormitoryId = Pavillon, Name = "Chambre 1" },
            new DormitoryRoom { Id = ChambreLibre, SchoolId = EcoleA, DormitoryId = Pavillon, Name = "Chambre 2" },
            new DormitoryRoom { Id = ChambreSupprimee, SchoolId = EcoleA, DormitoryId = Pavillon, Name = "Chambre supprimée" });

        var litPlein = new Bed { SchoolId = EcoleA, DormitoryRoomId = ChambrePleine, BedNumber = 1 };
        owner.Beds.AddRange(
            litPlein,
            new Bed { SchoolId = EcoleA, DormitoryRoomId = ChambreLibre, BedNumber = 1 },
            new Bed { SchoolId = EcoleA, DormitoryRoomId = ChambreLibre, BedNumber = 2 },
            new Bed { SchoolId = EcoleA, DormitoryRoomId = ChambreLibre, BedNumber = 3, Status = BedStatus.Maintenance });

        var interne = AddStudent(owner, EleveInterne, "ELEV-0001", "Awa Fall", "771234567", BoardingRegime.Interne, litPlein.Id);
        AddStudent(owner, Guid.NewGuid(), "ELEV-0002", "Cheikh Ndiaye", null, BoardingRegime.DemiPensionnaire, null);
        AddStudent(owner, Guid.NewGuid(), "ELEV-0003", "Demba Sow", null, BoardingRegime.Interne, null);              // en attente
        AddStudent(owner, Guid.NewGuid(), "ELEV-0004", "Ibou Gaye", null, BoardingRegime.Interne, null, active: false);   // séjour clos
        interne.Should().NotBeNull();

        await owner.SaveChangesAsync(CancellationToken.None);

        // Une chambre supprimée logiquement n'apparaît pas au tableau de bord.
        var supprimee = owner.DormitoryRooms.Local.Single(r => r.Id == ChambreSupprimee);
        supprimee.SoftDelete("test");
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static BoardingEnrollment AddStudent(
        ApplicationDbContext owner, Guid studentId, string matricule, string name, string? phone,
        BoardingRegime regime, Guid? bedId, bool active = true)
    {
        owner.Students.Add(new Student
        {
            Id = studentId, SchoolId = EcoleA, Matricule = matricule, FullName = name,
            BirthDate = new DateOnly(2014, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA, GuardianPhone = phone
        });
        var enrollment = new Enrollment
        {
            SchoolId = EcoleA, StudentId = studentId, SchoolYearId = AnneeA, ClassroomId = ClasseA,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            TotalDue = 0, ReceiptNumber = $"REC-{matricule}", EnrolledAt = DateTimeOffset.UtcNow
        };
        owner.Enrollments.Add(enrollment);
        var stay = new BoardingEnrollment
        {
            SchoolId = EcoleA, StudentId = studentId, EnrollmentId = enrollment.Id, Regime = regime, BedId = active ? bedId : null,
            StartDate = new DateOnly(2026, 9, 15), EndDate = active ? null : new DateOnly(2026, 10, 1), IsActive = active
        };
        owner.BoardingEnrollments.Add(stay);
        return stay;
    }

    [Fact]
    public async Task Reports_Occupancy_Per_Room_And_Global_Kpis()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var handler = new GetInternatDashboardQueryHandler(db);

        var result = await handler.Handle(new GetInternatDashboardQuery(), CancellationToken.None);

        result.TotalCapacity.Should().Be(3, "1 lit + 2 lits utilisables : le lit en maintenance ne compte pas");
        result.TotalOccupied.Should().Be(1, "seuls les internes ASSIS sur un lit occupent une chambre");
        result.InterneCount.Should().Be(2, "Awa (assise) et Demba (en attente) ; le séjour clos ne compte pas");
        result.DemiPensionnaireCount.Should().Be(1);
        result.FullRoomsCount.Should().Be(1);
        result.RoomsWithFreeSpaceCount.Should().Be(1);

        result.Rooms.Should().HaveCount(2, "la chambre supprimée n'apparaît pas");
        var full = result.Rooms.Single(r => r.RoomId == ChambrePleine);
        (full.RoomName, full.BuildingName, full.Capacity, full.OccupantsCount).Should().Be(("Chambre 1", "Pavillon A", 1, 1));
        full.Occupants.Should().ContainSingle(o =>
            o.StudentId == EleveInterne && o.GuardianPhone == "771234567" && o.BoardingStatus == "Interne" && o.ClassroomName == "CM2");

        var free = result.Rooms.Single(r => r.RoomId == ChambreLibre);
        (free.Capacity, free.OccupantsCount).Should().Be((2, 0));
        free.Occupants.Should().BeEmpty();
    }
}

/// <summary>
/// Cas de dégradation gracieuse : aucune SchoolYear active. Le tableau de bord ne doit jamais lever d'exception — il reste
/// consultable (chambres visibles, toutes vides) pour préparer l'année en amont.
/// </summary>
[Trait("Category", "MultiTenant")]
public class GetInternatDashboardQueryWithoutActiveYearTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleB = Guid.Parse("55555555-1111-1111-1111-111111111111");
    private static readonly Guid Pavillon = Guid.Parse("55555555-0000-0000-0000-00000000000a");
    private static readonly Guid Chambre = Guid.Parse("55555555-0000-0000-0000-00000000000b");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = EcoleB, Name = "École B", Phone = "77 987 65 43" });
        owner.Dormitories.Add(new Dormitory { Id = Pavillon, SchoolId = EcoleB, Name = "Pavillon B", Gender = DormitoryGender.Garcons });
        owner.DormitoryRooms.Add(new DormitoryRoom { Id = Chambre, SchoolId = EcoleB, DormitoryId = Pavillon, Name = "Chambre 1" });
        for (var number = 1; number <= 4; number++)
        {
            owner.Beds.Add(new Bed { SchoolId = EcoleB, DormitoryRoomId = Chambre, BedNumber = number });
        }

        // Volontairement : aucune SchoolYear créée -> activeYearId résout à null dans le Handler.
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Returns_Empty_Rooms_Without_Throwing_When_No_Active_School_Year()
    {
        await using var db = _db.NewAppContext(EcoleB);
        var handler = new GetInternatDashboardQueryHandler(db);

        var result = await handler.Handle(new GetInternatDashboardQuery(), CancellationToken.None);

        result.Rooms.Should().ContainSingle(r => r.RoomId == Chambre);
        result.Rooms.Single().Capacity.Should().Be(4);
        result.Rooms.Single().OccupantsCount.Should().Be(0);
        result.Rooms.Single().Occupants.Should().BeEmpty();
        result.TotalOccupied.Should().Be(0);
        result.InterneCount.Should().Be(0);
        result.DemiPensionnaireCount.Should().Be(0);
    }
}
