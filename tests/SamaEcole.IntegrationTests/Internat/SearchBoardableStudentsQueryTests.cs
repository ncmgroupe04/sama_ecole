using FluentAssertions;
using SamaEcole.Application.Internat.Queries.SearchBoardableStudents;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Internat;

/// <summary>
/// Recherche d'élève de la modale d'affectation (ancien écran) — même JSON, lue sur les séjours du modèle Pavillon/Lit.
/// <c>rowVersion</c> reste le <c>xmin</c> de l'INSCRIPTION : c'est le jeton de concurrence de la route d'affectation héritée.
/// </summary>
[Trait("Category", "MultiTenant")]
public class SearchBoardableStudentsQueryTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("55555555-2222-2222-2222-222222222222");
    private static readonly Guid ClasseA = Guid.Parse("55555555-0000-0000-0000-0000000000a1");
    private static readonly Guid AnneeA = Guid.Parse("55555555-0000-0000-0000-0000000000b1");
    private static readonly Guid Pavillon = Guid.Parse("55555555-0000-0000-0000-0000000000d1");
    private static readonly Guid Chambre = Guid.Parse("55555555-0000-0000-0000-0000000000c1");

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
        owner.DormitoryRooms.Add(new DormitoryRoom { Id = Chambre, SchoolId = EcoleA, DormitoryId = Pavillon, Name = "Chambre 1" });
        var lit = new Bed { SchoolId = EcoleA, DormitoryRoomId = Chambre, BedNumber = 1 };
        owner.Beds.Add(lit);

        Add(owner, "ELEV-0001", "Moussa Diop", "M", regime: null, bed: null);                       // externe : aucun séjour
        Add(owner, "ELEV-0002", "Awa Fall", "F", BoardingRegime.Interne, lit.Id);                   // interne assise
        Add(owner, "ELEV-0003", "Binta Fall", "F", BoardingRegime.DemiPensionnaire, null);          // demi-pensionnaire

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static void Add(Persistence.ApplicationDbContext owner, string matricule, string name, string gender, BoardingRegime? regime, Guid? bed)
    {
        var student = new Student
        {
            Id = Guid.NewGuid(), SchoolId = EcoleA, Matricule = matricule, FullName = name,
            BirthDate = new DateOnly(2014, 1, 1), BirthPlace = "Dakar", Gender = gender, ClassroomId = ClasseA
        };
        var enrollment = new Enrollment
        {
            SchoolId = EcoleA, StudentId = student.Id, SchoolYearId = AnneeA, ClassroomId = ClasseA,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, TotalDue = 0,
            ReceiptNumber = $"REC-{matricule}", EnrolledAt = DateTimeOffset.UtcNow
        };
        owner.Students.Add(student);
        owner.Enrollments.Add(enrollment);
        if (regime is { } r)
        {
            owner.BoardingEnrollments.Add(new BoardingEnrollment
            {
                SchoolId = EcoleA, StudentId = student.Id, EnrollmentId = enrollment.Id, Regime = r, BedId = bed,
                StartDate = new DateOnly(2026, 9, 15), IsActive = true
            });
        }
    }

    private async Task<IReadOnlyList<BoardableStudentDto>> SearchAsync(string term)
    {
        await using var db = _db.NewAppContext(EcoleA);
        return await new SearchBoardableStudentsQueryHandler(db).Handle(new SearchBoardableStudentsQuery(term), CancellationToken.None);
    }

    [Fact]
    public async Task Finds_By_Partial_Name_And_Reports_Current_Boarding_State()
    {
        var result = await SearchAsync("awa");

        result.Should().ContainSingle();
        result[0].FullName.Should().Be("Awa Fall");
        result[0].BoardingStatus.Should().Be("Interne");
        result[0].CurrentRoomId.Should().Be(Chambre);
        result[0].CurrentRoomName.Should().Be("Chambre 1");
        // Jeton xmin réel de l'inscription (AGENTS.md règle #5) : la modale d'affectation en a besoin pour poser le jeton de
        // concurrence de la route héritée sans provoquer un 409 systématique — jamais un placeholder à 0.
        result[0].RowVersion.Should().NotBe((uint)0);
    }

    [Fact]
    public async Task Finds_By_Matricule()
    {
        var result = await SearchAsync("ELEV-0001");

        result.Should().ContainSingle();
        result[0].FullName.Should().Be("Moussa Diop");
        result[0].BoardingStatus.Should().Be("Externe");
        result[0].CurrentRoomName.Should().BeNull();
    }

    [Fact]
    public async Task A_Half_Boarder_Is_Reported_Without_A_Room()
    {
        var result = await SearchAsync("binta");

        result.Should().ContainSingle();
        result[0].BoardingStatus.Should().Be("DemiPensionnaire");
        result[0].CurrentRoomId.Should().BeNull();
    }

    [Fact]
    public async Task Fall_Matches_Both_Boarders_Sorted_By_Name_And_A_Single_Letter_Matches_Nothing()
    {
        (await SearchAsync("fall")).Select(r => r.FullName).Should().Equal("Awa Fall", "Binta Fall");
        (await SearchAsync("a")).Should().BeEmpty();
    }
}
