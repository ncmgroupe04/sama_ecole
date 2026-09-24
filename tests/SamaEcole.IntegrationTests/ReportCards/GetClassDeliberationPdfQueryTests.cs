using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Grades.Commands.CreateGrade;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Application.ReportCards.Queries.GetClassDeliberationPdf;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using MediatR;
using Xunit;

namespace SamaEcole.IntegrationTests.ReportCards;

/// <summary>
/// PV de délibération de classe (ticket JGK-G03, module Comptabilité &amp; Fiscalité) — jusqu'ici sans
/// AUCUNE couverture de test, alors que <see cref="ClassDeliberationDocument"/>/<see cref="ClassDeliberationPdfGenerator"/>
/// sont pleinement câblés au contrôleur et au front (grades.js). Contrairement au ZIP/PDF fusionné
/// (<see cref="GetClassReportCardsTests"/>, tri alphabétique), le PV trie par RANG (ordre de mérite) —
/// la distinction que ce test doit prouver, pas seulement réutiliser.
/// </summary>
[Trait("Category", "MultiTenant")]
public class GetClassDeliberationPdfQueryTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid ClasseVide = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000f");
    private static readonly Guid ClasseEcoleB = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000b");

    // Noms choisis pour contredire l'ordre alphabétique : si le PV triait par nom (comme le ZIP), Awa
    // sortirait avant Zorro. Le classement par RANG doit inverser cet ordre.
    private static readonly Guid Zorro = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000a"); // 18/20 : meilleur
    private static readonly Guid Awa = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");   // 8/20 : moyen
    private static readonly Guid Moussa = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000c"); // aucune note

    private static readonly Guid Annee = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");
    private static readonly Guid Trimestre1 = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");
    private static readonly Guid Matiere = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000e");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        owner.Classrooms.AddRange(
            new Classroom { Id = Classe, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 40 },
            new Classroom { Id = ClasseVide, SchoolId = EcoleA, Name = "CM1", Level = "Primaire", Capacity = 40 },
            new Classroom { Id = ClasseEcoleB, SchoolId = EcoleB, Name = "CM2", Level = "Primaire", Capacity = 40 });

        owner.Students.AddRange(
            new Student { Id = Zorro, SchoolId = EcoleA, Matricule = "ELEV-2026-0001", FullName = "Zorro Diallo", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = Classe },
            new Student { Id = Awa, SchoolId = EcoleA, Matricule = "ELEV-2026-0002", FullName = "Awa Sow", BirthDate = new DateOnly(2015, 2, 2), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe },
            new Student { Id = Moussa, SchoolId = EcoleA, Matricule = "ELEV-2026-0003", FullName = "Moussa Ba", BirthDate = new DateOnly(2015, 3, 3), BirthPlace = "Dakar", Gender = "M", ClassroomId = Classe });

        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = EcoleA, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true
        });
        owner.Terms.Add(new Term { Id = Trimestre1, SchoolId = EcoleA, SchoolYearId = Annee, Label = "1er trimestre", Order = 1, StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 12, 20) });

        owner.Subjects.Add(new Subject { Id = Matiere, SchoolId = EcoleA, Name = "Mathématiques", Level = "Primaire", Coefficient = 4 });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static NoOpSchoolLogoProvider Logo => new();

    [Fact]
    public async Task Students_Are_Sorted_By_Merit_Rank_Not_Alphabetically()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var createGrade = new CreateGradeCommandHandler(db, new StubTenantProvider(EcoleA), new TestCurrentUser());
        await createGrade.Handle(new CreateGradeCommand(Zorro, Matiere, Trimestre1, EvaluationType.Devoir1, 18), CancellationToken.None);
        await createGrade.Handle(new CreateGradeCommand(Awa, Matiere, Trimestre1, EvaluationType.Devoir1, 8), CancellationToken.None);
        // Moussa : aucune note saisie.

        var sender = new FakeSender(db);
        var dataService = new ReportCardDataService(sender, db);
        var stubGenerator = new StubDeliberationPdfGenerator();
        var handler = new GetClassDeliberationPdfQueryHandler(db, dataService, stubGenerator, Logo);

        var result = await handler.Handle(new GetClassDeliberationPdfQuery(Classe, Trimestre1), CancellationToken.None);

        result.Content.Should().NotBeEmpty();
        result.FileName.Should().Be("PV_CM2_1er_trimestre.pdf");

        stubGenerator.LastReportCards.Should().HaveCount(3);
        stubGenerator.LastReportCards![0].StudentFullName.Should().Be("Zorro Diallo", "18/20 : la meilleure moyenne, malgré Z en tête alphabétiquement");
        stubGenerator.LastReportCards[1].StudentFullName.Should().Be("Awa Sow", "8/20 : moyenne devant l'élève sans note");
        stubGenerator.LastReportCards[2].StudentFullName.Should().Be("Moussa Ba", "aucune note saisie : dernier du classement");
    }

    [Fact]
    public async Task An_Empty_Classroom_Raises_A_Business_Rule_Error()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var sender = new FakeSender(db);
        var dataService = new ReportCardDataService(sender, db);
        var handler = new GetClassDeliberationPdfQueryHandler(db, dataService, new StubDeliberationPdfGenerator(), Logo);

        await FluentActions.Awaiting(() => handler.Handle(new GetClassDeliberationPdfQuery(ClasseVide, Trimestre1), CancellationToken.None))
            .Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task An_Unknown_Classroom_Raises_A_Not_Found_Error()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var sender = new FakeSender(db);
        var dataService = new ReportCardDataService(sender, db);
        var handler = new GetClassDeliberationPdfQueryHandler(db, dataService, new StubDeliberationPdfGenerator(), Logo);

        await FluentActions.Awaiting(() => handler.Handle(new GetClassDeliberationPdfQuery(Guid.NewGuid(), Trimestre1), CancellationToken.None))
            .Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task An_Unknown_Term_Raises_A_Not_Found_Error()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var sender = new FakeSender(db);
        var dataService = new ReportCardDataService(sender, db);
        var handler = new GetClassDeliberationPdfQueryHandler(db, dataService, new StubDeliberationPdfGenerator(), Logo);

        await FluentActions.Awaiting(() => handler.Handle(new GetClassDeliberationPdfQuery(Classe, Guid.NewGuid()), CancellationToken.None))
            .Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Another_Schools_Classroom_Is_Not_Reachable()
    {
        // École A tente le PV d'une classe de l'École B : RLS + filtre EF la rendent introuvable,
        // jamais un PV avec les élèves d'un autre établissement.
        await using var db = _db.NewAppContext(EcoleA);
        var sender = new FakeSender(db);
        var dataService = new ReportCardDataService(sender, db);
        var handler = new GetClassDeliberationPdfQueryHandler(db, dataService, new StubDeliberationPdfGenerator(), Logo);

        await FluentActions.Awaiting(() => handler.Handle(new GetClassDeliberationPdfQuery(ClasseEcoleB, Trimestre1), CancellationToken.None))
            .Should().ThrowAsync<KeyNotFoundException>();
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

    /// <summary>Capture la liste de ReportCardDto reçue (déjà triée par rang), sans générer de vrai PDF.</summary>
    private sealed class StubDeliberationPdfGenerator : IClassDeliberationPdfGenerator
    {
        public IReadOnlyList<ReportCardDto>? LastReportCards { get; private set; }

        public byte[] Generate(IReadOnlyList<ReportCardDto> reportCards, byte[]? schoolLogo)
        {
            LastReportCards = reportCards;
            return [1, 2, 3];
        }
    }

    /// <summary>
    /// Dispatch minimal, juste assez pour que ReportCardDataService puisse appeler GetGradeSummaryQuery
    /// en interne (JGK-G02) — même patron que GetClassReportCardsTests.FakeMediator.
    /// </summary>
    private sealed class FakeSender(ApplicationDbContext dbContext) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is GetGradeSummaryQuery query)
            {
                var handler = new GetGradeSummaryQueryHandler(dbContext);
                return (Task<TResponse>)(object)handler.Handle(query, cancellationToken);
            }

            throw new NotSupportedException($"FakeSender ne sait pas router {request.GetType().Name}.");
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
