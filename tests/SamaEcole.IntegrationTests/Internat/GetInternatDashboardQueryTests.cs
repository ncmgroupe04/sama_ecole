using FluentAssertions;
using SamaEcole.Application.Internat.Queries.GetInternatDashboard;
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Internat;

[Trait("Category", "MultiTenant")]
public class GetInternatDashboardQueryTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("44444444-1111-1111-1111-111111111111");
    private static readonly Guid Batiment = Guid.Parse("44444444-0000-0000-0000-00000000000a");
    private static readonly Guid ChambrePleine = Guid.Parse("44444444-0000-0000-0000-00000000000b");
    private static readonly Guid ChambreLibre = Guid.Parse("44444444-0000-0000-0000-00000000000c");
    private static readonly Guid ClasseA = Guid.Parse("44444444-0000-0000-0000-00000000000d");
    private static readonly Guid AnneeA = Guid.Parse("44444444-0000-0000-0000-00000000000e");
    private static readonly Guid EleveInterne = Guid.Parse("44444444-0000-0000-0000-00000000000f");

    private readonly int _year = AcademicYear.ForDate(DateTimeOffset.UtcNow);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = EcoleA, Name = "École A", Phone = "77 123 45 67" });
        owner.Classrooms.Add(new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = AnneeA, SchoolId = EcoleA, Label = $"{_year}-{_year + 1}",
            StartDate = new DateOnly(_year, 10, 1), EndDate = new DateOnly(_year + 1, 6, 30), IsActive = true
        });
        owner.Buildings.Add(new Building { Id = Batiment, SchoolId = EcoleA, Name = "Pavillon A" });
        owner.Rooms.Add(new Room { Id = ChambrePleine, SchoolId = EcoleA, BuildingId = Batiment, Name = "Chambre 1", Type = RoomType.Dortoir, Capacity = 1 });
        owner.Rooms.Add(new Room { Id = ChambreLibre, SchoolId = EcoleA, BuildingId = Batiment, Name = "Chambre 2", Type = RoomType.Dortoir, Capacity = 2 });
        owner.Students.Add(new Student
        {
            Id = EleveInterne, SchoolId = EcoleA, Matricule = "ELEV-0001", FullName = "Awa Fall",
            BirthDate = new DateOnly(2014, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA,
            GuardianPhone = "771234567"
        });
        owner.Enrollments.Add(new Enrollment
        {
            SchoolId = EcoleA, StudentId = EleveInterne, SchoolYearId = AnneeA, ClassroomId = ClasseA,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            BoardingStatus = BoardingStatus.Interne, RoomId = ChambrePleine,
            TotalDue = 0, ReceiptNumber = "REC-TEST-0001", EnrolledAt = DateTimeOffset.UtcNow
        });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Reports_Occupancy_Per_Room_And_Global_Kpis()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var handler = new GetInternatDashboardQueryHandler(db);

        var result = await handler.Handle(new GetInternatDashboardQuery(), CancellationToken.None);

        result.TotalCapacity.Should().Be(3);
        result.TotalOccupied.Should().Be(1);
        result.InterneCount.Should().Be(1);
        result.DemiPensionnaireCount.Should().Be(0);
        result.FullRoomsCount.Should().Be(1);

        var full = result.Rooms.Single(r => r.RoomId == ChambrePleine);
        full.OccupantsCount.Should().Be(1);
        full.Capacity.Should().Be(1);
        full.Occupants.Should().ContainSingle(o => o.StudentId == EleveInterne && o.GuardianPhone == "771234567");

        var free = result.Rooms.Single(r => r.RoomId == ChambreLibre);
        free.OccupantsCount.Should().Be(0);
        free.Occupants.Should().BeEmpty();
    }
}

/// <summary>
/// Cas de dégradation gracieuse : aucune SchoolYear active. CreateEnrollmentCommandHandler exige déjà
/// une année active pour affecter un élève, donc le dashboard ne doit jamais lever d'exception dans ce
/// cas — il doit rester consultable (dortoirs visibles, tous vides) pour préparer l'année en amont.
/// Classe séparée (fixture dédiée) pour ne pas perturber le seed « année active » du test ci-dessus.
/// </summary>
[Trait("Category", "MultiTenant")]
public class GetInternatDashboardQueryWithoutActiveYearTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleB = Guid.Parse("55555555-1111-1111-1111-111111111111");
    private static readonly Guid Batiment = Guid.Parse("55555555-0000-0000-0000-00000000000a");
    private static readonly Guid Chambre = Guid.Parse("55555555-0000-0000-0000-00000000000b");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = EcoleB, Name = "École B", Phone = "77 987 65 43" });
        owner.Buildings.Add(new Building { Id = Batiment, SchoolId = EcoleB, Name = "Pavillon B" });
        owner.Rooms.Add(new Room { Id = Chambre, SchoolId = EcoleB, BuildingId = Batiment, Name = "Chambre 1", Type = RoomType.Dortoir, Capacity = 4 });
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
        result.Rooms.Single().OccupantsCount.Should().Be(0);
        result.Rooms.Single().Occupants.Should().BeEmpty();
        result.TotalOccupied.Should().Be(0);
        result.InterneCount.Should().Be(0);
        result.DemiPensionnaireCount.Should().Be(0);
    }
}
