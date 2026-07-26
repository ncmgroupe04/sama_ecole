using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Grades.Commands.CreateGrade;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.ReportCards;

/// <summary>
/// Ticket JGK-G03 — le Handler calcule-t-il RÉELLEMENT le bon rang et la bonne moyenne annuelle contre
/// un PostgreSQL réel ? Exercé exactement comme GradeSummaryTests pour JGK-G02.
/// </summary>
[Trait("Category", "MultiTenant")]
public class GetReportCardPdfTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid EleveA = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b"); // meilleur
    private static readonly Guid EleveB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000c"); // moyen
    private static readonly Guid EleveC = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000d"); // moins bon
    private static readonly Guid Annee = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");
    private static readonly Guid Trimestre1 = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");
    private static readonly Guid Trimestre2 = Guid.Parse("dddddddd-0000-0000-0000-00000000000e");
    private static readonly Guid Matiere = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000e");

    // Seconde classe du MÊME établissement, sur un autre cycle : c'est tout l'objet du test d'en-tête.
    private static readonly Guid ClassePrimaire = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000b");
    private static readonly Guid ElevePrimaire = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000e");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        // Classe du SECONDAIRE, cycle explicite : les tests de rang et de moyenne annuelle ci-dessous
        // reposent sur la moyenne pondérée /20. Un « CM2 / Primaire » laissé sur le défaut College —
        // ce qu'elle était — mêlait la nomenclature du primaire au comportement du collège.
        owner.Classrooms.Add(new Classroom
        {
            Id = Classe, SchoolId = Ecole, Name = "3e A", Level = "Collège",
            Cycle = CycleType.College, Capacity = 40
        });

        owner.Students.AddRange(
            new Student { Id = EleveA, SchoolId = Ecole, Matricule = "ELEV-2026-0001", FullName = "Awa (meilleure)", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe },
            new Student { Id = EleveB, SchoolId = Ecole, Matricule = "ELEV-2026-0002", FullName = "Modou (moyen)", BirthDate = new DateOnly(2015, 2, 2), BirthPlace = "Dakar", Gender = "M", ClassroomId = Classe },
            new Student { Id = EleveC, SchoolId = Ecole, Matricule = "ELEV-2026-0003", FullName = "Fatou (moins bonne)", BirthDate = new DateOnly(2015, 3, 3), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe });

        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = Ecole, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true
        });
        owner.Terms.AddRange(
            new Term { Id = Trimestre1, SchoolId = Ecole, SchoolYearId = Annee, Label = "1er trimestre", Order = 1, StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 12, 20) },
            new Term { Id = Trimestre2, SchoolId = Ecole, SchoolYearId = Annee, Label = "2e trimestre", Order = 2, StartDate = new DateOnly(2027, 1, 5), EndDate = new DateOnly(2027, 3, 20) });

        owner.Subjects.Add(new Subject { Id = Matiere, SchoolId = Ecole, Name = "Mathématiques", Level = "Primaire", Coefficient = 4 });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static CreateGradeCommandHandler NewCreateGradeHandler(IApplicationDbContext db) =>
        new(db, new StubTenantProvider(Ecole));

    private static NoOpSchoolLogoProvider Logo => new();

    [Fact]
    public async Task The_Best_Student_Gets_Rank_One_And_The_Others_Follow_By_Average()
    {
        await using var db = _db.NewAppContext(Ecole);
        var createGrade = NewCreateGradeHandler(db);

        await createGrade.Handle(new CreateGradeCommand(EleveA, Matiere, Trimestre1, EvaluationType.Devoir1, 18), CancellationToken.None);
        await createGrade.Handle(new CreateGradeCommand(EleveB, Matiere, Trimestre1, EvaluationType.Devoir1, 12), CancellationToken.None);
        await createGrade.Handle(new CreateGradeCommand(EleveC, Matiere, Trimestre1, EvaluationType.Devoir1, 8), CancellationToken.None);

        var sender = new FakeMediator(db);
        var handler = new GetReportCardPdfQueryHandler(new ReportCardDataService(sender, db), new StubPdfGenerator(), Logo);

        var bestResult = await handler.Handle(new GetReportCardPdfQuery(EleveA, Trimestre1), CancellationToken.None);
        bestResult.Content.Should().NotBeEmpty();
        StubPdfGenerator.LastReportCard!.GeneralRank.Should().Be(1);

        await handler.Handle(new GetReportCardPdfQuery(EleveB, Trimestre1), CancellationToken.None);
        StubPdfGenerator.LastReportCard!.GeneralRank.Should().Be(2);

        await handler.Handle(new GetReportCardPdfQuery(EleveC, Trimestre1), CancellationToken.None);
        StubPdfGenerator.LastReportCard!.GeneralRank.Should().Be(3);
    }

    [Fact]
    public async Task Tied_Averages_Share_The_Same_Rank_And_The_Next_Rank_Skips()
    {
        await using var db = _db.NewAppContext(Ecole);
        var createGrade = NewCreateGradeHandler(db);

        await createGrade.Handle(new CreateGradeCommand(EleveA, Matiere, Trimestre1, EvaluationType.Devoir1, 15), CancellationToken.None);
        await createGrade.Handle(new CreateGradeCommand(EleveB, Matiere, Trimestre1, EvaluationType.Devoir1, 15), CancellationToken.None);
        await createGrade.Handle(new CreateGradeCommand(EleveC, Matiere, Trimestre1, EvaluationType.Devoir1, 10), CancellationToken.None);

        var handler = new GetReportCardPdfQueryHandler(new ReportCardDataService(new FakeMediator(db), db), new StubPdfGenerator(), Logo);

        await handler.Handle(new GetReportCardPdfQuery(EleveA, Trimestre1), CancellationToken.None);
        StubPdfGenerator.LastReportCard!.GeneralRank.Should().Be(1);

        await handler.Handle(new GetReportCardPdfQuery(EleveB, Trimestre1), CancellationToken.None);
        StubPdfGenerator.LastReportCard!.GeneralRank.Should().Be(1, "à égalité avec Awa, Modou partage le même rang");

        await handler.Handle(new GetReportCardPdfQuery(EleveC, Trimestre1), CancellationToken.None);
        StubPdfGenerator.LastReportCard!.GeneralRank.Should().Be(3, "le rang saute de 2 puisque deux élèves partagent déjà le rang 1");
    }

    [Fact]
    public async Task The_Annual_Average_Ignores_Terms_Not_Yet_Graded()
    {
        await using var db = _db.NewAppContext(Ecole);
        var createGrade = NewCreateGradeHandler(db);

        // Seul le 1er trimestre est noté ; le 2e trimestre n'a encore aucune note pour personne.
        await createGrade.Handle(new CreateGradeCommand(EleveA, Matiere, Trimestre1, EvaluationType.Devoir1, 14), CancellationToken.None);

        var handler = new GetReportCardPdfQueryHandler(new ReportCardDataService(new FakeMediator(db), db), new StubPdfGenerator(), Logo);

        await handler.Handle(new GetReportCardPdfQuery(EleveA, Trimestre1), CancellationToken.None);
        var reportCard = StubPdfGenerator.LastReportCard!;

        reportCard.TermRecaps.Should().Contain(r => r.TermLabel == "1er trimestre" && r.Average == 14);
        reportCard.TermRecaps.Should().Contain(r => r.TermLabel == "2e trimestre" && r.Average == null);
        reportCard.AnnualAverage.Should().Be(14, "le seul trimestre noté détermine seul la moyenne annuelle à ce stade");
    }

    /// <summary>
    /// L'en-tête du bulletin suit le CYCLE DE LA CLASSE de l'élève, pas un réglage global : dans le même
    /// établissement, le bulletin d'un CM1 s'intitule « ÉCOLE ÉLÉMENTAIRE DE » là où celui d'une 3e
    /// s'intitule « COLLÈGE DE » — un « LYCÉE DE » codé en dur s'imprimait auparavant sur les trois.
    ///
    /// Le nom est ici saisi AVEC son préfixe (« LYCÉE DE POPENGUINE »), cas parfaitement réel puisque le
    /// champ est libre : il doit être nettoyé, sans quoi l'en-tête cumulerait deux cycles contradictoires.
    /// </summary>
    [Fact]
    public async Task The_Heading_Follows_The_Cycle_Of_The_Student_Classroom()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            var school = await owner.Schools.FirstAsync(s => s.Id == Ecole);
            school.NomLycee = "LYCÉE DE POPENGUINE";

            owner.Classrooms.Add(new Classroom
            {
                Id = ClassePrimaire, SchoolId = Ecole, Name = "CM1", Level = "Primaire",
                Cycle = CycleType.Primaire, Capacity = 40
            });
            owner.Students.Add(new Student
            {
                Id = ElevePrimaire, SchoolId = Ecole, Matricule = "ELEV-2026-0004",
                FullName = "Ndeye Primaire", BirthDate = new DateOnly(2016, 4, 4),
                BirthPlace = "Dakar", Gender = "F", ClassroomId = ClassePrimaire
            });

            await owner.SaveChangesAsync(CancellationToken.None);
        }

        await using var db = _db.NewAppContext(Ecole);
        var handler = new GetReportCardPdfQueryHandler(new ReportCardDataService(new FakeMediator(db), db), new StubPdfGenerator(), Logo);

        // EleveA appartient à la classe d'origine, une 3e du cycle Collège.
        await handler.Handle(new GetReportCardPdfQuery(EleveA, Trimestre1), CancellationToken.None);
        var college = StubPdfGenerator.LastReportCard!;

        await handler.Handle(new GetReportCardPdfQuery(ElevePrimaire, Trimestre1), CancellationToken.None);
        var primaire = StubPdfGenerator.LastReportCard!;

        college.HeadingPrefix.Should().Be("COLLÈGE DE");
        primaire.HeadingPrefix.Should().Be("ÉCOLE ÉLÉMENTAIRE DE",
            "le bulletin d'un CM1 ne doit jamais s'intituler LYCÉE, même dans un établissement qui en abrite un");

        primaire.HeadingName.Should().Be("POPENGUINE",
            "le préfixe saisi par l'école est retiré, sans quoi l'en-tête afficherait « ÉCOLE ÉLÉMENTAIRE DE : LYCÉE DE POPENGUINE »");
    }

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    private sealed class NoOpSchoolLogoProvider : ISchoolLogoProvider
    {
        public Task<byte[]?> TryFetchAsync(string? logoUrl, CancellationToken cancellationToken) =>
            Task.FromResult<byte[]?>(null);
    }

    /// <summary>Capture le dernier ReportCardDto reçu, sans jamais générer de vrai PDF (test rapide et isolé).</summary>
    private sealed class StubPdfGenerator : IReportCardPdfGenerator
    {
        public static ReportCardDto? LastReportCard { get; private set; }

        public byte[] Generate(ReportCardDto reportCard, byte[]? logo)
        {
            LastReportCard = reportCard;
            return [1, 2, 3];
        }
    }

    /// <summary>
    /// Dispatch minimal, juste assez pour que GetReportCardPdfQueryHandler puisse appeler
    /// GetGradeSummaryQuery en interne (JGK-G02) — pas de conteneur DI, comme le reste des tests
    /// d'intégration qui instancient les Handlers directement.
    /// </summary>
    private sealed class FakeMediator(ApplicationDbContext dbContext) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is GetGradeSummaryQuery query)
            {
                var handler = new GetGradeSummaryQueryHandler(dbContext);
                return (Task<TResponse>)(object)handler.Handle(query, cancellationToken);
            }

            throw new NotSupportedException($"FakeMediator ne sait pas router {request.GetType().Name}.");
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
