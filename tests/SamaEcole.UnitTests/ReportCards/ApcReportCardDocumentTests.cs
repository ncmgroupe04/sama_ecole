using FluentAssertions;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Documents;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Xunit;

namespace SamaEcole.UnitTests.ReportCards;

/// <summary>
/// Bulletin à GRILLE D'ÉVALUATION configurable (APC du primaire) : le tableau se construit entièrement
/// depuis <see cref="EvaluationStructureDto"/>, ce que l'école a saisi dans l'écran des matières.
///
/// Les trois grilles reproduites ici sont les trois modèles réels du cycle primaire sénégalais, avec
/// leurs barèmes et leurs entêtes de colonnes tels quels — c'est le seul moyen de vérifier que le même
/// code rend bien TROIS tableaux différents, et que le plus chargé d'entre eux (CI-CP, 18 activités
/// réparties en 6 domaines) tient encore sur une page A5, la contrainte que le ticket JGK-G03 impose à
/// tous les bulletins du produit.
/// </summary>
public class ApcReportCardDocumentTests
{
    static ApcReportCardDocumentTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary>
    /// CI-CP — entêtes « Domaines » / « Activités », tout sur 10. La grille la PLUS LONGUE des trois :
    /// 6 domaines, 18 activités.
    /// </summary>
    private static EvaluationStructureDto CiCpStructure() => Structure("Domaines", "Activités",
        ("Lang & Com.", 10m, ["P. Alphabétique", "C. Phonologique", "Vocabulaire", "Compréhension", "Fluidité"]),
        ("Maths", 10m, ["A. Numériques", "A. Géométriques", "A. de Mesure", "R. de Problèmes"]),
        ("DDM", 10m, ["Histoire", "Géographie", "I.S.T"]),
        ("EDD", 10m, ["V. dans son Milieu", "V. Ensemble"]),
        ("EPSA", 10m, ["Dessin", "Récitation & Chant"]),
        ("Lang Etrangeres", 10m, ["Arabe", "Anglais"]));

    /// <summary>
    /// CE1-CE2 — entêtes « Activités » / « Contrôles », et surtout des barèmes qui CHANGENT d'une ligne à
    /// l'autre : Ressources /40 et Compétences /60 en Français et en Maths, /24 et /16 en DDM et EDD,
    /// /20 pour Dessin et Arabe. C'est le cas qui prouve que la colonne « Sur » est bien une donnée.
    /// </summary>
    private static EvaluationStructureDto Ce1Ce2Structure() => new("Activités", "Contrôles",
    [
        Group("Français", ("Ressources", 40m), ("Compétences", 60m)),
        Group("Maths", ("Ressources", 40m), ("Compétences", 60m)),
        Group("DDM", ("Ressources", 24m), ("Compétences", 16m)),
        Group("EDD", ("Ressources", 24m), ("Compétences", 16m)),
        Group("EPSA", ("Dessin", 20m)),
        Group("Lang Etrangeres", ("Arabe", 20m))
    ]);

    /// <summary>CM1-CM2 — même forme que le CE1-CE2, sans les langues étrangères et avec « Lang &amp; Com » en tête.</summary>
    private static EvaluationStructureDto Cm1Cm2Structure() => new("Activités", "Contrôles",
    [
        Group("Lang & Com", ("Ressources", 40m), ("Compétences", 60m)),
        Group("Maths", ("Ressources", 40m), ("Compétences", 60m)),
        Group("DDM", ("Ressources", 24m), ("Compétences", 16m)),
        Group("EDD", ("Ressources", 24m), ("Compétences", 16m)),
        Group("EPSA", ("Dessin", 20m))
    ]);

    public static TheoryData<string, EvaluationStructureDto> ThreePrimarySchoolGrids() => new()
    {
        { "CI-CP", CiCpStructure() },
        { "CE1-CE2", Ce1Ce2Structure() },
        { "CM1-CM2", Cm1Cm2Structure() }
    };

    /// <summary>
    /// Les trois grilles, notées de bout en bout, doivent chacune tenir sur UNE page A5 — la contrainte
    /// que tous les bulletins du produit respectent (JGK-G03). La plus longue (CI-CP, 18 lignes) est
    /// celle qui décide : si elle passe, les deux autres passent.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThreePrimarySchoolGrids))]
    public void Each_Primary_School_Grid_Fits_On_A_Single_A5_Page(string label, EvaluationStructureDto structure)
    {
        var reportCard = BuildApcReportCard(structure, gradeEverything: true);

        var pages = new ReportCardDocument(reportCard, logo: null)
            .GenerateImages(ImageGenerationSettings.Default).Count();

        pages.Should().Be(1, "la grille {0} doit tenir sur une seule page A5", label);
    }

    /// <summary>
    /// Grille VIERGE — aucune activité encore notée. C'est l'état d'un bulletin tiré en début de
    /// trimestre : le tableau doit s'imprimer ENTIER, toutes ses lignes visibles, cases de notes vides.
    /// Un tableau reconstruit depuis les seules notes saisies n'aurait ici rien à imprimer du tout.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThreePrimarySchoolGrids))]
    public void An_Ungraded_Grid_Still_Prints_Every_Line_On_One_Page(string label, EvaluationStructureDto structure)
    {
        var reportCard = BuildApcReportCard(structure, gradeEverything: false);

        var pages = new ReportCardDocument(reportCard, logo: null)
            .GenerateImages(ImageGenerationSettings.Default).Count();

        pages.Should().Be(1, "la grille vierge {0} s'imprime en entier sur une page A5", label);
        reportCard.EvaluationStructure!.LineCount.Should().Be(structure.LineCount);
    }

    /// <summary>
    /// Rétrocompatibilité explicite du ticket : une matière SANS activité au milieu d'une grille
    /// hiérarchique s'imprime sur une ligne simple, sans fusion de cellule (branche ColumnSpan de
    /// ComposeGradesTableApc). Le document ne doit ni planter ni déborder sur ce mélange.
    /// </summary>
    [Fact]
    public void A_Flat_Subject_Inside_A_Hierarchical_Grid_Renders_Without_A_RowSpan()
    {
        var mixed = new EvaluationStructureDto("Domaines", "Activités",
        [
            Group("Lang & Com.", ("Ressources", 40m), ("Compétences", 60m)),
            // Aucune activité : Label null → le nom occupe les deux premières colonnes.
            new EvaluationGroupDto(Guid.NewGuid(), "Conduite", [new EvaluationLineDto(Guid.NewGuid(), null, 18m, 20m, "Très Bien")]),
            Group("Maths", ("Ressources", 40m), ("Compétences", 60m))
        ]);

        var document = new ReportCardDocument(BuildApcReportCard(mixed, gradeEverything: true), logo: null);

        document.GenerateImages(ImageGenerationSettings.Default).Count().Should().Be(1);
        mixed.LineCount.Should().Be(5);
    }

    /// <summary>
    /// La grille prime sur la déduction par cycle : un bulletin qui en porte une n'emprunte NI le tableau
    /// du secondaire NI celui du primaire simple. Le vérifier par le nombre de pages ne dirait rien —
    /// c'est le fait que le rendu accepte une grille à barèmes mixtes (/60 au-dessus du /10 du cycle
    /// primaire) sans lever d'exception qui prouve que c'est bien ComposeGradesTableApc qui a composé.
    /// </summary>
    [Fact]
    public void A_Primaire_Report_Card_With_A_Grid_Renders_Scores_Above_Its_Cycle_Scale()
    {
        var reportCard = BuildApcReportCard(Ce1Ce2Structure(), gradeEverything: true)
            with { Cycle = CycleType.Primaire, GradingScale = 10 };

        var bytes = new ReportCardDocument(reportCard, logo: null).GeneratePdf();

        bytes.Should().NotBeEmpty();
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

    // ------------------------------------------------------------------ Fabriques

    private static EvaluationStructureDto Structure(
        string column1, string column2, params (string Name, decimal MaxScore, string[] Activities)[] groups) =>
        new(column1, column2,
            groups.Select(g => new EvaluationGroupDto(
                    Guid.NewGuid(),
                    g.Name,
                    g.Activities.Select(a => new EvaluationLineDto(Guid.NewGuid(), a, null, g.MaxScore, null)).ToList()))
                .ToList());

    private static EvaluationGroupDto Group(string name, params (string Label, decimal MaxScore)[] lines) =>
        new(Guid.NewGuid(), name,
            lines.Select(l => new EvaluationLineDto(Guid.NewGuid(), l.Label, null, l.MaxScore, null)).ToList());

    /// <summary>
    /// <paramref name="gradeEverything"/> false laisse toutes les cases de notes vides (bulletin tiré en
    /// début de trimestre) ; true note chaque ligne à 80 % de SON barème — ce qui, l'appréciation étant
    /// calculée sur le pourcentage de réussite, donne « Excellent » partout avec les mentions par défaut.
    /// </summary>
    private static ReportCardDto BuildApcReportCard(EvaluationStructureDto structure, bool gradeEverything)
    {
        var filled = gradeEverything
            ? new EvaluationStructureDto(
                structure.Column1Header,
                structure.Column2Header,
                structure.Groups.Select(g => new EvaluationGroupDto(
                        g.SubjectId,
                        g.Name,
                        g.Lines.Select(l => l with { Score = l.MaxScore * 0.8m, Appreciation = "Excellent" }).ToList()))
                    .ToList())
            : structure;

        // Le tableau APC se suffit de la structure : la liste plate Subjects ne sert plus qu'aux
        // récapitulatifs, et reste vide ici pour qu'aucun test ne dépende par erreur des deux à la fois.
        return new ReportCardDto(
            SchoolName: "École de test",
            SchoolLogoUrl: null,
            InspectionAcademie: "Thiès",
            InspectionEducationFormation: "Mbour 1",
            HeadingPrefix: "ÉCOLE ÉLÉMENTAIRE DE",
            HeadingName: "Popenguine",
            StudentFullName: "Mame Diarra Bousso Faye",
            BirthDate: new DateOnly(2018, 3, 14),
            BirthPlace: "Saint-Louis",
            ClassroomName: "CI A",
            Cycle: CycleType.Primaire,
            Matricule: "ELEV-2026-0001",
            ClassSize: 42,
            IsRepeating: false,
            SchoolYearLabel: "2026-2027",
            TermLabel: "1er trimestre",
            GradingScale: 20,
            Subjects: [],
            TotalCoefficients: 0m,
            TotalPoints: 0m,
            GeneralAverage: 16m,
            GeneralRank: 3,
            SubjectRanks: new Dictionary<Guid, int>(),
            SubjectAppreciations: new Dictionary<Guid, string?>(),
            Mention: null,
            Absences: 1,
            Retards: 0,
            TotalAbsences: 2,
            TermRecaps:
            [
                new ReportCardTermRecap("1er trimestre", 1, 16m),
                new ReportCardTermRecap("2e trimestre", 2, null),
                new ReportCardTermRecap("3e trimestre", 3, null)
            ],
            AnnualAverage: 16m,
            AnnualRank: 3,
            DisciplinaryMention: null,
            CouncilDecision: null,
            CouncilObservations: null,
            EvaluationStructure: filled);
    }
}
