using System.IO.Compression;
using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Grades.Commands.CreateGrade;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Application.ReportCards.Queries.GetClassReportCardsPdf;
using SamaEcole.Application.ReportCards.Queries.GetClassReportCardsZip;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using MediatR;
using Xunit;

namespace SamaEcole.IntegrationTests.ReportCards;

/// <summary>
/// Bulletins de classe (ZIP + PDF fusionné) — les deux réutilisent EXACTEMENT
/// <see cref="ReportCardDataService"/>, donc les mêmes calculs de moyenne/rang que JGK-G03 ; ce test
/// vérifie seulement l'agrégation (un fichier/une page par élève, dans l'ordre alphabétique) et les cas
/// limites (classe vide, classe/trimestre inconnu).
/// </summary>
[Trait("Category", "MultiTenant")]
public class GetClassReportCardsTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid ClasseVide = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000f");
    private static readonly Guid EleveA = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b"); // Zorro (dernier alphabétique)
    private static readonly Guid EleveB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000c"); // Awa (première)
    private static readonly Guid Annee = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");
    private static readonly Guid Trimestre1 = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");
    private static readonly Guid Matiere = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000e");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.Classrooms.AddRange(
            new Classroom { Id = Classe, SchoolId = Ecole, Name = "CM2", Level = "Primaire", Capacity = 40 },
            new Classroom { Id = ClasseVide, SchoolId = Ecole, Name = "CM1", Level = "Primaire", Capacity = 40 });

        // Noms délibérément inversés par rapport à l'ordre de création : EleveA (Zorro) doit sortir en
        // SECOND, EleveB (Awa) en PREMIER — l'ordre alphabétique du tri, pas l'ordre d'insertion.
        owner.Students.AddRange(
            new Student { Id = EleveA, SchoolId = Ecole, Matricule = "ELEV-2026-0001", FullName = "Zorro Diallo", BirthDate = new DateOnly(2015, 1, 1), Gender = "M", ClassroomId = Classe },
            new Student { Id = EleveB, SchoolId = Ecole, Matricule = "ELEV-2026-0002", FullName = "Awa Sow", BirthDate = new DateOnly(2015, 2, 2), Gender = "F", ClassroomId = Classe });

        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = Ecole, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true
        });
        owner.Terms.Add(
            new Term { Id = Trimestre1, SchoolId = Ecole, SchoolYearId = Annee, Label = "1er trimestre", Order = 1, StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 12, 20) });

        owner.Subjects.Add(new Subject { Id = Matiere, SchoolId = Ecole, Name = "Mathématiques", Level = "Primaire", Coefficient = 4 });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static NoOpSchoolLogoProvider Logo => new();

    [Fact]
    public async Task The_Zip_Contains_One_Pdf_Per_Student_Sorted_Alphabetically()
    {
        await using var db = _db.NewAppContext(Ecole);
        var createGrade = new CreateGradeCommandHandler(db, new StubTenantProvider(Ecole));
        await createGrade.Handle(new CreateGradeCommand(EleveA, Matiere, Trimestre1, EvaluationType.Devoir, 12), CancellationToken.None);
        await createGrade.Handle(new CreateGradeCommand(EleveB, Matiere, Trimestre1, EvaluationType.Devoir, 16), CancellationToken.None);

        var sender = new FakeMediator(db);
        var dataService = new ReportCardDataService(sender, db);
        var handler = new GetClassReportCardsZipQueryHandler(db, dataService, new StubPdfGenerator(), Logo);

        var result = await handler.Handle(new GetClassReportCardsZipQuery(Classe, Trimestre1), CancellationToken.None);

        result.Content.Should().NotBeEmpty();
        result.FileName.Should().Be("Bulletins_CM2_1er_trimestre.zip");

        using var archive = new ZipArchive(new MemoryStream(result.Content), ZipArchiveMode.Read);
        archive.Entries.Should().HaveCount(2);
        archive.Entries[0].Name.Should().Be("01_Awa_Sow.pdf");
        archive.Entries[1].Name.Should().Be("02_Zorro_Diallo.pdf");
    }

    [Fact]
    public async Task The_Merged_Pdf_Contains_One_ReportCard_Per_Student_Sorted_Alphabetically()
    {
        await using var db = _db.NewAppContext(Ecole);
        var createGrade = new CreateGradeCommandHandler(db, new StubTenantProvider(Ecole));
        await createGrade.Handle(new CreateGradeCommand(EleveA, Matiere, Trimestre1, EvaluationType.Devoir, 12), CancellationToken.None);
        await createGrade.Handle(new CreateGradeCommand(EleveB, Matiere, Trimestre1, EvaluationType.Devoir, 16), CancellationToken.None);

        var sender = new FakeMediator(db);
        var dataService = new ReportCardDataService(sender, db);
        var stubGenerator = new StubClassBulletinsPdfGenerator();
        var handler = new GetClassReportCardsPdfQueryHandler(db, dataService, stubGenerator, Logo);

        var result = await handler.Handle(new GetClassReportCardsPdfQuery(Classe, Trimestre1), CancellationToken.None);

        result.Content.Should().NotBeEmpty();
        result.FileName.Should().Be("Bulletins_CM2_1er_trimestre.pdf");

        stubGenerator.LastReportCards.Should().HaveCount(2);
        stubGenerator.LastReportCards![0].StudentFullName.Should().Be("Awa Sow");
        stubGenerator.LastReportCards[1].StudentFullName.Should().Be("Zorro Diallo");
    }

    [Fact]
    public async Task An_Empty_Classroom_Raises_A_Business_Rule_Error_For_Both_Zip_And_Merged_Pdf()
    {
        await using var db = _db.NewAppContext(Ecole);
        var sender = new FakeMediator(db);
        var dataService = new ReportCardDataService(sender, db);

        var zipHandler = new GetClassReportCardsZipQueryHandler(db, dataService, new StubPdfGenerator(), Logo);
        var pdfHandler = new GetClassReportCardsPdfQueryHandler(db, dataService, new StubClassBulletinsPdfGenerator(), Logo);

        await FluentActions.Awaiting(() => zipHandler.Handle(new GetClassReportCardsZipQuery(ClasseVide, Trimestre1), CancellationToken.None))
            .Should().ThrowAsync<BusinessRuleException>();

        await FluentActions.Awaiting(() => pdfHandler.Handle(new GetClassReportCardsPdfQuery(ClasseVide, Trimestre1), CancellationToken.None))
            .Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task An_Unknown_Classroom_Raises_A_Not_Found_Error()
    {
        await using var db = _db.NewAppContext(Ecole);
        var sender = new FakeMediator(db);
        var dataService = new ReportCardDataService(sender, db);
        var handler = new GetClassReportCardsZipQueryHandler(db, dataService, new StubPdfGenerator(), Logo);

        await FluentActions.Awaiting(() => handler.Handle(new GetClassReportCardsZipQuery(Guid.NewGuid(), Trimestre1), CancellationToken.None))
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

    /// <summary>Génère un faux PDF (jamais un vrai rendu QuestPDF) : rapide, et suffit à vérifier
    /// l'agrégation en ZIP sans dépendre de la mise en page du bulletin.</summary>
    private sealed class StubPdfGenerator : IReportCardPdfGenerator
    {
        public byte[] Generate(ReportCardDto reportCard, byte[]? logo) => [1, 2, 3];
    }

    /// <summary>Capture la liste de ReportCardDto reçue, sans jamais générer de vrai PDF fusionné.</summary>
    private sealed class StubClassBulletinsPdfGenerator : IClassBulletinsPdfGenerator
    {
        public IReadOnlyList<ReportCardDto>? LastReportCards { get; private set; }

        public byte[] Generate(IReadOnlyList<ReportCardDto> reportCards, byte[]? logo)
        {
            LastReportCards = reportCards;
            return [1, 2, 3];
        }
    }

    /// <summary>
    /// Dispatch minimal, juste assez pour que ReportCardDataService puisse appeler GetGradeSummaryQuery
    /// en interne (JGK-G02) — pas de conteneur DI, comme le reste des tests d'intégration qui instancient
    /// les Handlers directement.
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
