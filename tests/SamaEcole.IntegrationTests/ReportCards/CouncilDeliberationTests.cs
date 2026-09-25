using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.ClassSubjects;
using SamaEcole.Application.Coefficients;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Grades.Commands.CreateGrade;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Application.ReportCards;
using SamaEcole.Application.ReportCards.Commands.ApplyCouncilDecisionProposals;
using SamaEcole.Application.ReportCards.Commands.UpdateCouncilRules;
using SamaEcole.Application.ReportCards.Queries.GetClassAnnualDeliberationPdf;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.ReportCards;

file sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}

file sealed class NoLogo : ISchoolLogoProvider
{
    public Task<byte[]?> TryFetchAsync(string? logoUrl, CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);
}

file sealed class SpyGenerator : IClassDeliberationPdfGenerator
{
    public IReadOnlyList<ReportCardDto>? Cards { get; private set; }
    public DeliberationScope? Scope { get; private set; }

    public byte[] Generate(IReadOnlyList<ReportCardDto> reportCards, byte[]? schoolLogo) => Generate(reportCards, schoolLogo, DeliberationScope.Period);

    public byte[] Generate(IReadOnlyList<ReportCardDto> reportCards, byte[]? schoolLogo, DeliberationScope scope)
    {
        Cards = reportCards;
        Scope = scope;
        return [1];
    }
}

file sealed class SummarySender(ApplicationDbContext dbContext) : ISender
{
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        => request is GetGradeSummaryQuery query
            ? (Task<TResponse>)(object)new GetGradeSummaryQueryHandler(
                dbContext, new CoefficientOverrideLoader(dbContext), new SubjectFollowScope(dbContext)).Handle(query, cancellationToken)
            : throw new NotSupportedException(request.GetType().Name);

    public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

/// <summary>
/// Évolution N°7 — conseil de classe de bout en bout, PostgreSQL réel sous le rôle applicatif : PV annuel (moyenne
/// annuelle, sexe, proposition de décision), « Appliquer les propositions » sans jamais écraser une décision prise,
/// seuils propres à l'école, et isolation (les seuils d'une école ne s'appliquent pas à une autre).
/// </summary>
[Trait("Category", "MultiTenant")]
public class CouncilDeliberationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("91111111-1111-1111-1111-111111111111");
    private static readonly Guid AutreEcole = Guid.Parse("92222222-2222-2222-2222-222222222222");
    private static readonly Guid Classe = Guid.Parse("9aaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid Annee = Guid.Parse("9ccccccc-0000-0000-0000-00000000000c");
    private static readonly Guid Semestre1 = Guid.Parse("9ddddddd-0000-0000-0000-000000000001");
    private static readonly Guid Semestre2 = Guid.Parse("9ddddddd-0000-0000-0000-000000000002");
    private static readonly Guid Maths = Guid.Parse("9eeeeeee-0000-0000-0000-00000000000e");

    private static readonly Guid Awa = Guid.Parse("9bbbbbbb-0000-0000-0000-000000000001");    // F — 14 puis 12 : 13
    private static readonly Guid Moussa = Guid.Parse("9bbbbbbb-0000-0000-0000-000000000002"); // M — 9 puis 9 : 9
    private static readonly Guid Ibou = Guid.Parse("9bbbbbbb-0000-0000-0000-000000000003");   // M — 6 puis 6 : 6

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(new School { Id = Ecole, Name = "Lycée A" }, new School { Id = AutreEcole, Name = "Lycée B" });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "Terminale L2", Level = "Lycée", Cycle = CycleType.Lycee, Capacity = 40 });
        owner.Students.AddRange(
            Student(Awa, "Awa Ndiaye", "F"), Student(Moussa, "Moussa Fall", "M"), Student(Ibou, "Ibou Sarr", "M"));
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = Ecole, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 7, 15), IsActive = true
        });
        owner.Terms.AddRange(
            new Term { Id = Semestre1, SchoolId = Ecole, SchoolYearId = Annee, Label = "1er semestre", Order = 1, StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 2, 15) },
            new Term { Id = Semestre2, SchoolId = Ecole, SchoolYearId = Annee, Label = "2nd semestre", Order = 2, StartDate = new DateOnly(2027, 2, 16), EndDate = new DateOnly(2027, 7, 15) });
        owner.Subjects.Add(new Subject { Id = Maths, SchoolId = Ecole, Name = "Mathématiques", Level = "Lycée", Coefficient = 2 });
        // L'autre école a des seuils très sévères : ils ne doivent JAMAIS s'appliquer ici (Global Query Filter + RLS).
        owner.SchoolSettings.Add(new SchoolSettings { SchoolId = AutreEcole, CouncilPromotionMin = 18, CouncilRepeatMin = 17 });
        await owner.SaveChangesAsync();

        await using var db = _db.NewAppContext(Ecole);
        var grade = new CreateGradeCommandHandler(db, new StubTenantProvider(Ecole), new TestCurrentUser(), new SubjectFollowScope(db));
        foreach (var (student, s1, s2) in new[] { (Awa, 14m, 12m), (Moussa, 9m, 9m), (Ibou, 6m, 6m) })
        {
            await grade.Handle(new CreateGradeCommand(student, Maths, Semestre1, EvaluationType.Composition, s1), default);
            await grade.Handle(new CreateGradeCommand(student, Maths, Semestre2, EvaluationType.Composition, s2), default);
        }
    }

    private static Student Student(Guid id, string name, string gender) => new()
    {
        Id = id, SchoolId = Ecole, Matricule = $"M-{name[..3]}", FullName = name, BirthDate = new DateOnly(2008, 1, 1),
        BirthPlace = "Dakar", Gender = gender, ClassroomId = Classe
    };

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static ReportCardDataService Data(ApplicationDbContext db) => new(new SummarySender(db), db);

    [Fact]
    public async Task The_Annual_Minutes_List_Annual_Averages_Genders_And_Proposed_Decisions_In_Merit_Order()
    {
        await using var db = _db.NewAppContext(Ecole);
        var spy = new SpyGenerator();

        var result = await new GetClassAnnualDeliberationPdfQueryHandler(db, Data(db), spy, new NoLogo())
            .Handle(new GetClassAnnualDeliberationPdfQuery(Classe, Annee), default);

        result.FileName.Should().Be("PV_annuel_Terminale_L2_2026-2027.pdf");
        spy.Scope.Should().Be(DeliberationScope.Annual);
        spy.Cards!.Select(c => (c.StudentFullName, c.AnnualAverage, c.StudentGender, c.ProposedCouncilDecision)).Should().Equal(
            ("Awa Ndiaye", 13m, "F", CouncilDecision.Admitted),
            ("Moussa Fall", 9m, "M", CouncilDecision.AllowedToRepeat),
            ("Ibou Sarr", 6m, "M", CouncilDecision.Excluded));

        var stats = DeliberationStatistics.Compute(spy.Cards!, DeliberationScope.Annual);
        stats.Girls.Should().Be(new GenderBreakdown(1, 1, 1, 1));
        stats.Boys.Should().Be(new GenderBreakdown(2, 2, 2, 0));
        stats.Total.PassRate.Should().Be(33.33m);
    }

    [Fact]
    public async Task Applying_Proposals_Records_Them_Without_Ever_Overwriting_A_Council_Decision()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            // Le conseil a déjà décidé pour Ibou : il redouble (contre la proposition « Exclu »).
            owner.ReportCardRemarks.Add(new ReportCardRemark
            {
                SchoolId = Ecole, StudentId = Ibou, TermId = Semestre2, CouncilDecision = CouncilDecision.AllowedToRepeat
            });
            await owner.SaveChangesAsync();
        }

        await using (var db = _db.NewAppContext(Ecole))
        {
            var result = await new ApplyCouncilDecisionProposalsCommandHandler(db, new StubTenantProvider(Ecole), Data(db))
                .Handle(new ApplyCouncilDecisionProposalsCommand(Classe, Semestre2), default);

            result.Should().Be(new ApplyCouncilDecisionProposalsResult(Applied: 2, AlreadyDecided: 1, WithoutAverage: 0));
        }

        await using var check = _db.NewAppContext(Ecole);
        (await check.ReportCardRemarks.Where(r => r.TermId == Semestre2).ToDictionaryAsync(r => r.StudentId, r => r.CouncilDecision))
            .Should().BeEquivalentTo(new Dictionary<Guid, CouncilDecision?>
            {
                [Awa] = CouncilDecision.Admitted,
                [Moussa] = CouncilDecision.AllowedToRepeat,
                [Ibou] = CouncilDecision.AllowedToRepeat
            });
    }

    [Fact]
    public async Task The_School_Own_Thresholds_Drive_The_Proposals()
    {
        await using (var db = _db.NewAppContext(Ecole))
        {
            await new UpdateCouncilRulesCommandHandler(db, new StubTenantProvider(Ecole))
                .Handle(new UpdateCouncilRulesCommand(14, 12, 12, 5, PromotionMin: 8.5m, RepeatMin: 5), default);
        }

        await using var read = _db.NewAppContext(Ecole);
        var moussa = await Data(read).BuildAsync(Moussa, Semestre2, default);
        var ibou = await Data(read).BuildAsync(Ibou, Semestre2, default);

        moussa.ProposedCouncilDecision.Should().Be(CouncilDecision.Admitted, "9 ≥ 8,5 : seuil de passage de l'école");
        ibou.ProposedCouncilDecision.Should().Be(CouncilDecision.AllowedToRepeat, "6 ≥ 5");
    }

    [Fact]
    public async Task The_First_Semester_Card_Honours_The_Distinction_Rules()
    {
        await using var db = _db.NewAppContext(Ecole);

        (await Data(db).BuildAsync(Awa, Semestre1, default)).DisciplinaryMention
            .Should().Be(DisciplinaryMention.Felicitations, "14/20 ≥ 14 : seuil MEN des Félicitations");
        (await Data(db).BuildAsync(Awa, Semestre2, default)).DisciplinaryMention
            .Should().Be(DisciplinaryMention.TableauHonneur, "12/20 sans note éliminatoire");
        (await Data(db).BuildAsync(Moussa, Semestre1, default)).DisciplinaryMention.Should().BeNull();
    }
}
