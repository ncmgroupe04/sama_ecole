using FluentAssertions;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using SamaEcole.Infrastructure.Documents;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Xunit;

namespace SamaEcole.UnitTests.ReportCards;

/// <summary>
/// Ticket JGK-G03 — critère explicite du ticket : « un cas à 12 matières ne dépasse pas une page A5 ».
/// GenerateImages rend une image par page ; compter les images obtenues est le moyen le plus direct de
/// vérifier l'absence de débordement sans dépendre d'un outil de comparaison visuelle externe.
/// </summary>
public class ReportCardDocumentTests
{
    static ReportCardDocumentTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static ReportCardDto BuildReportCard(int subjectCount)
    {
        var subjects = Enumerable.Range(1, subjectCount)
            .Select(i => new SubjectGradeDto(
                Guid.NewGuid(), $"Matière {i}", Devoir: 12 + i % 5, Composition: 10 + i % 8,
                Average: 11 + i % 6, Coefficient: 1 + i % 4, WeightedPoints: (11 + i % 6) * (1 + i % 4)))
            .ToList();

        var ranks = subjects.ToDictionary(s => s.SubjectId, s => 1);
        var appreciations = subjects.ToDictionary(s => s.SubjectId, s => (string?)"Bien");

        return new ReportCardDto(
            SchoolName: "École de test",
            SchoolLogoUrl: null,
            InspectionAcademie: "Thies",
            InspectionEducationFormation: "Mbour 1",
            NomLycee: "Popenguine",
            StudentFullName: "Élève de Test avec un Nom Assez Long",
            BirthDate: new DateOnly(2012, 3, 14),
            BirthPlace: "Saint-Louis",
            ClassroomName: "3e A",
            Matricule: "ELEV-2026-0001",
            ClassSize: 42,
            IsRepeating: false,
            SchoolYearLabel: "2026-2027",
            TermLabel: "1er trimestre",
            GradingScale: 20,
            Subjects: subjects,
            TotalCoefficients: subjects.Sum(s => s.Coefficient),
            TotalPoints: subjects.Sum(s => s.WeightedPoints),
            GeneralAverage: 13.27m,
            GeneralRank: 3,
            SubjectRanks: ranks,
            SubjectAppreciations: appreciations,
            Mention: "Bien",
            Absences: 1,
            Retards: 0,
            TotalAbsences: 2,
            TermRecaps:
            [
                new ReportCardTermRecap("1er trimestre", 1, 13.27m),
                new ReportCardTermRecap("2e trimestre", 2, null),
                new ReportCardTermRecap("3e trimestre", 3, null)
            ],
            AnnualAverage: 13.27m,
            AnnualRank: 3,
            DisciplinaryMention: null,
            CouncilDecision: null,
            CouncilObservations: null);
    }

    [Fact]
    public void A_Twelve_Subject_Report_Card_Fits_On_A_Single_A5_Page()
    {
        var document = new ReportCardDocument(BuildReportCard(12), logo: null);

        var pages = document.GenerateImages(ImageGenerationSettings.Default).Count();

        pages.Should().Be(1, "le ticket JGK-G03 exige qu'un bulletin à 12 matières ne déborde jamais sur une seconde page A5");
    }

    /// <summary>
    /// Étape 3 (système hybride) : un bulletin PRIMAIRE (barème /10) emprunte le tableau épuré — sans
    /// colonnes de coefficients ni d'appréciations, sans rangée de distinctions — et ne doit pas plus
    /// déborder que la version secondaire, même au cas le plus chargé (12 matières).
    /// </summary>
    [Fact]
    public void A_Primaire_Report_Card_On_A_Ten_Point_Scale_Fits_On_A_Single_A5_Page()
    {
        var reportCard = BuildReportCard(12) with { GradingScale = 10 };

        var pages = new ReportCardDocument(reportCard, logo: null)
            .GenerateImages(ImageGenerationSettings.Default).Count();

        pages.Should().Be(1, "le bulletin primaire /10 au tableau épuré ne doit jamais déborder sur une seconde page A5");
    }

    [Fact]
    public void The_Generated_Document_Is_A_Valid_Non_Empty_Pdf()
    {
        var bytes = new ReportCardDocument(BuildReportCard(6), logo: null).GeneratePdf();

        bytes.Should().NotBeEmpty();
        // Signature standard d'un fichier PDF : les 5 premiers octets valent "%PDF-".
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

    /// <summary>
    /// Volume 1 §8.5 — suppression des décimales inutiles : une moyenne entière ou à une décimale ne
    /// doit jamais afficher de zéro de remplissage, mais l'arrondi à 2 décimales reste appliqué avant
    /// affichage (une moyenne pondérée peut porter bien plus de décimales brutes).
    /// </summary>
    [Theory]
    [InlineData(17.00, "17")]      // Entière : aucune décimale affichée.
    [InlineData(15.50, "15,5")]    // Une décimale utile conservée, virgule comme séparateur.
    [InlineData(9.5625, "9,56")]   // Arrondi correct à 2 décimales (AwayFromZero) avant affichage.
    [InlineData(0.00, "0")]        // Zéro : pas de "0,00".
    public void FormatGrade_Strips_Unnecessary_Decimals(double value, string expected)
    {
        ReportCardDocument.FormatGrade((decimal)value).Should().Be(expected);
    }

    [Theory]
    [InlineData(17.00, "17")]
    [InlineData(9.5625, "9,56")]
    public void FormatOptionalGrade_Delegates_To_FormatGrade_When_A_Value_Is_Present(double value, string expected)
    {
        ReportCardDocument.FormatOptionalGrade((decimal)value).Should().Be(expected);
    }

    /// <summary>Devoir ou Composition pas encore saisi : le placeholder "-", jamais un zéro trompeur.</summary>
    [Fact]
    public void FormatOptionalGrade_Returns_Placeholder_When_Null()
    {
        ReportCardDocument.FormatOptionalGrade(null).Should().Be("-");
    }

    /// <summary>
    /// Aucun appel fait sur la période : les cases Absences/Retards impriment "-" (null), jamais un
    /// zéro qui affirmerait à tort une assiduité parfaite. Un vrai zéro compté s'affiche, lui, "0".
    /// </summary>
    [Theory]
    [InlineData(null, "-")]
    [InlineData(0, "0")]
    [InlineData(3, "3")]
    public void FormatOptionalCount_Prints_Dash_Only_When_Attendance_Was_Never_Taken(int? value, string expected)
    {
        ReportCardDocument.FormatOptionalCount(value).Should().Be(expected);
    }

    /// <summary>
    /// La référence sépare Prénoms et Nom mais le modèle ne porte qu'un FullName : le dernier mot est
    /// affiché comme nom de famille, le reste comme prénoms (usage sénégalais). Heuristique
    /// d'affichage uniquement — rien n'est modifié en base.
    /// </summary>
    [Theory]
    [InlineData("Mame Diarra Bousso Faye", "Mame Diarra Bousso", "Faye")]
    [InlineData("Awa Diallo", "Awa", "Diallo")]
    [InlineData("Awa", "Awa", "")]
    [InlineData("  Awa   Diallo  ", "Awa", "Diallo")]
    public void SplitFullName_Displays_The_Last_Word_As_Family_Name(string fullName, string prenoms, string nom)
    {
        ReportCardDocument.SplitFullName(fullName).Should().Be((prenoms, nom));
    }

    /// <summary>
    /// Une école qui n'a pas encore renseigné son en-tête administratif (IA/IEF/LYCEE DE null) doit
    /// quand même obtenir son bulletin — lignes vides, jamais une exception ni une valeur inventée.
    /// </summary>
    [Fact]
    public void A_Report_Card_Without_Academic_Header_Renders_On_One_Page()
    {
        var reportCard = BuildReportCard(8) with
        {
            InspectionAcademie = null,
            InspectionEducationFormation = null,
            NomLycee = null,
            BirthPlace = null,
            Absences = null,
            Retards = null,
            TotalAbsences = null
        };

        var pages = new ReportCardDocument(reportCard, logo: null)
            .GenerateImages(ImageGenerationSettings.Default).Count();

        pages.Should().Be(1);
    }

    /// <summary>
    /// Une distinction cochée (Blâme… Félicitations) et des observations du conseil au texte maximal
    /// (300 caractères, la borne du validator — voir ReportCardRemarkConfiguration) ne doivent jamais
    /// faire déborder le bulletin sur une seconde page A5, même combinées au cas le plus chargé (12
    /// matières, déjà éprouvé par ailleurs).
    /// </summary>
    [Fact]
    public void A_Report_Card_With_A_Checked_Mention_And_Long_Observations_Fits_On_One_Page()
    {
        var reportCard = BuildReportCard(12) with
        {
            DisciplinaryMention = SamaEcole.Domain.Enums.DisciplinaryMention.Felicitations,
            CouncilObservations = string.Concat(Enumerable.Repeat("Très bon trimestre, continuez ainsi. ", 9))[..300]
        };

        var pages = new ReportCardDocument(reportCard, logo: null)
            .GenerateImages(ImageGenerationSettings.Default).Count();

        pages.Should().Be(1);
    }

    /// <summary>
    /// Bloc « Décision du Conseil » : une décision cochée (ex. Admitted) ne doit pas faire déborder le
    /// bulletin, même combinée au cas le plus chargé (12 matières). Le contenu exact des trois cases
    /// (une seule cochée) n'est pas vérifiable ici sans extraction de texte PDF — ComposeDecisionDuConseil
    /// est un simple aiguillage sur reportCard.CouncilDecision, couvert visuellement à la revue.
    /// </summary>
    [Fact]
    public void A_Report_Card_With_A_Council_Decision_Fits_On_One_Page()
    {
        var reportCard = BuildReportCard(12) with
        {
            CouncilDecision = SamaEcole.Domain.Enums.CouncilDecision.Admitted
        };

        var pages = new ReportCardDocument(reportCard, logo: null)
            .GenerateImages(ImageGenerationSettings.Default).Count();

        pages.Should().Be(1);
    }
}
