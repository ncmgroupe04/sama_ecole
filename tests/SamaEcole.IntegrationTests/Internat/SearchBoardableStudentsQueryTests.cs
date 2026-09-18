using FluentAssertions;
using SamaEcole.Application.Internat.Queries.SearchBoardableStudents;
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Internat;

[Trait("Category", "MultiTenant")]
public class SearchBoardableStudentsQueryTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("55555555-1111-1111-1111-111111111111");
    private static readonly Guid ClasseA = Guid.Parse("55555555-0000-0000-0000-00000000000a");
    private static readonly Guid AnneeA = Guid.Parse("55555555-0000-0000-0000-00000000000b");
    private static readonly Guid Chambre = Guid.Parse("55555555-0000-0000-0000-00000000000c");
    private static readonly Guid Batiment = Guid.Parse("55555555-0000-0000-0000-00000000000d");
    private static readonly Guid EleveExterne = Guid.Parse("55555555-0000-0000-0000-00000000000e");
    private static readonly Guid EleveInterne = Guid.Parse("55555555-0000-0000-0000-00000000000f");

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
        owner.Rooms.Add(new Room { Id = Chambre, SchoolId = EcoleA, BuildingId = Batiment, Name = "Chambre 1", Type = RoomType.Dortoir, Capacity = 2 });

        owner.Students.Add(new Student { Id = EleveExterne, SchoolId = EcoleA, Matricule = "ELEV-0001", FullName = "Moussa Diop", BirthDate = new DateOnly(2014, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = ClasseA });
        owner.Students.Add(new Student { Id = EleveInterne, SchoolId = EcoleA, Matricule = "ELEV-0002", FullName = "Awa Fall", BirthDate = new DateOnly(2014, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA });

        owner.Enrollments.Add(new Enrollment { SchoolId = EcoleA, StudentId = EleveExterne, SchoolYearId = AnneeA, ClassroomId = ClasseA, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, BoardingStatus = BoardingStatus.Externe, TotalDue = 0, ReceiptNumber = "REC-0001", EnrolledAt = DateTimeOffset.UtcNow });
        owner.Enrollments.Add(new Enrollment { SchoolId = EcoleA, StudentId = EleveInterne, SchoolYearId = AnneeA, ClassroomId = ClasseA, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, BoardingStatus = BoardingStatus.Interne, RoomId = Chambre, TotalDue = 0, ReceiptNumber = "REC-0002", EnrolledAt = DateTimeOffset.UtcNow });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Finds_By_Partial_Name_And_Reports_Current_Boarding_State()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var handler = new SearchBoardableStudentsQueryHandler(db);

        var result = await handler.Handle(new SearchBoardableStudentsQuery("fall"), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].FullName.Should().Be("Awa Fall");
        result[0].BoardingStatus.Should().Be("Interne");
        result[0].CurrentRoomName.Should().Be("Chambre 1");
        // Jeton xmin réel de l'inscription (AGENTS.md règle #5) : la modale d'affectation (Task 16) en
        // a besoin pour poser SetOriginalConcurrencyToken sur un second appel sans provoquer un 409
        // systématique — jamais un placeholder à 0.
        result[0].RowVersion.Should().NotBe((uint)0);
    }

    [Fact]
    public async Task Finds_By_Matricule()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var handler = new SearchBoardableStudentsQueryHandler(db);

        var result = await handler.Handle(new SearchBoardableStudentsQuery("ELEV-0001"), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].FullName.Should().Be("Moussa Diop");
        result[0].BoardingStatus.Should().Be("Externe");
        result[0].CurrentRoomName.Should().BeNull();
    }
}
