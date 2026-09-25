using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Coefficients;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Application.OptionalSubjects;
using SamaEcole.Application.ReportCards;
using SamaEcole.Application.Students.Queries.GetStudentDetail;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.OptionalSubjects;

/// <summary>
/// Matières optionnelles et dispenses — le sommaire de notes, la fiche élève et la structure APC ignorent les
/// matières dont l'élève est dispensé, et NE CHANGENT RIEN tant qu'aucune dispense n'existe.
///
/// Jeu : Maths (coef 4, note 12), Français (2, 16), Espagnol (3, 14, option LV2), Arabe (3, 10, option LV2),
/// EPS (1, obligatoire). Un élève sans dispense : coefficients 12, points 48+32+42+30 = 152. Dispensé d'Arabe :
/// coefficients 9, points 48+32+42 = 122 — l'Arabe reste NOTÉ en base mais n'entre plus nulle part. Un élève
/// dispensé d'EPS (motif médical) a 12 en Maths, 16 en Français, 8 en EPS : coefficients 6, points 80 — l'EPS
/// n'entre plus dans la moyenne mais est remontée dans <c>ExemptSubjects</c> pour que le bulletin la marque.
/// </summary>
[Trait("Category", "MultiTenant")]
public class OptionalSubjectsCalculationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("96666666-6666-6666-6666-666666666666");
    private static readonly Guid Annee1 = Guid.Parse("96666666-0000-0000-0000-000000000001");
    private static readonly Guid Annee0 = Guid.Parse("96666666-0000-0000-0000-000000000002");
    private static readonly Guid Trimestre1 = Guid.Parse("96666666-0000-0000-0000-0000000000d1");
    private static readonly Guid Trimestre0 = Guid.Parse("96666666-0000-0000-0000-0000000000d0");
    private static readonly Guid Classe = Guid.Parse("96666666-0000-0000-0000-0000000000c1");

    private static readonly Guid Maths = Guid.Parse("96666666-0000-0000-0000-0000000000a1");
    private static readonly Guid Francais = Guid.Parse("96666666-0000-0000-0000-0000000000a2");
    private static readonly Guid Espagnol = Guid.Parse("96666666-0000-0000-0000-0000000000a3");
    private static readonly Guid Arabe = Guid.Parse("96666666-0000-0000-0000-0000000000a4");
    private static readonly Guid Eps = Guid.Parse("96666666-0000-0000-0000-0000000000a5");

    private static readonly Guid EleveLibre = Guid.Parse("96666666-0000-0000-0000-0000000000e1");
    private static readonly Guid EleveDispense = Guid.Parse("96666666-0000-0000-0000-0000000000e2");
    private static readonly Guid EleveEps = Guid.Parse("96666666-0000-0000-0000-0000000000e3");

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
            new Subject { Id = Espagnol, SchoolId = Ecole, Name = "Espagnol", Level = "Collège", Coefficient = 3, IsOptional = true, OptionGroup = "LV2" },
            new Subject { Id = Arabe, SchoolId = Ecole, Name = "Arabe", Level = "Collège", Coefficient = 3, IsOptional = true, OptionGroup = "LV2" },
            new Subject { Id = Eps, SchoolId = Ecole, Name = "EPS", Level = "Collège", Coefficient = 1 });
        owner.Students.AddRange(
            NewStudent(EleveLibre, "ELEV-0001"), NewStudent(EleveDispense, "ELEV-0002"), NewStudent(EleveEps, "ELEV-0003"));

        var inscriptionLibre = NewEnrollment(EleveLibre, Annee1, "R-1");
        var inscriptionDispense = NewEnrollment(EleveDispense, Annee1, "R-2");
        var inscriptionEps = NewEnrollment(EleveEps, Annee1, "R-3");
        owner.Enrollments.AddRange(inscriptionLibre, inscriptionDispense, inscriptionEps, NewEnrollment(EleveDispense, Annee0, "R-0"));
        owner.EnrollmentSubjectExemptions.AddRange(
            new EnrollmentSubjectExemption { SchoolId = Ecole, EnrollmentId = inscriptionDispense.Id, SubjectId = Arabe },
            new EnrollmentSubjectExemption { SchoolId = Ecole, EnrollmentId = inscriptionEps.Id, SubjectId = Eps, Reason = "Inaptitude médicale" });

        foreach (var student in new[] { EleveLibre, EleveDispense })
        {
            AddGrades(owner, student, Trimestre1, (Maths, 12m), (Francais, 16m), (Espagnol, 14m), (Arabe, 10m));
        }

        AddGrades(owner, EleveDispense, Trimestre0, (Maths, 12m), (Francais, 16m), (Espagnol, 14m), (Arabe, 10m));
        AddGrades(owner, EleveEps, Trimestre1, (Maths, 12m), (Francais, 16m), (Eps, 8m));
        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // 1 — L'INVARIANT : sans dispense, le calcul est celui d'avant.
    [Fact]
    public async Task Without_Any_Exemption_Every_Subject_Counts_As_Before()
    {
        await using var db = _db.NewAppContext(Ecole);

        var summary = await SummaryAsync(db, EleveLibre, Trimestre1);

        summary.Subjects.Select(s => s.SubjectId).Should().BeEquivalentTo([Maths, Francais, Espagnol, Arabe]);
        summary.TotalCoefficients.Should().Be(12m);
        summary.TotalPoints.Should().Be(152m);
        summary.GeneralAverage.Should().Be(152m / 12m);
        summary.ExemptSubjects.Should().BeNullOrEmpty();
    }

    // 2 — l'option non suivie disparaît, et le total des coefficients s'adapte.
    [Fact]
    public async Task An_Option_Not_Followed_Leaves_The_Summary_And_The_Coefficient_Total_Adapts()
    {
        await using var db = _db.NewAppContext(Ecole);

        var summary = await SummaryAsync(db, EleveDispense, Trimestre1);

        summary.Subjects.Select(s => s.SubjectId).Should().BeEquivalentTo([Maths, Francais, Espagnol]);
        summary.TotalCoefficients.Should().Be(9m);
        summary.TotalPoints.Should().Be(122m);
        summary.GeneralAverage.Should().Be(122m / 9m);
        summary.ExemptSubjects.Should().BeNullOrEmpty("une option non suivie n'est pas marquée sur le bulletin, elle en disparaît");
    }

    // 3 — une matière obligatoire dispensée sort du calcul MAIS reste signalée pour le bulletin.
    [Fact]
    public async Task A_Mandatory_Subject_Exemption_Leaves_The_Calculation_But_Is_Reported_For_The_Report_Card()
    {
        await using var db = _db.NewAppContext(Ecole);

        var summary = await SummaryAsync(db, EleveEps, Trimestre1);

        summary.Subjects.Select(s => s.SubjectId).Should().BeEquivalentTo([Maths, Francais]);
        summary.TotalCoefficients.Should().Be(6m);
        summary.TotalPoints.Should().Be(80m);
        summary.ExemptSubjects.Should().ContainSingle()
            .Which.Should().Be(new ExemptSubjectDto(Eps, "EPS", 1m));
    }

    // 4 — la dispense reporte le coefficient EFFECTIF (surcharges comprises), pas seulement celui de la matière.
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

    // 5 — la note n'est jamais supprimée (règle #6) : seulement masquée.
    [Fact]
    public async Task The_Grade_Of_An_Exempted_Subject_Is_Kept_In_The_Database()
    {
        await using var db = _db.NewAppContext(Ecole);

        (await db.Grades.CountAsync(g => g.StudentId == EleveDispense && g.SubjectId == Arabe)).Should().Be(2);
        (await db.Grades.CountAsync(g => g.StudentId == EleveEps && g.SubjectId == Eps)).Should().Be(1);
    }

    // 6 — une dispense posée sur l'inscription d'une année ne touche pas l'autre année.
    [Fact]
    public async Task An_Exemption_Of_This_Year_Never_Hides_The_Subject_In_Another_Year()
    {
        await using var db = _db.NewAppContext(Ecole);

        var lastYear = await SummaryAsync(db, EleveDispense, Trimestre0);

        lastYear.Subjects.Select(s => s.SubjectId).Should().Contain(Arabe);
        lastYear.TotalCoefficients.Should().Be(12m);
    }

    // 7 — repasser la matière en « obligatoire » la rend immédiatement à tous (règle « ligne active »).
    [Fact]
    public async Task A_Subject_That_Stops_Being_Optional_Comes_Back_For_Everybody()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            (await owner.Subjects.IgnoreQueryFilters().SingleAsync(s => s.Id == Arabe)).IsOptional = false;
            await owner.SaveChangesAsync();
        }

        await using var db = _db.NewAppContext(Ecole);

        var summary = await SummaryAsync(db, EleveDispense, Trimestre1);

        summary.Subjects.Select(s => s.SubjectId).Should().Contain(Arabe);
        summary.TotalCoefficients.Should().Be(12m);
        summary.ExemptSubjects.Should().BeNullOrEmpty();
    }

    // 8 — la fiche élève ne contredit jamais le bulletin.
    [Fact]
    public async Task The_Student_Sheet_Hides_The_Same_Subjects_And_Shows_The_Same_Average_As_The_Summary()
    {
        await using var db = _db.NewAppContext(Ecole);

        foreach (var student in new[] { EleveDispense, EleveEps })
        {
            var summary = await SummaryAsync(db, student, Trimestre1);
            var detail = await new GetStudentDetailQueryHandler(
                    db, new TestCurrentUser(role: Role.Directeur), new CoefficientOverrideLoader(db))
                .Handle(new GetStudentDetailQuery(student), default);

            var term = detail.Grades.Single(t => t.TermId == Trimestre1);
            term.Subjects.Select(s => s.SubjectId).Should().BeEquivalentTo(summary.Subjects.Select(s => s.SubjectId));
            term.GeneralAverage.Should().Be(summary.GeneralAverage, "une seule règle, deux lecteurs");
        }
    }

    // 9 — la grille APC : une option non suivie disparaît, une matière obligatoire dispensée reste, marquée.
    [Fact]
    public async Task The_Evaluation_Structure_Drops_An_Unfollowed_Option_And_Marks_An_Exempted_Mandatory_Line()
    {
        var domaine = Guid.NewGuid();
        var activite = Guid.NewGuid();
        var option = Guid.NewGuid();
        var obligatoire = Guid.NewGuid();
        await using (var owner = _db.NewOwnerContext())
        {
            owner.Subjects.AddRange(
                new Subject { Id = domaine, SchoolId = Ecole, Name = "Lang & Com.", Level = "CE1", Coefficient = 1 },
                new Subject { Id = activite, SchoolId = Ecole, Name = "Vocabulaire", Level = "CE1", Coefficient = 1, ParentSubjectId = domaine },
                new Subject { Id = option, SchoolId = Ecole, Name = "Arabe CE1", Level = "CE1", Coefficient = 1, IsOptional = true },
                new Subject { Id = obligatoire, SchoolId = Ecole, Name = "EPS CE1", Level = "CE1", Coefficient = 1 });
            await owner.SaveChangesAsync();
        }

        await using var db = _db.NewAppContext(Ecole);

        var none = await EvaluationStructureBuilder.BuildAsync(db, "CE1", 10, [], [], StudentExemptions.None, default);
        var exempted = await EvaluationStructureBuilder.BuildAsync(
            db, "CE1", 10, [], [],
            new StudentExemptions(
            [
                new ExemptSubject(option, "Arabe CE1", 1m, IsMandatory: false),
                new ExemptSubject(obligatoire, "EPS CE1", 1m, IsMandatory: true)
            ]),
            default);

        none!.Groups.SelectMany(g => g.Lines).Should().OnlyContain(l => !l.IsExempt, "sans dispense, aucune ligne n'est marquée");
        none.Groups.Select(g => g.SubjectId).Should().Contain([option, obligatoire]);

        exempted!.Groups.Select(g => g.SubjectId).Should().NotContain(option, "une option non suivie disparaît de la grille, même sans note");
        exempted.Groups.Single(g => g.SubjectId == obligatoire).Lines.Should().ContainSingle()
            .Which.IsExempt.Should().BeTrue();
        exempted.Groups.Select(g => g.SubjectId).Should().Contain(domaine);
    }

    private static Task<GradeSummaryDto> SummaryAsync(SamaEcole.Persistence.ApplicationDbContext db, Guid student, Guid term)
        => new GetGradeSummaryQueryHandler(db, new CoefficientOverrideLoader(db))
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

    private static void AddGrades(
        SamaEcole.Persistence.ApplicationDbContext owner, Guid student, Guid term, params (Guid Subject, decimal Value)[] grades)
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
