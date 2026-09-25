using SamaEcole.Application.ClassSubjects;
using FluentAssertions;
using SamaEcole.Application.Coefficients;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Grades.Commands.CreateGrade;
using SamaEcole.Application.Grades.Commands.CreateMention;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Application.Grades.Queries.GetMentions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Grades;

/// <summary>
/// Ticket JGK-G02 — le calcul des moyennes/mentions exerce le vrai Handler contre un PostgreSQL réel,
/// exactement comme EnrollmentTests pour le calcul financier.
/// </summary>
[Trait("Category", "MultiTenant")]
public class GradeSummaryTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid Eleve = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid Annee = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");
    private static readonly Guid Trimestre = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");
    private static readonly Guid Maths = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000e");
    private static readonly Guid Francais = Guid.Parse("ffffffff-0000-0000-0000-00000000000f");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.Students.Add(new Student
        {
            Id = Eleve, SchoolId = Ecole, Matricule = "ELEV-2026-0001", FullName = "Élève de test",
            BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = Classe
        });
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
        // Coefficients volontairement différents : une moyenne générale plate (ex. moyenne des
        // moyennes non pondérée) donnerait un résultat différent — le test doit distinguer les deux.
        owner.Subjects.Add(new Subject { Id = Maths, SchoolId = Ecole, Name = "Mathématiques", Level = "Primaire", Coefficient = 4 });
        owner.Subjects.Add(new Subject { Id = Francais, SchoolId = Ecole, Name = "Français", Level = "Primaire", Coefficient = 2 });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static CreateGradeCommandHandler NewCreateGradeHandler(IApplicationDbContext db) =>
        new(db, new StubTenantProvider(Ecole), new TestCurrentUser(), new SubjectFollowScope(db));

    private static CreateMentionCommandHandler NewCreateMentionHandler(IApplicationDbContext db) =>
        new(db, new StubTenantProvider(Ecole));

    [Fact]
    public async Task The_General_Average_Is_Weighted_By_Subject_Coefficients()
    {
        await using var db = _db.NewAppContext(Ecole);
        var createGrade = NewCreateGradeHandler(db);

        // Maths (coeff 4) : Devoir 10, Composition 14 -> moyenne 12.
        await createGrade.Handle(new CreateGradeCommand(Eleve, Maths, Trimestre, EvaluationType.Devoir1, 10), CancellationToken.None);
        await createGrade.Handle(new CreateGradeCommand(Eleve, Maths, Trimestre, EvaluationType.Composition, 14), CancellationToken.None);

        // Français (coeff 2) : Devoir 16 seul (Composition pas encore saisie) -> moyenne 16.
        await createGrade.Handle(new CreateGradeCommand(Eleve, Francais, Trimestre, EvaluationType.Devoir1, 16), CancellationToken.None);

        var summary = await new GetGradeSummaryQueryHandler(db, new CoefficientOverrideLoader(db), new SubjectFollowScope(db)).Handle(
            new GetGradeSummaryQuery(Eleve, Trimestre), CancellationToken.None);

        summary.Subjects.Should().HaveCount(2);

        var maths = summary.Subjects.Single(s => s.SubjectId == Maths);
        maths.Devoir1.Should().Be(10);
        maths.Composition.Should().Be(14);
        maths.Average.Should().Be(12);
        maths.WeightedPoints.Should().Be(48); // 12 * 4

        var francais = summary.Subjects.Single(s => s.SubjectId == Francais);
        francais.Devoir1.Should().Be(16);
        francais.Composition.Should().BeNull("la Composition n'a pas encore été saisie");
        francais.Average.Should().Be(16);
        francais.WeightedPoints.Should().Be(32); // 16 * 2

        summary.TotalCoefficients.Should().Be(6); // 4 + 2
        summary.TotalPoints.Should().Be(80); // 48 + 32
        summary.GeneralAverage.Should().Be(80m / 6m); // pondérée, PAS (12+16)/2 = 14
    }

    /// <summary>
    /// Étape 3 (système hybride) — une classe de cycle Primaire calcule une moyenne SIMPLE sur /10 :
    /// les coefficients des matières sont neutralisés à 1 et aucune mention n'est attribuée (réservée au
    /// secondaire). La classe par défaut des autres tests est en cycle College (pondérée /20) — les deux
    /// comportements coexistent dans la même école.
    /// </summary>
    [Fact]
    public async Task In_A_Primaire_Class_The_General_Average_Is_A_Simple_Mean_Without_Coefficients_Or_Mention()
    {
        var primaireClasse = Guid.Parse("a1a1a1a1-0000-0000-0000-0000000000a1");
        var primaireEleve = Guid.Parse("b1b1b1b1-0000-0000-0000-0000000000b1");

        await using (var owner = _db.NewOwnerContext())
        {
            owner.Classrooms.Add(new Classroom
            {
                Id = primaireClasse, SchoolId = Ecole, Name = "CI", Level = "Primaire",
                Capacity = 40, Cycle = CycleType.Primaire
            });
            owner.Students.Add(new Student
            {
                Id = primaireEleve, SchoolId = Ecole, Matricule = "ELEV-2026-0002", FullName = "Élève primaire",
                BirthDate = new DateOnly(2018, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = primaireClasse
            });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        await using var db = _db.NewAppContext(Ecole);
        var createGrade = NewCreateGradeHandler(db);

        // Maths (coeff 4) : 8. Français (coeff 2) : 6. En pondéré ce serait (8*4 + 6*2)/6 = 7,33…
        // En primaire (moyenne simple, coefficients ignorés) : (8 + 6) / 2 = 7.
        await createGrade.Handle(new CreateGradeCommand(primaireEleve, Maths, Trimestre, EvaluationType.Devoir1, 8), CancellationToken.None);
        await createGrade.Handle(new CreateGradeCommand(primaireEleve, Francais, Trimestre, EvaluationType.Devoir1, 6), CancellationToken.None);

        var summary = await new GetGradeSummaryQueryHandler(db, new CoefficientOverrideLoader(db), new SubjectFollowScope(db)).Handle(
            new GetGradeSummaryQuery(primaireEleve, Trimestre), CancellationToken.None);

        summary.Subjects.Should().OnlyContain(s => s.Coefficient == 1m, "le primaire neutralise les coefficients à 1");
        summary.GeneralAverage.Should().Be(7m, "moyenne simple (8+6)/2, jamais pondérée en primaire");
        summary.Mention.Should().BeNull("le cycle primaire n'attribue pas de mention");
    }

    [Fact]
    public async Task With_No_Grades_Entered_Yet_The_Summary_Is_Empty_And_Carries_No_Mention()
    {
        await using var db = _db.NewAppContext(Ecole);

        var summary = await new GetGradeSummaryQueryHandler(db, new CoefficientOverrideLoader(db), new SubjectFollowScope(db)).Handle(
            new GetGradeSummaryQuery(Eleve, Trimestre), CancellationToken.None);

        summary.Subjects.Should().BeEmpty();
        summary.TotalCoefficients.Should().Be(0);
        summary.GeneralAverage.Should().Be(0);
        summary.Mention.Should().BeNull("rien à qualifier tant qu'aucune matière n'est notée");
    }

    [Theory]
    [InlineData(17, "Excellent")]   // >= 16 (0.80 * 20)
    [InlineData(15, "Très Bien")]   // >= 14 (0.70 * 20)
    [InlineData(13, "Bien")]        // >= 12 (0.60 * 20)
    [InlineData(11, "Assez Bien")]  // >= 10 (0.50 * 20)
    [InlineData(9, "Passable")]     // >= 8  (0.40 * 20)
    [InlineData(5, null)]           // sous le seuil le plus bas -> aucune mention
    public async Task The_Default_Mention_Matches_The_Highest_Threshold_Reached_On_A_20_Point_Scale(
        decimal average, string? expectedMention)
    {
        await using var db = _db.NewAppContext(Ecole);
        // Une seule matière de coefficient 1 : la moyenne générale vaut alors exactement la note saisie.
        await using var seedSubject = _db.NewOwnerContext();
        var soloSubjectId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        seedSubject.Subjects.Add(new Subject { Id = soloSubjectId, SchoolId = Ecole, Name = "Solo", Level = "Primaire", Coefficient = 1 });
        await seedSubject.SaveChangesAsync(CancellationToken.None);

        await NewCreateGradeHandler(db).Handle(
            new CreateGradeCommand(Eleve, soloSubjectId, Trimestre, EvaluationType.Devoir1, average), CancellationToken.None);

        var summary = await new GetGradeSummaryQueryHandler(db, new CoefficientOverrideLoader(db), new SubjectFollowScope(db)).Handle(
            new GetGradeSummaryQuery(Eleve, Trimestre), CancellationToken.None);

        summary.GeneralAverage.Should().Be(average);
        summary.Mention.Should().Be(expectedMention);
    }

    [Fact]
    public async Task A_Custom_Mention_Overrides_The_Defaults_Entirely()
    {
        await using var db = _db.NewAppContext(Ecole);

        // Dès qu'UNE mention personnalisée existe, l'école ne reçoit plus les valeurs par défaut.
        await NewCreateMentionHandler(db).Handle(new CreateMentionCommand("Mention Maison", 5), CancellationToken.None);

        await NewCreateGradeHandler(db).Handle(
            new CreateGradeCommand(Eleve, Maths, Trimestre, EvaluationType.Devoir1, 6), CancellationToken.None);

        var summary = await new GetGradeSummaryQueryHandler(db, new CoefficientOverrideLoader(db), new SubjectFollowScope(db)).Handle(
            new GetGradeSummaryQuery(Eleve, Trimestre), CancellationToken.None);

        summary.Mention.Should().Be("Mention Maison");

        var mentions = await new GetMentionsQueryHandler(db).Handle(new GetMentionsQuery(), CancellationToken.None);
        mentions.Should().ContainSingle(m => m.Label == "Mention Maison" && m.Id != null,
            "une mention stockée porte un Id, contrairement à une mention par défaut calculée");
    }

    [Fact]
    public async Task Listing_Mentions_Without_Any_Customization_Returns_The_Five_Defaults_Scaled_To_20()
    {
        await using var db = _db.NewAppContext(Ecole);

        var mentions = await new GetMentionsQueryHandler(db).Handle(new GetMentionsQuery(), CancellationToken.None);

        mentions.Should().HaveCount(5);
        mentions.Should().OnlyContain(m => m.Id == null);
        mentions.Should().ContainSingle(m => m.Label == "Excellent" && m.MinAverage == 16);
        mentions.Should().ContainSingle(m => m.Label == "Passable" && m.MinAverage == 8);
    }

    [Fact]
    public async Task Creating_A_Mention_Above_The_Grading_Scale_Is_Rejected()
    {
        await using var db = _db.NewAppContext(Ecole);

        var act = async () => await NewCreateMentionHandler(db).Handle(
            new CreateMentionCommand("Impossible", 25), CancellationToken.None);

        await act.Should().ThrowAsync<SamaEcole.Application.Common.Exceptions.ValidationException>();
    }

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }
}
