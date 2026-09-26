using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.ClassSubjects;
using SamaEcole.Application.Coefficients;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Application.ReportCards;
using SamaEcole.Application.Students.Queries.GetStudentDetail;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.Exemptions;

/// <summary>
/// Dispense d'une matière obligatoire — le résumé de notes, la fiche élève et la grille APC ignorent la matière
/// dispensée, et NE CHANGENT RIEN tant qu'aucune dispense n'existe.
///
/// Jeu : Maths (coef 4, note 12), Français (2, 16), EPS (1, note 8). <c>EleveLibre</c> : aucune dispense, coefficients
/// 7, points 48 + 32 + 8 = 88. <c>EleveEps</c> : dispensé d'EPS (« Inaptitude médicale ») pour l'année active,
/// avec la note d'EPS en base : coefficients 6, points 80 — l'EPS n'entre plus dans la moyenne mais est remontée
/// dans <c>ExemptSubjects</c> pour que le bulletin la marque. Le motif n'apparaît dans aucun DTO de bulletin.
/// </summary>
[Trait("Category", "MultiTenant")]
public class ExemptionCalculationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("c2222222-2222-2222-2222-222222222222");
    private static readonly Guid Annee1 = Guid.Parse("c2222222-0000-0000-0000-000000000001");
    private static readonly Guid Annee0 = Guid.Parse("c2222222-0000-0000-0000-000000000002");
    private static readonly Guid Trimestre1 = Guid.Parse("c2222222-0000-0000-0000-0000000000d1");
    private static readonly Guid Trimestre0 = Guid.Parse("c2222222-0000-0000-0000-0000000000d0");
    private static readonly Guid Classe = Guid.Parse("c2222222-0000-0000-0000-0000000000c1");

    private static readonly Guid Maths = Guid.Parse("c2222222-0000-0000-0000-0000000000a1");
    private static readonly Guid Francais = Guid.Parse("c2222222-0000-0000-0000-0000000000a2");
    private static readonly Guid Eps = Guid.Parse("c2222222-0000-0000-0000-0000000000a3");

    private static readonly Guid EleveLibre = Guid.Parse("c2222222-0000-0000-0000-0000000000e1");
    private static readonly Guid EleveEps = Guid.Parse("c2222222-0000-0000-0000-0000000000e2");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = Ecole, Name = "Collège A" });
        owner.SchoolYears.AddRange(
            new SchoolYear { Id = Annee1, SchoolId = Ecole, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true },
            new SchoolYear { Id = Annee0, SchoolId = Ecole, Label = "2025-2026", StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) });
        owner.Terms.AddRange(
            new Term { Id = Trimestre1, SchoolId = Ecole, SchoolYearId = Annee1, Label = "1er trimestre", Order = 1, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 12, 20) },
            new Term { Id = Trimestre0, SchoolId = Ecole, SchoolYearId = Annee0, Label = "1er trimestre", Order = 1, StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2025, 12, 20) });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 });
        owner.Subjects.AddRange(
            new Subject { Id = Maths, SchoolId = Ecole, Name = "Mathématiques", Level = "Collège", Coefficient = 4 },
            new Subject { Id = Francais, SchoolId = Ecole, Name = "Français", Level = "Collège", Coefficient = 2 },
            new Subject { Id = Eps, SchoolId = Ecole, Name = "EPS", Level = "Collège", Coefficient = 1 });
        owner.Students.AddRange(NewStudent(EleveLibre, "ELEV-0001"), NewStudent(EleveEps, "ELEV-0002"));

        owner.Enrollments.AddRange(
            NewEnrollment(EleveLibre, Annee1, "R-1"), NewEnrollment(EleveEps, Annee1, "R-2"), NewEnrollment(EleveEps, Annee0, "R-0"));
        owner.StudentSubjectExemptions.Add(new StudentSubjectExemption
        {
            SchoolId = Ecole, StudentId = EleveEps, SubjectId = Eps, SchoolYearId = Annee1, Reason = "Inaptitude médicale"
        });

        AddGrades(owner, EleveLibre, Trimestre1, (Maths, 12m), (Francais, 16m), (Eps, 8m));
        AddGrades(owner, EleveEps, Trimestre1, (Maths, 12m), (Francais, 16m), (Eps, 8m));
        AddGrades(owner, EleveEps, Trimestre0, (Maths, 12m), (Francais, 16m), (Eps, 8m));
        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // L'INVARIANT : sans dispense, le calcul est celui d'avant.
    [Fact]
    public async Task Without_Any_Exemption_The_Summary_Is_As_Before()
    {
        await using var db = _db.NewAppContext(Ecole);

        var summary = await SummaryAsync(db, EleveLibre, Trimestre1);

        summary.Subjects.Select(s => s.SubjectId).Should().BeEquivalentTo([Maths, Francais, Eps]);
        summary.TotalCoefficients.Should().Be(7m);
        summary.TotalPoints.Should().Be(88m);
        summary.GeneralAverage.Should().Be(88m / 7m);
        summary.ExemptSubjects.Should().BeNullOrEmpty();
    }

    // La matière dispensée sort du calcul, et le total des coefficients s'adapte.
    [Fact]
    public async Task An_Exempted_Subject_Leaves_The_Summary_And_The_Coefficient_Total_Adapts()
    {
        await using var db = _db.NewAppContext(Ecole);

        var summary = await SummaryAsync(db, EleveEps, Trimestre1);

        summary.Subjects.Select(s => s.SubjectId).Should().BeEquivalentTo([Maths, Francais]);
        summary.TotalCoefficients.Should().Be(6m);
        summary.TotalPoints.Should().Be(80m);
        summary.GeneralAverage.Should().Be(80m / 6m);
        summary.ExemptSubjects.Should().ContainSingle()
            .Which.Should().Be(new ExemptSubjectDto(Eps, "EPS", 1m));
    }

    // La dispense reporte le coefficient EFFECTIF (surcharges comprises), pas seulement celui de la matière.
    [Fact]
    public async Task The_Reported_Exempt_Coefficient_Is_The_Effective_One()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            owner.SubjectCoefficientOverrides.Add(new SubjectCoefficientOverride
            {
                SchoolId = Ecole, SchoolYearId = Annee1, SubjectId = Eps, ClassroomId = Classe, Coefficient = 2m
            });
            await owner.SaveChangesAsync();
        }

        await using var db = _db.NewAppContext(Ecole);

        var summary = await SummaryAsync(db, EleveEps, Trimestre1);

        summary.ExemptSubjects!.Single().Coefficient.Should().Be(2m);
    }

    // La note n'est jamais supprimée (règle #6) : seulement masquée.
    [Fact]
    public async Task The_Grade_Of_An_Exempted_Subject_Is_Kept_In_The_Database()
    {
        await using var db = _db.NewAppContext(Ecole);

        (await db.Grades.CountAsync(g => g.StudentId == EleveEps && g.SubjectId == Eps && g.TermId == Trimestre1)).Should().Be(1);
    }

    // Une dispense d'une année ne masque jamais la matière dans une autre année.
    [Fact]
    public async Task An_Exemption_Of_This_Year_Never_Hides_The_Subject_In_Another_Year()
    {
        await using var db = _db.NewAppContext(Ecole);

        var lastYear = await SummaryAsync(db, EleveEps, Trimestre0);

        lastYear.Subjects.Select(s => s.SubjectId).Should().Contain(Eps);
        lastYear.TotalCoefficients.Should().Be(7m);
        lastYear.ExemptSubjects.Should().BeNullOrEmpty();
    }

    // La fiche élève ne contredit jamais le bulletin.
    [Fact]
    public async Task The_Student_Sheet_Hides_The_Same_Subject_And_Shows_The_Same_Average_As_The_Summary()
    {
        await using var db = _db.NewAppContext(Ecole);

        var summary = await SummaryAsync(db, EleveEps, Trimestre1);
        var detail = await new GetStudentDetailQueryHandler(
                db, new TestCurrentUser(role: Role.Directeur), new CoefficientOverrideLoader(db), new SubjectFollowScope(db))
            .Handle(new GetStudentDetailQuery(EleveEps), default);

        var term = detail.Grades.Single(t => t.TermId == Trimestre1);
        term.Subjects.Select(s => s.SubjectId).Should().BeEquivalentTo(summary.Subjects.Select(s => s.SubjectId));
        term.GeneralAverage.Should().Be(summary.GeneralAverage, "une seule règle, deux lecteurs");
    }

    // La grille APC garde la ligne d'une matière dispensée, marquée ; sans dispense, aucune ligne n'est marquée.
    [Fact]
    public async Task The_Evaluation_Structure_Marks_An_Exempted_Line_And_Keeps_It()
    {
        var domaine = Guid.NewGuid();
        var activite = Guid.NewGuid();
        var epsCe1 = Guid.NewGuid();
        await using (var owner = _db.NewOwnerContext())
        {
            owner.Subjects.AddRange(
                new Subject { Id = domaine, SchoolId = Ecole, Name = "Lang & Com.", Level = "CE1", Coefficient = 1 },
                new Subject { Id = activite, SchoolId = Ecole, Name = "Vocabulaire", Level = "CE1", Coefficient = 1, ParentSubjectId = domaine },
                new Subject { Id = epsCe1, SchoolId = Ecole, Name = "EPS CE1", Level = "CE1", Coefficient = 1 });
            await owner.SaveChangesAsync();
        }

        await using var db = _db.NewAppContext(Ecole);

        var none = await EvaluationStructureBuilder.BuildAsync(db, "CE1", 10, [], [], new HashSet<Guid>(), default);
        var exempted = await EvaluationStructureBuilder.BuildAsync(db, "CE1", 10, [], [], new HashSet<Guid> { epsCe1 }, default);

        none!.Groups.SelectMany(g => g.Lines).Should().OnlyContain(l => !l.IsExempt, "sans dispense, aucune ligne n'est marquée");
        exempted!.Groups.Single(g => g.SubjectId == epsCe1).Lines.Should().ContainSingle()
            .Which.IsExempt.Should().BeTrue("la ligne reste dans la grille, marquée");
        exempted.Groups.Select(g => g.SubjectId).Should().Contain(domaine, "les autres groupes sont intacts");
    }

    // Le motif est sensible : jamais dans un DTO de bulletin.
    [Fact]
    public void The_Exemption_Reason_Is_Never_Part_Of_The_Bulletin_DTOs()
    {
        typeof(ExemptSubjectDto).GetProperties().Select(p => p.Name).Should().NotContain("Reason");
        typeof(GradeSummaryDto).GetProperties().Select(p => p.Name).Should().NotContain("Reason");
    }

    private static Task<GradeSummaryDto> SummaryAsync(ApplicationDbContext db, Guid student, Guid term)
        => new GetGradeSummaryQueryHandler(db, new CoefficientOverrideLoader(db), new SubjectFollowScope(db))
            .Handle(new GetGradeSummaryQuery(student, term), default);

    private static Student NewStudent(Guid id, string matricule) => new()
    {
        Id = id, SchoolId = Ecole, Matricule = matricule, FullName = matricule,
        BirthDate = new DateOnly(2011, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe
    };

    private static Enrollment NewEnrollment(Guid student, Guid year, string receipt) => new()
    {
        SchoolId = Ecole, StudentId = student, SchoolYearId = year, ClassroomId = Classe,
        Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, ReceiptNumber = receipt
    };

    private static void AddGrades(ApplicationDbContext owner, Guid student, Guid term, params (Guid Subject, decimal Value)[] grades)
    {
        foreach (var (subject, value) in grades)
        {
            owner.Grades.Add(new Grade
            {
                SchoolId = Ecole, StudentId = student, SubjectId = subject, TermId = term,
                EvaluationType = EvaluationType.Composition, Value = value
            });
        }
    }
}
