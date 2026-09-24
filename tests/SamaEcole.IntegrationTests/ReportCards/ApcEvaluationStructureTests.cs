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
using Xunit;

namespace SamaEcole.IntegrationTests.ReportCards;

/// <summary>
/// La grille d'évaluation par compétences, bout en bout contre un PostgreSQL réel : une structure
/// réellement enregistrée en base (domaines, activités, barèmes /40 et /60) doit ressortir dans le
/// <see cref="ReportCardDto.EvaluationStructure"/> que reçoit le générateur PDF, avec les notes en face
/// des bonnes lignes et les cases non notées restées vides.
///
/// Le jeu de données reproduit la grille officielle du CE1-CE2 : Français et Maths, chacun en
/// « Ressources » /40 et « Compétences » /60.
/// </summary>
[Trait("Category", "MultiTenant")]
public class ApcEvaluationStructureTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-1111-0000-0000-00000000000a");
    private static readonly Guid Eleve = Guid.Parse("bbbbbbbb-1111-0000-0000-00000000000b");
    private static readonly Guid Annee = Guid.Parse("cccccccc-1111-0000-0000-00000000000c");
    private static readonly Guid Trimestre = Guid.Parse("dddddddd-1111-0000-0000-00000000000d");

    private static readonly Guid Francais = Guid.Parse("eeeeeeee-1111-0000-0000-000000000001");
    private static readonly Guid FrancaisRessources = Guid.Parse("eeeeeeee-1111-0000-0000-000000000002");
    private static readonly Guid FrancaisCompetences = Guid.Parse("eeeeeeee-1111-0000-0000-000000000003");
    private static readonly Guid Maths = Guid.Parse("eeeeeeee-1111-0000-0000-000000000004");
    private static readonly Guid MathsRessources = Guid.Parse("eeeeeeee-1111-0000-0000-000000000005");
    private static readonly Guid MathsCompetences = Guid.Parse("eeeeeeee-1111-0000-0000-000000000006");
    private static readonly Guid Conduite = Guid.Parse("eeeeeeee-1111-0000-0000-000000000007");

    private const string Niveau = "CE1-CE2";

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = Ecole, Name = "École APC" });
        owner.Classrooms.Add(new Classroom
        {
            Id = Classe, SchoolId = Ecole, Name = "CE1 A", Level = Niveau,
            Cycle = CycleType.Primaire, Capacity = 40
        });
        owner.Students.Add(new Student
        {
            Id = Eleve, SchoolId = Ecole, Matricule = "ELEV-2026-0001", FullName = "Awa Diop",
            BirthDate = new DateOnly(2018, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe
        });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = Ecole, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true
        });
        owner.Terms.Add(new Term
        {
            Id = Trimestre, SchoolId = Ecole, SchoolYearId = Annee, Label = "1er trimestre", Order = 1,
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 12, 20)
        });

        // Deux domaines et leurs activités, plus une matière SIMPLE (« Conduite ») au milieu de la
        // grille : elle doit s'imprimer sur une ligne, sans fusion de cellule.
        owner.Subjects.AddRange(
            new Subject
            {
                Id = Francais, SchoolId = Ecole, Name = "Français", Level = Niveau, Coefficient = 4,
                DisplayOrder = 1, Column1Header = "Activités", Column2Header = "Contrôles"
            },
            new Subject { Id = FrancaisRessources, SchoolId = Ecole, Name = "Ressources", Level = Niveau, Coefficient = 4, ParentSubjectId = Francais, MaxScore = 40, DisplayOrder = 1 },
            new Subject { Id = FrancaisCompetences, SchoolId = Ecole, Name = "Compétences", Level = Niveau, Coefficient = 4, ParentSubjectId = Francais, MaxScore = 60, DisplayOrder = 2 },

            new Subject { Id = Maths, SchoolId = Ecole, Name = "Maths", Level = Niveau, Coefficient = 4, DisplayOrder = 2 },
            new Subject { Id = MathsRessources, SchoolId = Ecole, Name = "Ressources", Level = Niveau, Coefficient = 4, ParentSubjectId = Maths, MaxScore = 40, DisplayOrder = 1 },
            new Subject { Id = MathsCompetences, SchoolId = Ecole, Name = "Compétences", Level = Niveau, Coefficient = 4, ParentSubjectId = Maths, MaxScore = 60, DisplayOrder = 2 },

            new Subject { Id = Conduite, SchoolId = Ecole, Name = "Conduite", Level = Niveau, Coefficient = 1, DisplayOrder = 3 });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static async Task<ReportCardDto> BuildReportCardAsync(ApplicationDbContext db) =>
        await new ReportCardDataService(new FakeSummaryMediator(db), db)
            .BuildAsync(Eleve, Trimestre, CancellationToken.None);

    /// <summary>
    /// Le nerf du sujet : la structure ressort telle qu'elle a été configurée — entêtes de colonnes,
    /// ordre des domaines, ordre des activités, et un barème « Sur » PAR LIGNE.
    /// </summary>
    [Fact]
    public async Task The_Configured_Grid_Reaches_The_Report_Card_With_Its_Own_Headers_And_Scales()
    {
        await using var db = _db.NewAppContext(Ecole);

        var reportCard = await BuildReportCardAsync(db);
        var structure = reportCard.EvaluationStructure;

        structure.Should().NotBeNull("le niveau déclare une hiérarchie, le bulletin doit basculer sur le tableau APC");
        structure!.Column1Header.Should().Be("Activités");
        structure.Column2Header.Should().Be("Contrôles");

        structure.Groups.Select(g => g.Name).Should().Equal("Français", "Maths", "Conduite");

        var francais = structure.Groups[0];
        francais.Lines.Select(l => l.Label).Should().Equal("Ressources", "Compétences");
        francais.Lines.Select(l => l.MaxScore).Should().Equal(40m, 60m);

        // Matière simple : UNE ligne, sans libellé de seconde colonne (le document lui donne alors les
        // deux premières colonnes, sans RowSpan).
        var conduite = structure.Groups[2];
        conduite.Lines.Should().ContainSingle();
        conduite.Lines[0].Label.Should().BeNull();
    }

    /// <summary>
    /// Les notes atterrissent en face de LEUR ligne, et les lignes non notées gardent une case vide —
    /// jamais un zéro, qui vaudrait échec sur un document officiel. C'est ce qui distingue une grille
    /// imprimée en entier d'un tableau reconstruit depuis les seules notes saisies.
    /// </summary>
    [Fact]
    public async Task Graded_Lines_Carry_Their_Score_And_Ungraded_Ones_Stay_Empty()
    {
        await using var db = _db.NewAppContext(Ecole);
        var createGrade = new CreateGradeCommandHandler(db, new StubTenantProvider(Ecole), new TestCurrentUser());

        // 32/40 en Ressources (80 %) ; rien ailleurs.
        await createGrade.Handle(
            new CreateGradeCommand(Eleve, FrancaisRessources, Trimestre, EvaluationType.Composition, 32),
            CancellationToken.None);

        var structure = (await BuildReportCardAsync(db)).EvaluationStructure!;
        var francais = structure.Groups.Single(g => g.Name == "Français");

        var ressources = francais.Lines.Single(l => l.Label == "Ressources");
        ressources.Score.Should().Be(32m);
        ressources.MaxScore.Should().Be(40m);
        ressources.Appreciation.Should().Be("Excellent", "32/40 vaut 80 %, le seuil de la mention la plus haute");

        var competences = francais.Lines.Single(l => l.Label == "Compétences");
        competences.Score.Should().BeNull();
        competences.Appreciation.Should().BeNull();
    }

    /// <summary>
    /// Une note de 32 est ACCEPTÉE sur une ligne notée sur 40, dans une classe de cycle Primaire dont le
    /// barème par défaut est /10. C'est la preuve que le plafond suit bien la matière et non le cycle —
    /// sans quoi aucune grille APC ne serait saisissable.
    /// </summary>
    [Fact]
    public async Task A_Score_Above_The_Cycle_Scale_Is_Accepted_When_The_Subject_Allows_It()
    {
        await using var db = _db.NewAppContext(Ecole);
        var createGrade = new CreateGradeCommandHandler(db, new StubTenantProvider(Ecole), new TestCurrentUser());

        var act = async () => await createGrade.Handle(
            new CreateGradeCommand(Eleve, FrancaisCompetences, Trimestre, EvaluationType.Composition, 55),
            CancellationToken.None);

        await act.Should().NotThrowAsync("la ligne « Compétences » est notée sur 60, pas sur le /10 du cycle");
    }

    /// <summary>Au-dessus du barème de SA ligne, en revanche, la note reste refusée.</summary>
    [Fact]
    public async Task A_Score_Above_The_Subject_Max_Score_Is_Still_Refused()
    {
        await using var db = _db.NewAppContext(Ecole);
        var createGrade = new CreateGradeCommandHandler(db, new StubTenantProvider(Ecole), new TestCurrentUser());

        var act = async () => await createGrade.Handle(
            new CreateGradeCommand(Eleve, FrancaisRessources, Trimestre, EvaluationType.Composition, 41),
            CancellationToken.None);

        await act.Should().ThrowAsync<Application.Common.Exceptions.ValidationException>();
    }

    /// <summary>
    /// Un DOMAINE ne se note pas : la note se saisit sur ses activités. L'accepter produirait une note
    /// invisible sur le bulletin, dont le tableau n'imprime que les lignes.
    /// </summary>
    [Fact]
    public async Task Grading_A_Domain_Is_Refused()
    {
        await using var db = _db.NewAppContext(Ecole);
        var createGrade = new CreateGradeCommandHandler(db, new StubTenantProvider(Ecole), new TestCurrentUser());

        var act = async () => await createGrade.Handle(
            new CreateGradeCommand(Eleve, Francais, Trimestre, EvaluationType.Composition, 15),
            CancellationToken.None);

        await act.Should().ThrowAsync<Application.Common.Exceptions.ValidationException>();
    }

    /// <summary>
    /// Deux lignes de barèmes différents mais de MÊME pourcentage donnent une moyenne générale égale à ce
    /// pourcentage, exprimée sur le barème du bulletin (/10 au primaire). Moyenner les notes brutes aurait
    /// donné (32 + 48) ÷ 2 = 40 — un nombre qui ne veut rien dire sur aucun barème.
    /// </summary>
    [Fact]
    public async Task The_General_Average_Rebases_Lines_Of_Different_Scales_Before_Averaging()
    {
        await using var db = _db.NewAppContext(Ecole);
        var createGrade = new CreateGradeCommandHandler(db, new StubTenantProvider(Ecole), new TestCurrentUser());

        // 32/40 et 48/60 : 80 % dans les deux cas → 8/10 sur le barème du cycle primaire.
        await createGrade.Handle(new CreateGradeCommand(Eleve, FrancaisRessources, Trimestre, EvaluationType.Composition, 32), CancellationToken.None);
        await createGrade.Handle(new CreateGradeCommand(Eleve, FrancaisCompetences, Trimestre, EvaluationType.Composition, 48), CancellationToken.None);

        var summary = await new GetGradeSummaryQueryHandler(db)
            .Handle(new GetGradeSummaryQuery(Eleve, Trimestre), CancellationToken.None);

        summary.GeneralAverage.Should().Be(8m);

        // La moyenne PAR LIGNE reste brute, sur le barème de sa ligne : c'est la note que le bulletin
        // imprime en face de son « Sur ».
        summary.Subjects.Single(s => s.SubjectId == FrancaisCompetences).Average.Should().Be(48m);
        summary.Subjects.Single(s => s.SubjectId == FrancaisCompetences).MaxScore.Should().Be(60m);
    }

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    /// <summary>Dispatch minimal : ReportCardDataService n'appelle que GetGradeSummaryQuery.</summary>
    private sealed class FakeSummaryMediator(ApplicationDbContext dbContext) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is GetGradeSummaryQuery query)
            {
                return (Task<TResponse>)(object)new GetGradeSummaryQueryHandler(dbContext).Handle(query, cancellationToken);
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
