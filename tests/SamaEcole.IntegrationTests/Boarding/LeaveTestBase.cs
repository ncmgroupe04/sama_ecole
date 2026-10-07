using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.Boarding;

/// <summary>Horloge figée : la date « d'aujourd'hui » des handlers ne dépend jamais du jour d'exécution.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}

/// <summary>
/// Jeu de données commun aux tests des sorties et du pointage (lot D) : une école A avec une année active, deux pavillons
/// et des séjours de toutes natures, plus une école B pour l'isolation. « Aujourd'hui » = 2026-10-12.
/// </summary>
public abstract class LeaveTestBase : IAsyncLifetime
{
    protected static readonly Guid EcoleA = Guid.Parse("e9d11111-1111-1111-1111-111111111111");
    protected static readonly Guid EcoleB = Guid.Parse("e9d22222-2222-2222-2222-222222222222");
    protected static readonly Guid AnneeA = Guid.Parse("e9d11111-0000-0000-0000-000000000001");
    protected static readonly Guid AnneeAPassee = Guid.Parse("e9d11111-0000-0000-0000-000000000002");
    protected static readonly Guid AnneeB = Guid.Parse("e9d22222-0000-0000-0000-000000000001");
    protected static readonly Guid PavA = Guid.Parse("e9d11111-0000-0000-0000-00000000b001");
    protected static readonly Guid PavA2 = Guid.Parse("e9d11111-0000-0000-0000-00000000b002");
    protected static readonly Guid PavEcoleB = Guid.Parse("e9d22222-0000-0000-0000-00000000b001");

    protected static readonly DateOnly Today = new(2026, 10, 12);
    protected static readonly TimeProvider Clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero));

    protected readonly RlsTestDatabase Db = new();

    /// <summary>Séjours par clé : m1-m3 (pavillon A), m4 (pavillon A2), attente (interne sans lit), demi, clos, autre (école B).</summary>
    protected readonly Dictionary<string, Guid> Stays = new();

    public async Task InitializeAsync()
    {
        await Db.InitializeAsync();
        await using var owner = Db.NewOwnerContext();

        owner.Schools.AddRange(new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" });
        owner.SchoolYears.AddRange(
            new SchoolYear { Id = AnneeA, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true },
            new SchoolYear { Id = AnneeAPassee, SchoolId = EcoleA, Label = "2025-2026", StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) },
            new SchoolYear { Id = AnneeB, SchoolId = EcoleB, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true });

        var classeA = Guid.NewGuid();
        var classeB = Guid.NewGuid();
        owner.Classrooms.AddRange(
            new Classroom { Id = classeA, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 60 },
            new Classroom { Id = classeB, SchoolId = EcoleB, Name = "CM2", Level = "Primaire", Capacity = 60 });

        owner.Dormitories.AddRange(
            new Dormitory { Id = PavA, SchoolId = EcoleA, Name = "Pavillon Oustaz Ahmad", Gender = DormitoryGender.Garcons },
            new Dormitory { Id = PavA2, SchoolId = EcoleA, Name = "Pavillon Oustaz Bamba", Gender = DormitoryGender.Garcons },
            new Dormitory { Id = PavEcoleB, SchoolId = EcoleB, Name = "Pavillon de l'école B", Gender = DormitoryGender.Garcons });

        var chambre101 = Guid.NewGuid();
        var chambre102 = Guid.NewGuid();
        var chambreA2 = Guid.NewGuid();
        var chambreB = Guid.NewGuid();
        owner.DormitoryRooms.AddRange(
            new DormitoryRoom { Id = chambre101, SchoolId = EcoleA, DormitoryId = PavA, Name = "Chambre 101" },
            new DormitoryRoom { Id = chambre102, SchoolId = EcoleA, DormitoryId = PavA, Name = "Chambre 102" },
            new DormitoryRoom { Id = chambreA2, SchoolId = EcoleA, DormitoryId = PavA2, Name = "Chambre 201" },
            new DormitoryRoom { Id = chambreB, SchoolId = EcoleB, DormitoryId = PavEcoleB, Name = "Chambre B1" });

        var lits = new Dictionary<string, Guid>();
        foreach (var (key, school, room, number) in new[]
                 {
                     ("101-1", EcoleA, chambre101, 1), ("101-2", EcoleA, chambre101, 2), ("102-1", EcoleA, chambre102, 1),
                     ("201-1", EcoleA, chambreA2, 1), ("B1-1", EcoleB, chambreB, 1)
                 })
        {
            lits[key] = Guid.NewGuid();
            owner.Beds.Add(new Bed { Id = lits[key], SchoolId = school, DormitoryRoomId = room, BedNumber = number });
        }

        AddStay(owner, "m1", EcoleA, AnneeA, classeA, BoardingRegime.Interne, lits["101-1"],
            allowed: [new AllowedExitPerson { Name = "Fatou Diop", Relationship = "Mère", Phone = "770000000" }]);
        AddStay(owner, "m2", EcoleA, AnneeA, classeA, BoardingRegime.Interne, lits["101-2"]);
        AddStay(owner, "m3", EcoleA, AnneeA, classeA, BoardingRegime.Interne, lits["102-1"]);
        AddStay(owner, "m4", EcoleA, AnneeA, classeA, BoardingRegime.Interne, lits["201-1"]);
        AddStay(owner, "attente", EcoleA, AnneeA, classeA, BoardingRegime.Interne, null);
        AddStay(owner, "demi", EcoleA, AnneeA, classeA, BoardingRegime.DemiPensionnaire, null);
        AddStay(owner, "clos", EcoleA, AnneeA, classeA, BoardingRegime.Interne, null, active: false);
        AddStay(owner, "autre", EcoleB, AnneeB, classeB, BoardingRegime.Interne, lits["B1-1"]);

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => Db.DisposeAsync().AsTask();

    private void AddStay(
        ApplicationDbContext owner, string key, Guid school, Guid year, Guid classroom, BoardingRegime regime, Guid? bed,
        bool active = true, List<AllowedExitPerson>? allowed = null)
    {
        var student = new Student
        {
            Id = Guid.NewGuid(), SchoolId = school, Matricule = $"M-{key}", FullName = $"Élève {key}",
            BirthDate = new DateOnly(2014, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = classroom
        };
        var enrollment = new Enrollment
        {
            Id = Guid.NewGuid(), SchoolId = school, StudentId = student.Id, SchoolYearId = year, ClassroomId = classroom,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, TotalDue = 15_000m,
            ReceiptNumber = $"REC-{key}", EnrolledAt = DateTimeOffset.UtcNow
        };
        var stay = new BoardingEnrollment
        {
            SchoolId = school, StudentId = student.Id, EnrollmentId = enrollment.Id, Regime = regime, BedId = bed,
            StartDate = new DateOnly(2026, 9, 15), IsActive = active, EndDate = active ? null : new DateOnly(2026, 9, 20),
            AllowedExitPersons = allowed ?? []
        };
        Stays[key] = stay.Id;
        owner.Students.Add(student);
        owner.Enrollments.Add(enrollment);
        owner.BoardingEnrollments.Add(stay);
    }
}
