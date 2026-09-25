using SamaEcole.Application.ClassSubjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Coefficients;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Application.Students.Queries.GetStudentDetail;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Coefficients;

/// <summary>
/// Évolution N°4 — les moyennes, la fiche élève et donc les bulletins utilisent le coefficient EFFECTIF :
/// surcharge de classe, sinon surcharge de série, sinon coefficient de la matière (arbitrage A4).
///
/// Jeu de données : « Mathématiques » (coeff 4) et « Français » (coeff 2), chaque élève ayant 12 en maths
/// et 16 en français. Sans surcharge : coefficients 6, points 80, moyenne 80/6. Avec Maths = 6 : coefficients
/// 8, points 104, moyenne 13. Avec Maths = 8 : coefficients 10, points 128, moyenne 12,8.
/// </summary>
[Trait("Category", "MultiTenant")]
public class EffectiveCoefficientTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("81111111-1111-1111-1111-111111111111");
    private static readonly Guid AutreEcole = Guid.Parse("82222222-2222-2222-2222-222222222222");

    private static readonly Guid Annee1 = Guid.Parse("81111111-0000-0000-0000-000000000001");
    private static readonly Guid Annee0 = Guid.Parse("81111111-0000-0000-0000-000000000002");
    private static readonly Guid Trimestre1 = Guid.Parse("81111111-0000-0000-0000-0000000000d1");
    private static readonly Guid Trimestre0 = Guid.Parse("81111111-0000-0000-0000-0000000000d0");

    private static readonly Guid ClasseS2 = Guid.Parse("8aaaaaaa-0000-0000-0000-0000000000a2");
    private static readonly Guid ClasseL2 = Guid.Parse("8aaaaaaa-0000-0000-0000-0000000000a3");
    private static readonly Guid ClassePrimaire = Guid.Parse("8aaaaaaa-0000-0000-0000-0000000000a4");

    private static readonly Guid Maths = Guid.Parse("8ccccccc-0000-0000-0000-0000000000c1");
    private static readonly Guid Francais = Guid.Parse("8ccccccc-0000-0000-0000-0000000000c2");

    private static readonly Guid EleveS2 = Guid.Parse("8eeeeeee-0000-0000-0000-0000000000e1");
    private static readonly Guid EleveL2 = Guid.Parse("8eeeeeee-0000-0000-0000-0000000000e2");
    private static readonly Guid ElevePrimaire = Guid.Parse("8eeeeeee-0000-0000-0000-0000000000e3");
    private static readonly Guid EleveMuté = Guid.Parse("8eeeeeee-0000-0000-0000-0000000000e4");

    private static readonly Guid ClasseAutre = Guid.Parse("8bbbbbbb-0000-0000-0000-0000000000b1");
    private static readonly Guid MathsAutre = Guid.Parse("8ddddddd-0000-0000-0000-0000000000d1");
    private static readonly Guid AnneeAutre = Guid.Parse("82222222-0000-0000-0000-000000000001");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = Ecole, Name = "Lycée A" },
            new School { Id = AutreEcole, Name = "Lycée B" });

        owner.SchoolYears.AddRange(
            new SchoolYear { Id = Annee1, SchoolId = Ecole, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true },
            new SchoolYear { Id = Annee0, SchoolId = Ecole, Label = "2025-2026", StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) },
            new SchoolYear { Id = AnneeAutre, SchoolId = AutreEcole, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true });

        owner.Terms.AddRange(
            new Term { Id = Trimestre1, SchoolId = Ecole, SchoolYearId = Annee1, Label = "1er trimestre", Order = 1, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 12, 20) },
            new Term { Id = Trimestre0, SchoolId = Ecole, SchoolYearId = Annee0, Label = "1er trimestre", Order = 1, StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2025, 12, 20) });

        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseS2, SchoolId = Ecole, Name = "Terminale S2 A", Level = "Lycée", Cycle = CycleType.Lycee, Capacity = 40, Series = "S2" },
            new Classroom { Id = ClasseL2, SchoolId = Ecole, Name = "Terminale L2 A", Level = "Lycée", Cycle = CycleType.Lycee, Capacity = 40, Series = "L2" },
            new Classroom { Id = ClassePrimaire, SchoolId = Ecole, Name = "CM2 A", Level = "Primaire", Cycle = CycleType.Primaire, Capacity = 40 },
            new Classroom { Id = ClasseAutre, SchoolId = AutreEcole, Name = "Terminale S2 A", Level = "Lycée", Cycle = CycleType.Lycee, Capacity = 40, Series = "S2" });

        owner.Subjects.AddRange(
            new Subject { Id = Maths, SchoolId = Ecole, Name = "Mathématiques", Level = "Lycée", Coefficient = 4 },
            new Subject { Id = Francais, SchoolId = Ecole, Name = "Français", Level = "Lycée", Coefficient = 2 },
            new Subject { Id = MathsAutre, SchoolId = AutreEcole, Name = "Mathématiques", Level = "Lycée", Coefficient = 4 });

        owner.Students.AddRange(
            NewStudent(EleveS2, "ELEV-0001", "Awa S2", ClasseS2),
            NewStudent(EleveL2, "ELEV-0002", "Fatou L2", ClasseL2),
            NewStudent(ElevePrimaire, "ELEV-0003", "Modou CM2", ClassePrimaire),
            // Passé de S2 (année N-1) à L2 (année courante) : sa classe ACTUELLE est L2.
            NewStudent(EleveMuté, "ELEV-0004", "Ibra muté", ClasseL2));

        owner.Enrollments.AddRange(
            NewEnrollment(EleveMuté, Annee0, ClasseS2, "R-0"),
            NewEnrollment(EleveMuté, Annee1, ClasseL2, "R-1"));

        foreach (var student in new[] { EleveS2, EleveL2, ElevePrimaire, EleveMuté })
        {
            AddGrades(owner, student, Trimestre1);
        }

        AddGrades(owner, EleveMuté, Trimestre0);

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // 1 — sans surcharge, le calcul est celui d'avant (rétrocompatibilité par construction).
    [Fact]
    public async Task Without_Any_Override_The_Subject_Coefficient_Applies_As_Before()
    {
        await using var db = _db.NewAppContext(Ecole);

        var summary = await SummaryAsync(db, EleveS2, Trimestre1);

        summary.Subjects.Single(s => s.SubjectId == Maths).Coefficient.Should().Be(4m);
        summary.TotalCoefficients.Should().Be(6m);
        summary.TotalPoints.Should().Be(80m);
        summary.GeneralAverage.Should().Be(80m / 6m);
    }

    // 2 — une surcharge de série ne touche que les classes de cette série.
    [Fact]
    public async Task A_Series_Override_Applies_To_That_Series_Only()
    {
        await AddOverrideAsync(Annee1, Maths, series: "S2", coefficient: 6m);
        await using var db = _db.NewAppContext(Ecole);

        var s2 = await SummaryAsync(db, EleveS2, Trimestre1);
        s2.Subjects.Single(s => s.SubjectId == Maths).Coefficient.Should().Be(6m);
        s2.TotalCoefficients.Should().Be(8m);
        s2.TotalPoints.Should().Be(104m);
        s2.GeneralAverage.Should().Be(13m);

        var l2 = await SummaryAsync(db, EleveL2, Trimestre1);
        l2.Subjects.Single(s => s.SubjectId == Maths).Coefficient.Should().Be(4m, "la série L2 n'est pas visée");
        l2.GeneralAverage.Should().Be(80m / 6m);
    }

    // 3 — la classe l'emporte sur la série, pour cette classe uniquement.
    [Fact]
    public async Task A_Classroom_Override_Wins_Over_The_Series_Override()
    {
        await AddOverrideAsync(Annee1, Maths, series: "S2", coefficient: 6m);
        await AddOverrideAsync(Annee1, Maths, classroomId: ClasseS2, coefficient: 8m);
        await using var db = _db.NewAppContext(Ecole);

        var summary = await SummaryAsync(db, EleveS2, Trimestre1);

        summary.Subjects.Single(s => s.SubjectId == Maths).Coefficient.Should().Be(8m);
        summary.TotalCoefficients.Should().Be(10m);
        summary.GeneralAverage.Should().Be(12.8m);
    }

    // 4 — une surcharge posée sur une année ne s'applique pas à une autre.
    [Fact]
    public async Task An_Override_Never_Applies_Outside_Its_School_Year()
    {
        await AddOverrideAsync(Annee0, Maths, series: "S2", coefficient: 6m);
        await using var db = _db.NewAppContext(Ecole);

        var summary = await SummaryAsync(db, EleveS2, Trimestre1); // année 1

        summary.Subjects.Single(s => s.SubjectId == Maths).Coefficient.Should().Be(4m);
    }

    // 5 — le primaire neutralise le coefficient à 1 : une surcharge (posée en base) n'y change rien.
    [Fact]
    public async Task Primary_Classes_Keep_Their_Coefficient_Neutralised_To_One()
    {
        await AddOverrideAsync(Annee1, Maths, classroomId: ClassePrimaire, coefficient: 9m);
        await using var db = _db.NewAppContext(Ecole);

        var summary = await SummaryAsync(db, ElevePrimaire, Trimestre1);

        summary.Subjects.Should().OnlyContain(s => s.Coefficient == 1m);
        summary.TotalCoefficients.Should().Be(2m);
    }

    // 6 — l'historique suit l'INSCRIPTION de l'année, pas la classe actuelle (arbitrage A11).
    [Fact]
    public async Task A_Past_Year_Uses_The_Classroom_Of_Its_Enrollment_Not_The_Current_One()
    {
        await AddOverrideAsync(Annee0, Maths, series: "S2", coefficient: 6m);
        await using var db = _db.NewAppContext(Ecole);

        var lastYear = await SummaryAsync(db, EleveMuté, Trimestre0);
        lastYear.Subjects.Single(s => s.SubjectId == Maths).Coefficient.Should().Be(6m,
            "en année N-1 l'élève était en S2, même si sa classe actuelle est L2");

        var thisYear = await SummaryAsync(db, EleveMuté, Trimestre1);
        thisYear.Subjects.Single(s => s.SubjectId == Maths).Coefficient.Should().Be(4m,
            "cette année il est en L2, dont aucune surcharge n'existe");
    }

    // 7 — la fiche élève ne contredit jamais le bulletin.
    [Fact]
    public async Task The_Student_Sheet_Shows_The_Same_Coefficient_And_Average_As_The_Grade_Summary()
    {
        await AddOverrideAsync(Annee1, Maths, series: "S2", coefficient: 6m);
        await AddOverrideAsync(Annee1, Francais, classroomId: ClasseS2, coefficient: 3m);
        await using var db = _db.NewAppContext(Ecole);

        var summary = await SummaryAsync(db, EleveS2, Trimestre1);
        var detail = await new GetStudentDetailQueryHandler(
                db, new TestCurrentUser(role: Role.Directeur), new CoefficientOverrideLoader(db), new SubjectFollowScope(db))
            .Handle(new GetStudentDetailQuery(EleveS2), default);

        var term = detail.Grades.Single(t => t.TermId == Trimestre1);
        term.Subjects.Single(s => s.SubjectId == Maths).Coefficient.Should().Be(6m);
        term.Subjects.Single(s => s.SubjectId == Francais).Coefficient.Should().Be(3m);
        term.GeneralAverage.Should().Be(summary.GeneralAverage, "une seule règle, deux lecteurs");
    }

    // 8 — l'école B ne voit jamais les surcharges de l'école A, même série et même matière homonyme.
    [Fact]
    public async Task Overrides_Of_One_School_Never_Reach_Another()
    {
        await AddOverrideAsync(Annee1, Maths, series: "S2", coefficient: 6m);

        await using var autre = _db.NewAppContext(AutreEcole);
        (await autre.SubjectCoefficientOverrides.CountAsync()).Should().Be(0);

        var loaded = await new CoefficientOverrideLoader(autre)
            .LoadAsync(Guid.NewGuid(), AnneeAutre, default);
        loaded.Should().BeSameAs(CoefficientOverrides.None, "un élève introuvable n'a aucune surcharge");
    }

    private static Task<GradeSummaryDto> SummaryAsync(SamaEcole.Persistence.ApplicationDbContext db, Guid student, Guid term)
        => new GetGradeSummaryQueryHandler(db, new CoefficientOverrideLoader(db), new SubjectFollowScope(db))
            .Handle(new GetGradeSummaryQuery(student, term), default);

    private async Task AddOverrideAsync(
        Guid yearId, Guid subjectId, string? series = null, Guid? classroomId = null, decimal coefficient = 1m)
    {
        await using var owner = _db.NewOwnerContext();
        owner.SubjectCoefficientOverrides.Add(new SubjectCoefficientOverride
        {
            SchoolId = Ecole, SchoolYearId = yearId, SubjectId = subjectId,
            Series = series, ClassroomId = classroomId, Coefficient = coefficient
        });
        await owner.SaveChangesAsync();
    }

    private static Student NewStudent(Guid id, string matricule, string name, Guid classroomId) => new()
    {
        Id = id, SchoolId = Ecole, Matricule = matricule, FullName = name,
        BirthDate = new DateOnly(2008, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = classroomId
    };

    private static Enrollment NewEnrollment(Guid studentId, Guid yearId, Guid classroomId, string receipt) => new()
    {
        SchoolId = Ecole, StudentId = studentId, SchoolYearId = yearId, ClassroomId = classroomId,
        Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, ReceiptNumber = receipt
    };

    private static void AddGrades(SamaEcole.Persistence.ApplicationDbContext owner, Guid studentId, Guid termId)
    {
        owner.Grades.AddRange(
            new Grade { SchoolId = Ecole, StudentId = studentId, SubjectId = Maths, TermId = termId, EvaluationType = EvaluationType.Composition, Value = 12 },
            new Grade { SchoolId = Ecole, StudentId = studentId, SubjectId = Francais, TermId = termId, EvaluationType = EvaluationType.Composition, Value = 16 });
    }
}
