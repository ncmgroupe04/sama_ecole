using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.ClassSubjects;
using SamaEcole.Application.ClassSubjects.Commands;
using SamaEcole.Application.ClassSubjects.Queries;
using SamaEcole.Application.Classrooms.Commands.CreateClassroom;
using SamaEcole.Application.Coefficients;
using SamaEcole.Application.Coefficients.Commands;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments.Commands.CreateEnrollment;
using SamaEcole.Application.Grades.Commands.CreateGrade;
using SamaEcole.Application.Grades.Queries.GetClassGrades;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Application.Grades;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using SamaEcole.Infrastructure.Documents;
using MediatR;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.ClassSubjects;

file sealed class NoOpKpiCacheService : IKpiCacheService
{
    public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken) =>
        factory(cancellationToken);

    public void Invalidate(string key) { }
}

file sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}

/// <summary>Route le seul GetGradeSummaryQuery dont ReportCardDataService a besoin (pas de conteneur DI).</summary>
file sealed class SummaryMediator(ApplicationDbContext dbContext) : ISender
{
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        => request is GetGradeSummaryQuery query
            ? (Task<TResponse>)(object)new GetGradeSummaryQueryHandler(
                dbContext, new CoefficientOverrideLoader(dbContext), new SubjectFollowScope(dbContext)).Handle(query, cancellationToken)
            : throw new NotSupportedException(request.GetType().Name);

    public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
        => throw new NotSupportedException();

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}

/// <summary>
/// Évolution N°6 — séries, matières et options, de bout en bout contre un PostgreSQL réel sous le rôle applicatif :
/// la création d'une classe de série injecte le modèle national, l'inscription enregistre les options (option la
/// plus fréquente par défaut), la grille de saisie et le bulletin ne voient que les matières suivies, et
/// « Réinitialiser » rétablit les coefficients officiels.
/// </summary>
[Trait("Category", "MultiTenant")]
public class ClassSubjectsTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("61111111-1111-1111-1111-111111111111");
    private static readonly Guid Annee = Guid.Parse("61111111-0000-0000-0000-00000000000a");
    private static readonly Guid Trimestre = Guid.Parse("61111111-0000-0000-0000-00000000000b");

    // Matières DÉJÀ présentes dans l'école : Mathématiques (4 ≠ 2 officiel en L2), Français (5 = officiel).
    private static readonly Guid Maths = Guid.Parse("61111111-0000-0000-0000-0000000000c1");
    private static readonly Guid Francais = Guid.Parse("61111111-0000-0000-0000-0000000000c2");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = Ecole, Name = "Lycée de test" });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = Ecole, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true
        });
        owner.Terms.Add(new Term
        {
            Id = Trimestre, SchoolId = Ecole, SchoolYearId = Annee, Label = "1er trimestre", Order = 1,
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 1, 15)
        });
        owner.Subjects.AddRange(
            new Subject { Id = Maths, SchoolId = Ecole, Name = "Maths", Level = "Lycée", Coefficient = 4 },
            new Subject { Id = Francais, SchoolId = Ecole, Name = "Français", Level = "Lycée", Coefficient = 5 });
        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private ClassSubjectTemplateInjector Injector(IApplicationDbContext db) =>
        new(db, new NationalSeriesTemplateProvider(), new TestCurrentUser(Guid.NewGuid(), Role.Directeur));

    private async Task<(Guid ClassroomId, ClassTemplateReport Report)> CreateL2ClassAsync()
    {
        await using var db = _db.NewAppContext(Ecole);
        var result = await new CreateClassroomCommandHandler(db, new StubTenantProvider(Ecole), new NoOpKpiCacheService(), Injector(db))
            .Handle(new CreateClassroomCommand { Name = "Terminale L2 A", Level = "Lycée", Capacity = 40, Series = "L2" }, default);
        return (result.Id, result.Template!);
    }

    private async Task<Dictionary<string, Guid>> ClassSubjectIdsAsync(Guid classroomId)
    {
        await using var db = _db.NewAppContext(Ecole);
        return await (
            from c in db.ClassSubjects
            join s in db.Subjects on c.SubjectId equals s.Id
            where c.ClassroomId == classroomId
            select new { s.Name, c.Id }).ToDictionaryAsync(x => x.Name, x => x.Id);
    }

    private async Task<Guid> EnrollAsync(Guid classroomId, string name, params Guid[] options)
    {
        await using var db = _db.NewAppContext(Ecole);
        var receipt = await new CreateEnrollmentCommandHandler(
                db, new StubTenantProvider(Ecole), _db.NewGenerator(db), TimeProvider.System, new NoOpKpiCacheService(),
                new TestCurrentUser())
            .Handle(new CreateEnrollmentCommand
            {
                Type = EnrollmentType.NewEnrollment,
                ClassroomId = classroomId,
                FullName = name,
                BirthDate = new DateOnly(2008, 3, 1),
                BirthPlace = "Thiès",
                Gender = "F",
                SubjectOptionIds = options.Length == 0 ? null : options
            }, default);

        await using var read = _db.NewAppContext(Ecole);
        return await read.Enrollments.Where(e => e.Id == receipt.EnrollmentId).Select(e => e.StudentId).SingleAsync();
    }

    private async Task<HashSet<string>> ChosenOptionsAsync(Guid studentId)
    {
        await using var db = _db.NewAppContext(Ecole);
        return (await (
            from e in db.StudentSubjectEnrollments
            join c in db.ClassSubjects on e.ClassSubjectId equals c.Id
            join s in db.Subjects on c.SubjectId equals s.Id
            where e.StudentId == studentId && e.SchoolYearId == Annee
            select s.Name).ToListAsync()).ToHashSet();
    }

    private async Task<Guid> SubjectIdAsync(string name)
    {
        await using var db = _db.NewAppContext(Ecole);
        return await db.Subjects.Where(s => s.Name == name).Select(s => s.Id).SingleAsync();
    }

    private async Task GradeAsync(Guid studentId, Guid subjectId, decimal value)
    {
        await using var db = _db.NewAppContext(Ecole);
        await new CreateGradeCommandHandler(db, new StubTenantProvider(Ecole), new TestCurrentUser(), new SubjectFollowScope(db))
            .Handle(new CreateGradeCommand(studentId, subjectId, Trimestre, EvaluationType.Composition, value), default);
    }

    [Fact]
    public async Task Creating_An_L2_Class_Injects_The_National_Programme_With_Its_Option_Groups()
    {
        var (classroomId, report) = await CreateL2ClassAsync();

        // Fr, Philo, HG, Anglais, 4 × LV2, Maths, SVT, PC, EPS.
        report.SubjectsAdded.Should().Be(12);
        report.SubjectsCreated.Should().Be(10, "Maths et Français existaient déjà, reconnus par leur nom");
        report.CoefficientsSet.Should().Be(1, "seul Maths (4) diffère de l'officiel (2) ; Français vaut déjà 5");

        await using var db = _db.NewAppContext(Ecole);
        var programme = await new GetClassSubjectsQueryHandler(db, new NationalSeriesTemplateProvider())
            .Handle(new GetClassSubjectsQuery(classroomId), default);

        programme.Rows.Single(r => r.SubjectId == Maths).EffectiveCoefficient.Should().Be(2m);
        programme.Rows.Single(r => r.SubjectId == Maths).Source.Should().Be(CoefficientRules.SourceClassroom);
        programme.Rows.Single(r => r.SubjectId == Francais).Source.Should().Be(CoefficientRules.SourceSubject);
        programme.Rows.Where(r => r.OptionGroup == SeriesCoefficientTemplates.ScienceOptionGroup).Select(r => r.SubjectName)
            .Should().BeEquivalentTo("SVT", "Physique-Chimie");
        programme.Rows.Count(r => r.OptionGroup == SeriesCoefficientTemplates.SecondLanguageGroup).Should().Be(4);
        programme.OptionGroups.Select(g => g.Name).Should().BeEquivalentTo(
            SeriesCoefficientTemplates.SecondLanguageGroup, SeriesCoefficientTemplates.ScienceOptionGroup);

        // Les coefficients des matières de l'école ne bougent JAMAIS : seule la classe reçoit une surcharge.
        (await db.Subjects.Where(s => s.Id == Maths).Select(s => s.Coefficient).SingleAsync()).Should().Be(4m);
    }

    [Fact]
    public async Task Enrollment_Records_The_Chosen_Options_And_Defaults_To_The_Most_Frequent_One()
    {
        var (classroomId, _) = await CreateL2ClassAsync();
        var ids = await ClassSubjectIdsAsync(classroomId);

        var first = await EnrollAsync(classroomId, "Awa Ndiaye", ids["Physique-Chimie"], ids["Allemand"]);
        var second = await EnrollAsync(classroomId, "Fatou Sow"); // aucun choix : options par défaut

        (await ChosenOptionsAsync(first)).Should().BeEquivalentTo("Physique-Chimie", "Allemand");
        (await ChosenOptionsAsync(second)).Should().BeEquivalentTo(
            ["Physique-Chimie", "Allemand"], "l'option la plus fréquente de l'établissement est pré-choisie");
    }

    [Fact]
    public async Task Two_Options_Of_The_Same_Group_Roll_Back_The_Whole_Enrollment()
    {
        var (classroomId, _) = await CreateL2ClassAsync();
        var ids = await ClassSubjectIdsAsync(classroomId);

        var act = () => EnrollAsync(classroomId, "Awa Ndiaye", ids["SVT"], ids["Physique-Chimie"]);

        await act.Should().ThrowAsync<ValidationException>();
        await using var db = _db.NewAppContext(Ecole);
        (await db.Students.CountAsync()).Should().Be(0, "l'inscription entière est annulée, matricule compris");
    }

    [Fact]
    public async Task The_Grade_Grid_Of_An_Option_Lists_Only_The_Students_Who_Chose_It()
    {
        var (classroomId, _) = await CreateL2ClassAsync();
        var ids = await ClassSubjectIdsAsync(classroomId);
        var pcStudent = await EnrollAsync(classroomId, "Awa Ndiaye", ids["Physique-Chimie"]);
        var svtStudent = await EnrollAsync(classroomId, "Fatou Sow", ids["SVT"]);

        await using var db = _db.NewAppContext(Ecole);
        var user = new TestCurrentUser(Guid.NewGuid(), Role.Directeur);
        var handler = new GetClassGradesQueryHandler(
            db, new GradeCorrectionAuthorizer(db, user, TimeProvider.System), new SubjectFollowScope(db));

        var pcGrid = await handler.Handle(new GetClassGradesQuery(classroomId, await SubjectIdAsync("Physique-Chimie"), Trimestre), default);
        var mathsGrid = await handler.Handle(new GetClassGradesQuery(classroomId, Maths, Trimestre), default);

        pcGrid.Select(r => r.StudentId).Should().Equal(pcStudent);
        mathsGrid.Select(r => r.StudentId).Should().BeEquivalentTo([pcStudent, svtStudent], "une matière commune liste toute la classe");
    }

    [Fact]
    public async Task A_Grade_On_An_Option_The_Student_Did_Not_Choose_Is_Refused()
    {
        var (classroomId, _) = await CreateL2ClassAsync();
        var ids = await ClassSubjectIdsAsync(classroomId);
        var student = await EnrollAsync(classroomId, "Awa Ndiaye", ids["Physique-Chimie"]);

        var svt = await SubjectIdAsync("SVT");

        var act = () => GradeAsync(student, svt, 12);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task The_Report_Card_Counts_Only_The_Subjects_Actually_Followed()
    {
        var (classroomId, _) = await CreateL2ClassAsync();
        var ids = await ClassSubjectIdsAsync(classroomId);
        var student = await EnrollAsync(classroomId, "Awa Ndiaye", ids["Physique-Chimie"]);
        var svt = await SubjectIdAsync("SVT");

        await GradeAsync(student, Maths, 10);                                 // coefficient de classe : 2
        await GradeAsync(student, await SubjectIdAsync("Physique-Chimie"), 16); // option : 2

        // Une note SVT antérieure au choix d'option (saisie hors garde, directement en base) : elle reste en base
        // mais sort du bulletin — ni ligne vide, ni coefficient au total.
        await using (var owner = _db.NewOwnerContext())
        {
            owner.Grades.Add(new Grade
            {
                SchoolId = Ecole, StudentId = student, SubjectId = svt, TermId = Trimestre,
                EvaluationType = EvaluationType.Composition, Value = 2
            });
            await owner.SaveChangesAsync();
        }

        await using var db = _db.NewAppContext(Ecole);
        var summary = await new GetGradeSummaryQueryHandler(db, new CoefficientOverrideLoader(db), new SubjectFollowScope(db))
            .Handle(new GetGradeSummaryQuery(student, Trimestre), default);

        summary.Subjects.Select(s => s.SubjectName).Should().BeEquivalentTo("Maths", "Physique-Chimie");
        summary.TotalCoefficients.Should().Be(4m);
        summary.TotalPoints.Should().Be(52m);   // 10 × 2 + 16 × 2
        summary.GeneralAverage.Should().Be(13m); // 52 / 4
    }

    [Fact]
    public async Task The_Pdf_Report_Card_Hides_Unchosen_Options_And_Ranks_Each_Option_Among_Its_Own_Students()
    {
        var (classroomId, _) = await CreateL2ClassAsync();
        var ids = await ClassSubjectIdsAsync(classroomId);
        var pc = await SubjectIdAsync("Physique-Chimie");
        var svt = await SubjectIdAsync("SVT");
        var pcStudent = await EnrollAsync(classroomId, "Awa Ndiaye", ids["Physique-Chimie"]);
        var svtStudent = await EnrollAsync(classroomId, "Fatou Sow", ids["SVT"]);

        await GradeAsync(pcStudent, Maths, 10);   // coefficient de classe : 2
        await GradeAsync(pcStudent, pc, 16);      // option : 2
        await GradeAsync(svtStudent, Maths, 14);
        await GradeAsync(svtStudent, svt, 18);

        // Note SVT antérieure au choix de l'élève de l'option PC : en base, jamais sur son bulletin.
        await using (var owner = _db.NewOwnerContext())
        {
            owner.Grades.Add(new Grade
            {
                SchoolId = Ecole, StudentId = pcStudent, SubjectId = svt, TermId = Trimestre,
                EvaluationType = EvaluationType.Composition, Value = 2
            });
            await owner.SaveChangesAsync();
        }

        await using var db = _db.NewAppContext(Ecole);
        var reportCard = await new ReportCardDataService(new SummaryMediator(db), db).BuildAsync(pcStudent, Trimestre, default);

        reportCard.Subjects.Select(s => s.SubjectName).Should().BeEquivalentTo("Maths", "Physique-Chimie");
        reportCard.Subjects.Should().NotContain(s => s.SubjectId == svt, "une option non choisie est masquée, sans ligne vide");
        reportCard.Subjects.Single(s => s.SubjectId == pc).Coefficient.Should().Be(2m);
        reportCard.TotalCoefficients.Should().Be(4m);
        reportCard.TotalPoints.Should().Be(52m);
        reportCard.GeneralAverage.Should().Be(13m);
        reportCard.SubjectRanks.Keys.Should().BeEquivalentTo([Maths, pc]);
        reportCard.SubjectRanks[pc].Should().Be(1, "le rang d'une option se calcule parmi les seuls élèves qui la suivent");

        // Le vrai générateur (QuestPDF) imprime ce bulletin sans erreur.
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var pdf = new ReportCardPdfGenerator().Generate(reportCard, logo: null);
        System.Text.Encoding.ASCII.GetString(pdf, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public async Task Reset_Restores_The_Official_Coefficients_And_Reactivates_The_Programme()
    {
        var (classroomId, _) = await CreateL2ClassAsync();
        var eps = await SubjectIdAsync("EPS");

        await using (var db = _db.NewAppContext(Ecole))
        {
            // Le Directeur a porté Maths à 7 pour la classe et désactivé l'EPS.
            var mathsOverride = await db.SubjectCoefficientOverrides.SingleAsync(o => o.SubjectId == Maths && o.ClassroomId == classroomId);
            mathsOverride.Coefficient = 7;
            (await db.ClassSubjects.SingleAsync(c => c.ClassroomId == classroomId && c.SubjectId == eps)).IsActive = false;
            await db.SaveChangesAsync();
        }

        await using (var db = _db.NewAppContext(Ecole))
        {
            var report = await new ResetClassSubjectsCommandHandler(db, Injector(db), new NationalSeriesTemplateProvider())
                .Handle(new ResetClassSubjectsCommand(classroomId), default);

            report.CoefficientsSet.Should().Be(1);
            report.SubjectsRestored.Should().Be(1);
        }

        await using var check = _db.NewAppContext(Ecole);
        (await check.SubjectCoefficientOverrides.SingleAsync(o => o.SubjectId == Maths && o.ClassroomId == classroomId))
            .Coefficient.Should().Be(2m);
        (await check.ClassSubjects.SingleAsync(c => c.ClassroomId == classroomId && c.SubjectId == eps)).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Assigning_Default_Options_Fills_Only_The_Students_Without_A_Choice()
    {
        var (classroomId, _) = await CreateL2ClassAsync();
        var ids = await ClassSubjectIdsAsync(classroomId);
        var chose = await EnrollAsync(classroomId, "Awa Ndiaye", ids["SVT"], ids["Espagnol"]);

        // Un élève arrivé avant la configuration des options : aucun choix.
        var late = Guid.NewGuid();
        await using (var owner = _db.NewOwnerContext())
        {
            owner.Students.Add(new Student
            {
                Id = late, SchoolId = Ecole, Matricule = "LATE-0001", FullName = "Élève sans option",
                BirthDate = new DateOnly(2008, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = classroomId
            });
            await owner.SaveChangesAsync();
        }

        await using (var db = _db.NewAppContext(Ecole))
        {
            var result = await new AssignDefaultOptionsCommandHandler(db, new StubTenantProvider(Ecole), new TestCurrentUser())
                .Handle(new AssignDefaultOptionsCommand(classroomId), default);

            result.StudentsUpdated.Should().Be(1);
            result.OptionsAssigned.Should().Be(2);
        }

        (await ChosenOptionsAsync(late)).Should().BeEquivalentTo("SVT", "Espagnol");
        (await ChosenOptionsAsync(chose)).Should().BeEquivalentTo("SVT", "Espagnol");
    }

    [Fact]
    public async Task Changing_A_Students_Option_Soft_Deletes_The_Previous_Choice()
    {
        var (classroomId, _) = await CreateL2ClassAsync();
        var ids = await ClassSubjectIdsAsync(classroomId);
        var student = await EnrollAsync(classroomId, "Awa Ndiaye", ids["SVT"], ids["Espagnol"]);

        await using (var db = _db.NewAppContext(Ecole))
        {
            await new SetStudentSubjectOptionsCommandHandler(db, new StubTenantProvider(Ecole), new TestCurrentUser(Guid.NewGuid()))
                .Handle(new SetStudentSubjectOptionsCommand { StudentId = student, ClassSubjectIds = [ids["Physique-Chimie"]] }, default);
        }

        (await ChosenOptionsAsync(student)).Should().BeEquivalentTo("Physique-Chimie");

        await using var owner = _db.NewOwnerContext();
        (await owner.StudentSubjectEnrollments.IgnoreQueryFilters().CountAsync(e => e.StudentId == student && e.IsDeleted))
            .Should().Be(2, "SVT et Espagnol sont archivés, jamais supprimés (règle #6)");
    }

    [Fact]
    public async Task A_Class_Without_Programme_Keeps_Every_Graded_Subject_On_The_Report_Card()
    {
        var classroomId = Guid.NewGuid();
        var student = Guid.NewGuid();
        await using (var owner = _db.NewOwnerContext())
        {
            owner.Classrooms.Add(new Classroom { Id = classroomId, SchoolId = Ecole, Name = "Seconde A", Level = "Lycée", Capacity = 40 });
            owner.Students.Add(new Student
            {
                Id = student, SchoolId = Ecole, Matricule = "SEC-0001", FullName = "Élève de Seconde",
                BirthDate = new DateOnly(2010, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = classroomId
            });
            await owner.SaveChangesAsync();
        }

        await GradeAsync(student, Maths, 12);
        await GradeAsync(student, Francais, 14);

        await using var db = _db.NewAppContext(Ecole);
        var summary = await new GetGradeSummaryQueryHandler(db, new CoefficientOverrideLoader(db), new SubjectFollowScope(db))
            .Handle(new GetGradeSummaryQuery(student, Trimestre), default);

        summary.Subjects.Should().HaveCount(2);
        summary.TotalCoefficients.Should().Be(9m, "4 + 5 : sans programme de classe, le calcul est strictement celui d'avant");
    }
}
