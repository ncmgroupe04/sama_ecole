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
                Guid.NewGuid(), $"Matière {i}", Devoir1: 12 + i % 5, Devoir2: null, Composition: 10 + i % 8,
                DevoirAverage: 12 + i % 5, Average: 11 + i % 6, Coefficient: 1 + i % 4, WeightedPoints: (11 + i % 6) * (1 + i % 4)))
            .ToList();

        var ranks = subjects.ToDictionary(s => s.SubjectId, s => 1);
        var appreciations = subjects.ToDictionary(s => s.SubjectId, s => (string?)"Bien");

        return new ReportCardDto(
            SchoolName: "École de test",
            SchoolLogoUrl: null,
            InspectionAcademie: "Thies",
            InspectionEducationFormation: "Mbour 1",
            HeadingPrefix: "LYCÉE DE",
            HeadingName: "Popenguine",
            StudentFullName: "Élève de Test avec un Nom Assez Long",
            BirthDate: new DateOnly(2012, 3, 14),
            BirthPlace: "Saint-Louis",
            ClassroomName: "3e A",
            Cycle: CycleType.Lycee,
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

    /// <summary>
    /// Évolution N°2 — un bulletin d'école semestrielle (2 périodes) ou personnalisée (jusqu'à 6) garde une
    /// page A5 : le titre suit le libellé de la période (« BULLETIN DE LA 4E PÉRIODE » est le plus long) et
    /// le récapitulatif annuel compte autant de lignes que de périodes.
    /// </summary>
    [Theory]
    [InlineData("1er semestre", 2)]
    [InlineData("2e trimestre", 3)]
    [InlineData("4e période", 6)]
    public void A_Report_Card_Whatever_The_Period_Split_Fits_On_A_Single_A5_Page(string termLabel, int periodCount)
    {
        var recaps = Enumerable.Range(1, periodCount)
            .Select(i => new ReportCardTermRecap($"{i}e période", i, i == 1 ? 13.27m : null))
            .ToList();
        var reportCard = BuildReportCard(12) with { TermLabel = termLabel, TermRecaps = recaps };

        var pages = new ReportCardDocument(reportCard, logo: null)
            .GenerateImages(ImageGenerationSettings.Default).Count();

        pages.Should().Be(1);
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
        var reportCard = BuildReportCard(12) with { GradingScale = 10, Cycle = CycleType.Primaire };

        var pages = new ReportCardDocument(reportCard, logo: null)
            .GenerateImages(ImageGenerationSettings.Default).Count();

        pages.Should().Be(1, "le bulletin primaire /10 au tableau épuré ne doit jamais déborder sur une seconde page A5");
    }

    /// <summary>
    /// Classe PASSERELLE / ACCÉLÉRÉE : la mention « Cursus Accéléré Passerelle — CI → CP » s'insère sous
    /// le titre. C'est une LIGNE DE PLUS sur un gabarit A5 déjà calibré au plus juste — le cas le plus
    /// chargé (12 matières, critère du ticket JGK-G03) doit donc encore tenir sur une seule page, sans
    /// quoi l'option produirait des bulletins à deux pages pour les seules classes qui l'utilisent.
    /// </summary>
    [Fact]
    public void An_Accelerated_Twelve_Subject_Report_Card_Still_Fits_On_A_Single_A5_Page()
    {
        var reportCard = BuildReportCard(12) with
        {
            AcceleratedPathLabel = "Cursus Accéléré Passerelle — CI → CP"
        };

        var pages = new ReportCardDocument(reportCard, logo: null)
            .GenerateImages(ImageGenerationSettings.Default).Count();

        pages.Should().Be(1, "la mention du cursus accéléré ne doit pas pousser le bulletin sur une seconde page A5");
    }

    /// <summary>
    /// Symétrique du test ci-dessus : une classe ORDINAIRE n'insère rien du tout. C'est ce null qui
    /// garantit que le gabarit de la référence visuelle (AGENTS.md règle #12) reste intact pour
    /// l'immense majorité des bulletins.
    /// </summary>
    [Fact]
    public void An_Ordinary_Report_Card_Carries_No_Accelerated_Mention()
    {
        BuildReportCard(12).AcceleratedPathLabel.Should().BeNull();
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
            HeadingName = null,
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

    // PNG 1×1 transparent, valide — exerce l'incrustation réelle de la signature/du cachet sans dépendre du réseau.
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    /// <summary>
    /// La signature du Chef d'Établissement et le cachet officiel (Paramètres → Établissement,
    /// SchoolSettings) s'incrustent en pied de page sans faire déborder le bulletin, même au cas le
    /// plus chargé (12 matières).
    /// </summary>
    [Fact]
    public void A_Report_Card_With_Director_Signature_And_Official_Stamp_Fits_On_One_Page()
    {
        var pages = new ReportCardDocument(BuildReportCard(12), logo: null, directorSignature: TinyPng, officialStamp: TinyPng)
            .GenerateImages(ImageGenerationSettings.Default).Count();

        pages.Should().Be(1);
    }

    /// <summary>
    /// École qui n'a saisi qu'UN des deux (ex. le cachet mais pas encore la signature, ou l'inverse) :
    /// ne doit pas planter ni faire déborder — chaque emplacement retombe indépendamment sur son état
    /// vide d'origine.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void A_Report_Card_With_Only_One_Of_Signature_Or_Stamp_Fits_On_One_Page(bool withSignature, bool withStamp)
    {
        var pages = new ReportCardDocument(
                BuildReportCard(12), logo: null,
                directorSignature: withSignature ? TinyPng : null,
                officialStamp: withStamp ? TinyPng : null)
            .GenerateImages(ImageGenerationSettings.Default).Count();

        pages.Should().Be(1);
    }

    /// <summary>
    /// Module Coran/Franco-Arabe : titre, noms de matières, décision du conseil et mention
    /// disciplinaire gagnent chacun leur équivalent arabe SANS faire déborder le bulletin — même cas le
    /// plus chargé (12 matières) que le reste de la suite. Le contenu exact n'est pas vérifiable ici
    /// sans extraction de texte PDF (même limite que A_Report_Card_With_A_Council_Decision_Fits_On_One_Page) ;
    /// l'inspection visuelle réelle se fait via le PDF généré manuellement (voir plan de vérification).
    /// </summary>
    [Fact]
    public void A_Bilingual_Report_Card_With_Twelve_Subjects_Fits_On_One_Page()
    {
        var subjects = BuildReportCard(12).Subjects;
        var subjectNamesAr = subjects.ToDictionary(s => s.SubjectId, s => (string?)"الرياضيات");

        var reportCard = BuildReportCard(12) with
        {
            IsBilingualArabic = true,
            SubjectNamesAr = subjectNamesAr,
            DisciplinaryMention = SamaEcole.Domain.Enums.DisciplinaryMention.Felicitations,
            CouncilDecision = SamaEcole.Domain.Enums.CouncilDecision.Admitted
        };

        var pages = new ReportCardDocument(reportCard, logo: null)
            .GenerateImages(ImageGenerationSettings.Default).Count();

        pages.Should().Be(1, "le module Coran/Franco-Arabe ne doit pas faire déborder le bulletin sur une seconde page A5");
    }

    /// <summary>
    /// Une école qui active le module (IsBilingualArabic) sans avoir encore saisi de nom arabe pour ses
    /// matières (SubjectNamesAr null ou vide) obtient un bulletin sans second nom — jamais une exception,
    /// jamais une valeur inventée. Seul le titre et les libellés fixes (décision, mention) sont arabes.
    /// </summary>
    [Fact]
    public void A_Bilingual_Report_Card_Without_Any_Arabic_Subject_Name_Still_Renders()
    {
        var reportCard = BuildReportCard(8) with { IsBilingualArabic = true, SubjectNamesAr = null };

        var bytes = new ReportCardDocument(reportCard, logo: null).GeneratePdf();

        bytes.Should().NotBeEmpty();
    }

    /// <summary>
    /// Le rendu NON bilingue (SchoolSettings.IsCoranModuleEnabled à faux, l'immense majorité des écoles)
    /// ne doit strictement rien changer : IsBilingualArabic à faux (son défaut) laisse SubjectNamesAr
    /// hors-jeu même s'il est renseigné par erreur, et aucune ligne arabe ne doit apparaître — non
    /// vérifiable ici au caractère près (voir la limite déjà notée sur ComposeDecisionDuConseil), mais
    /// le document ne doit ni lever ni déborder.
    /// </summary>
    [Fact]
    public void A_Non_Bilingual_Report_Card_Ignores_Arabic_Subject_Names()
    {
        var subjects = BuildReportCard(12).Subjects;
        var subjectNamesAr = subjects.ToDictionary(s => s.SubjectId, s => (string?)"الرياضيات");

        var reportCard = BuildReportCard(12) with { SubjectNamesAr = subjectNamesAr };

        var pages = new ReportCardDocument(reportCard, logo: null)
            .GenerateImages(ImageGenerationSettings.Default).Count();

        pages.Should().Be(1);
    }
}
