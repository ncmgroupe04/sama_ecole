using FluentAssertions;
using SamaEcole.Application.Students.Queries.GetStudents;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Students;

/// <summary>
/// GetStudentsQueryHandler — portée par année scolaire (interrupteur « Inscrits / Non inscrits / Tous »
/// de l'écran Élèves). Exercé contre un PostgreSQL réel sous le rôle applicatif (RLS active), comme
/// GetStudentDetailQueryTests : la sous-requête sur Enrollments doit rester bornée au tenant courant.
///
/// Trois périmètres EXCLUSIFS sur le même annuaire :
///   * ActiveYearOnly           -> uniquement les élèves AVEC une inscription vivante pour l'année active
///   * NotEnrolledForActiveYear -> uniquement les élèves SANS une telle inscription (annulée = non inscrit)
///   * aucun des deux           -> l'annuaire complet
/// </summary>
[Trait("Category", "MultiTenant")]
public class GetStudentsQueryHandlerTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClasseA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid AnneeA = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");

    private static readonly Guid EleveInscrit = Guid.Parse("11111111-0000-0000-0000-0000000000a1");
    private static readonly Guid EleveNonInscrit = Guid.Parse("11111111-0000-0000-0000-0000000000a2");
    private static readonly Guid EleveInscriptionAnnulee = Guid.Parse("11111111-0000-0000-0000-0000000000a3");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = EcoleA, Name = "École A" });
        owner.Classrooms.Add(new Classroom
        {
            Id = ClasseA, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 40
        });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = AnneeA, SchoolId = EcoleA, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true
        });

        owner.Students.AddRange(
            new Student
            {
                Id = EleveInscrit, SchoolId = EcoleA, Matricule = "ELEV-2026-0001", FullName = "Awa Fall",
                BirthDate = new DateOnly(2015, 3, 12), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA
            },
            new Student
            {
                Id = EleveNonInscrit, SchoolId = EcoleA, Matricule = "ELEV-2026-0002", FullName = "Bineta Ba",
                BirthDate = new DateOnly(2015, 7, 2), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA
            },
            new Student
            {
                Id = EleveInscriptionAnnulee, SchoolId = EcoleA, Matricule = "ELEV-2026-0003", FullName = "Cheikh Sy",
                BirthDate = new DateOnly(2015, 1, 9), BirthPlace = "Dakar", Gender = "M", ClassroomId = ClasseA
            });

        owner.Enrollments.AddRange(
            new Enrollment
            {
                SchoolId = EcoleA, StudentId = EleveInscrit, SchoolYearId = AnneeA, ClassroomId = ClasseA,
                Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
                TotalDue = 100_000m, AmountPaid = 0m, ReceiptNumber = "REC-2026-0001", EnrolledAt = DateTimeOffset.UtcNow
            },
            // Inscription ANNULÉE : l'élève doit compter comme « non inscrit », jamais comme inscrit.
            new Enrollment
            {
                SchoolId = EcoleA, StudentId = EleveInscriptionAnnulee, SchoolYearId = AnneeA, ClassroomId = ClasseA,
                Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Cancelled,
                TotalDue = 0m, AmountPaid = 0m, ReceiptNumber = "REC-2026-0002", EnrolledAt = DateTimeOffset.UtcNow
            });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task ActiveYearOnly_Returns_Only_Students_With_A_Live_Enrollment_For_The_Active_Year()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var handler = new GetStudentsQueryHandler(db);

        var result = await handler.Handle(
            new GetStudentsQuery { ActiveYearOnly = true, PageSize = 50 }, CancellationToken.None);

        result.Items.Select(i => i.Id).Should().Equal(EleveInscrit);
    }

    [Fact]
    public async Task NotEnrolledForActiveYear_Returns_Students_Without_A_Live_Enrollment_Cancelled_Included()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var handler = new GetStudentsQueryHandler(db);

        var result = await handler.Handle(
            new GetStudentsQuery { NotEnrolledForActiveYear = true, PageSize = 50 }, CancellationToken.None);

        result.Items.Select(i => i.Id).Should()
            .BeEquivalentTo(new[] { EleveNonInscrit, EleveInscriptionAnnulee });
    }

    [Fact]
    public async Task ActiveYearOnly_Wins_When_Both_Scope_Flags_Are_Set()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var handler = new GetStudentsQueryHandler(db);

        var result = await handler.Handle(
            new GetStudentsQuery { ActiveYearOnly = true, NotEnrolledForActiveYear = true, PageSize = 50 },
            CancellationToken.None);

        result.Items.Select(i => i.Id).Should().Equal(EleveInscrit);
    }

    [Fact]
    public async Task Without_A_Scope_Flag_The_Whole_Directory_Is_Returned()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var handler = new GetStudentsQueryHandler(db);

        var result = await handler.Handle(new GetStudentsQuery { PageSize = 50 }, CancellationToken.None);

        result.Items.Select(i => i.Id).Should()
            .BeEquivalentTo(new[] { EleveInscrit, EleveNonInscrit, EleveInscriptionAnnulee });
    }
}
