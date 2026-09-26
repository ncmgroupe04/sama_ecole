using FluentAssertions;
using MediatR;
using SamaEcole.Application.ClassSubjects;
using SamaEcole.Application.Coefficients;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.Exemptions;

/// <summary>
/// Le câblage données → <see cref="ReportCardDto"/> pour un élève dispensé d'une matière OBLIGATOIRE : la
/// matière sort des totaux, est reportée dans <see cref="ReportCardDto.ExemptSubjects"/> (coefficient effectif)
/// et n'a ni rang ni appréciation. Le MOTIF (ici médical) n'existe sur aucun objet de bulletin.
///
/// Jeu : « 4ème A » (Collège), Maths (coef 4, 12), Français (coef 2, 16), EPS (coef 1, 8, obligatoire). L'élève
/// dispensé d'EPS (dispense de la Tâche 1, par année) a coefficients 6 et points 80 ; l'élève libre garde les trois matières.
/// </summary>
[Trait("Category", "MultiTenant")]
public class ExemptSubjectReportCardDataTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("97777777-7777-7777-7777-777777777777");
    private static readonly Guid Annee = Guid.Parse("97777777-0000-0000-0000-000000000001");
    private static readonly Guid Trimestre = Guid.Parse("97777777-0000-0000-0000-0000000000d1");
    private static readonly Guid Classe = Guid.Parse("97777777-0000-0000-0000-0000000000c1");

    private static readonly Guid Maths = Guid.Parse("97777777-0000-0000-0000-0000000000a1");
    private static readonly Guid Francais = Guid.Parse("97777777-0000-0000-0000-0000000000a2");
    private static readonly Guid Eps = Guid.Parse("97777777-0000-0000-0000-0000000000a3");

    private static readonly Guid Eleve = Guid.Parse("97777777-0000-0000-0000-0000000000e1");
    private static readonly Guid EleveLibre = Guid.Parse("97777777-0000-0000-0000-0000000000e2");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = Ecole, Name = "Collège B" });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = Ecole, Label = "2026-2027",
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true
        });
        owner.Terms.Add(new Term
        {
            Id = Trimestre, SchoolId = Ecole, SchoolYearId = Annee, Label = "1er trimestre", Order = 1,
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 12, 20)
        });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 });
        owner.Subjects.AddRange(
            new Subject { Id = Maths, SchoolId = Ecole, Name = "Mathématiques", Level = "Collège", Coefficient = 4 },
            new Subject { Id = Francais, SchoolId = Ecole, Name = "Français", Level = "Collège", Coefficient = 2 },
            new Subject { Id = Eps, SchoolId = Ecole, Name = "EPS", Level = "Collège", Coefficient = 1 });
        owner.Students.AddRange(NewStudent(Eleve, "ELEV-0001"), NewStudent(EleveLibre, "ELEV-0002"));

        owner.StudentSubjectExemptions.Add(new StudentSubjectExemption
        {
            SchoolId = Ecole, StudentId = Eleve, SubjectId = Eps, SchoolYearId = Annee, Reason = "Inaptitude médicale"
        });

        foreach (var student in new[] { Eleve, EleveLibre })
        {
            foreach (var (subject, value) in new[] { (Maths, 12m), (Francais, 16m), (Eps, 8m) })
            {
                owner.Grades.Add(new Grade
                {
                    SchoolId = Ecole, StudentId = student, SubjectId = subject, TermId = Trimestre,
                    EvaluationType = EvaluationType.Composition, Value = value
                });
            }
        }

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task The_Report_Card_Carries_The_Exempted_Subject_Outside_Its_Totals_And_Never_Its_Reason()
    {
        await using var db = _db.NewAppContext(Ecole);

        var card = await new ReportCardDataService(new FakeSummaryMediator(db), db)
            .BuildAsync(Eleve, Trimestre, CancellationToken.None);

        card.Subjects.Select(s => s.SubjectId).Should().BeEquivalentTo([Maths, Francais]);
        card.TotalCoefficients.Should().Be(6m);
        card.TotalPoints.Should().Be(80m);
        card.ExemptSubjects.Should().ContainSingle().Which.Should().Be(new ExemptSubjectDto(Eps, "EPS", 1m));
        card.SubjectRanks.Keys.Should().NotContain(Eps, "une matière dispensée n'a pas de rang");
        card.SubjectAppreciations.Keys.Should().NotContain(Eps);
    }

    [Fact]
    public async Task A_Student_Without_Exemption_Gets_A_Report_Card_Without_Exempt_Subjects()
    {
        await using var db = _db.NewAppContext(Ecole);

        var card = await new ReportCardDataService(new FakeSummaryMediator(db), db)
            .BuildAsync(EleveLibre, Trimestre, CancellationToken.None);

        card.ExemptSubjects.Should().BeNullOrEmpty();
        card.Subjects.Select(s => s.SubjectId).Should().BeEquivalentTo([Maths, Francais, Eps]);
    }

    private static Student NewStudent(Guid id, string matricule) => new()
    {
        Id = id, SchoolId = Ecole, Matricule = matricule, FullName = matricule,
        BirthDate = new DateOnly(2011, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe
    };

    /// <summary>Doublure recopiée de ApcEvaluationStructureTests : route le seul GetGradeSummaryQuery vers son vrai handler.</summary>
    private sealed class FakeSummaryMediator(ApplicationDbContext dbContext) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is GetGradeSummaryQuery query)
            {
                return (Task<TResponse>)(object)new GetGradeSummaryQueryHandler(dbContext, new CoefficientOverrideLoader(dbContext), new SubjectFollowScope(dbContext)).Handle(query, cancellationToken);
            }

            throw new NotSupportedException($"FakeSummaryMediator ne sait pas router {request.GetType().Name}.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
