using FluentAssertions;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Application.ReportCards;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Documents;
using SamaEcole.UnitTests.Boarding;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Xunit;

namespace SamaEcole.UnitTests.ReportCards;

/// <summary>
/// Bulletin franco-arabe à deux sections (A4 paysage, ReportCardDocument.Sections.cs). Trois garanties :
///   • il TIENT sur une page, y compris chargé (noms arabes, matière dispensée, décision du conseil) ;
///   • son texte est EXACT — libellés des deux sections, moyennes, rang, arabe réellement rendu dans la police
///     arabe embarquée (pas de carré « tofu », pas de glyphes de repli) ;
///   • RIEN ne se chevauche, et tout bulletin SANS section garde sa mise en page A5 d'origine.
/// </summary>
public class SectionedReportCardDocumentTests
{
    static SectionedReportCardDocumentTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        PdfFonts.EnsureRegistered();
    }

    private static readonly string[] FrenchNames = ["Mathématiques", "Français", "Histoire-Géographie", "SVT", "EPS", "Anglais", "Physique-Chimie", "Informatique"];
    private static readonly (string Name, string NameAr)[] ArabicNames =
    [
        ("Langue arabe", "اللغة العربية"), ("Fiqh", "الفقه"), ("Hadith", "الحديث"), ("Tajwid", "التجويد"),
        ("Sirah", "السيرة النبوية"), ("Aqidah", "العقيدة"), ("Tarbiya", "التربية الإسلامية"), ("Tafsir", "التفسير")
    ];

    /// <summary>Un bulletin à <paramref name="perSection"/> matières françaises ET autant d'arabes.</summary>
    private static ReportCardDto BuildSectioned(int perSection, bool withExempt = false)
    {
        var french = Enumerable.Range(0, perSection)
            .Select(i => Subject(FrenchNames[i % FrenchNames.Length], 11 + i % 6, 1 + i % 3)).ToList();
        var arabic = Enumerable.Range(0, perSection)
            .Select(i => Subject(ArabicNames[i % ArabicNames.Length].Name, 9 + i % 8, 1 + i % 3)).ToList();
        var subjects = french.Concat(arabic).ToList();

        var sectionTypes = french.ToDictionary(s => s.SubjectId, _ => SectionType.French)
            .Concat(arabic.Select((s, i) => KeyValuePair.Create(s.SubjectId, i % 2 == 0 ? SectionType.Arabic : SectionType.IslamicStudies)))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var namesAr = arabic
            .Select((s, i) => KeyValuePair.Create(s.SubjectId, (string?)ArabicNames[i % ArabicNames.Length].NameAr))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        List<ExemptSubjectDto>? exempt = null;
        if (withExempt)
        {
            var dispensee = new ExemptSubjectDto(Guid.NewGuid(), "Éducation physique", 1);
            exempt = [dispensee];
            sectionTypes[dispensee.SubjectId] = SectionType.Common;
        }

        var breakdown = SectionAverageCalculator.Compute(subjects, sectionTypes);

        return new ReportCardDto(
            SchoolName: "Collège franco-arabe de test",
            SchoolLogoUrl: null,
            InspectionAcademie: "Thiès",
            InspectionEducationFormation: "Mbour 1",
            HeadingPrefix: "COLLÈGE DE",
            HeadingName: "Popenguine",
            StudentFullName: "Mame Diarra Bousso Faye",
            BirthDate: new DateOnly(2012, 3, 14),
            BirthPlace: "Saint-Louis",
            ClassroomName: "4ème A",
            Cycle: CycleType.College,
            Matricule: "ELEV-2026-0042",
            ClassSize: 38,
            IsRepeating: false,
            SchoolYearLabel: "2026-2027",
            TermLabel: "1er trimestre",
            GradingScale: 20,
            Subjects: subjects,
            TotalCoefficients: breakdown.TotalCoefficients,
            TotalPoints: breakdown.TotalPoints,
            GeneralAverage: Math.Round(breakdown.CombinedAverage!.Value, 2),
            GeneralRank: 3,
            SubjectRanks: subjects.ToDictionary(s => s.SubjectId, _ => 2),
            SubjectAppreciations: subjects.ToDictionary(s => s.SubjectId, s => (string?)(s.Average >= 14 ? "Bon Travail" : s.Average >= 12 ? "Assez Bien" : "Moyen")),
            Mention: "Assez Bien",
            Absences: 1,
            Retards: 0,
            TermRecaps:
            [
                new ReportCardTermRecap("1er trimestre", 1, 12.5m),
                new ReportCardTermRecap("2e trimestre", 2, null),
                new ReportCardTermRecap("3e trimestre", 3, null)
            ],
            AnnualAverage: 12.5m,
            AnnualRank: 3,
            DisciplinaryMention: DisciplinaryMention.Encouragements,
            CouncilDecision: CouncilDecision.Admitted,
            CouncilObservations: "Élève sérieuse, à encourager.",
            TotalAbsences: 2,
            IsBilingualArabic: true,
            SubjectNamesAr: namesAr,
            ExemptSubjects: exempt,
            Sections: new BilingualSectionsDto(
                sectionTypes,
                new SectionResultDto(breakdown.French.TotalCoefficients, breakdown.French.TotalPoints, breakdown.French.Average, 2),
                new SectionResultDto(breakdown.Arabic.TotalCoefficients, breakdown.Arabic.TotalPoints, breakdown.Arabic.Average, 5)));

        static SubjectGradeDto Subject(string name, decimal average, decimal coefficient) =>
            new(Guid.NewGuid(), name, average, null, average, average, average, coefficient, average * coefficient);
    }

    private static byte[] Pdf(ReportCardDto card, byte[]? logo = null) =>
        new ReportCardDocument(card, logo, directorSignature: null, officialStamp: null).GeneratePdf();

    private static int Pages(ReportCardDto card) =>
        new ReportCardDocument(card, logo: null).GenerateImages(ImageGenerationSettings.Default).Count();

    // ---------------------------------------------------------------- Tient sur une page

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    public void A_Sectioned_Report_Card_Fits_On_One_Page(int perSection)
    {
        Pages(BuildSectioned(perSection)).Should().Be(1,
            $"{perSection} matières françaises + {perSection} arabes ne doivent pas déborder du A4 paysage");
    }

    [Fact]
    public void A_Sectioned_Report_Card_With_An_Exempt_Subject_Still_Fits_On_One_Page()
    {
        Pages(BuildSectioned(7, withExempt: true)).Should().Be(1);
    }

    [Fact]
    public void A_Sectioned_Report_Card_Renders_With_A_School_Logo()
    {
        // PNG 1×1 : un logo illisible ne doit de toute façon jamais empêcher l'émission (PdfRenderGuard) ; ici,
        // un logo valide s'insère dans le bandeau sans casser la page.
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

        var bytes = Pdf(BuildSectioned(6), png);

        bytes.Should().NotBeEmpty();
        using var document = PdfDocument.Open(bytes);
        document.NumberOfPages.Should().Be(1);
    }

    // ---------------------------------------------------------------- Contenu

    [Fact]
    public void The_Sectioned_Page_Is_Landscape()
    {
        using var document = PdfDocument.Open(Pdf(BuildSectioned(6)));

        var page = document.GetPage(1);
        page.Width.Should().BeGreaterThan(page.Height, "deux sections côte à côte : A4 paysage");
    }

    [Fact]
    public void The_Page_Prints_Both_Sections_The_Official_Header_And_The_Combined_Average()
    {
        var card = BuildSectioned(6);

        var text = PdfText.Extract(Pdf(card));

        text.Should().Contain("SECTION FRANÇAISE");
        text.Should().Contain("SECTION ARABE");
        text.Should().Contain("RÉPUBLIQUE DU SÉNÉGAL");
        text.Should().Contain("MOYENNE GÉNÉRALE");
        text.Should().Contain("Moyenne section française");
        text.Should().Contain("Moyenne section arabe");
        text.Should().Contain("Ministère de l'Éducation nationale");
        text.Should().Contain("Le Chef d'Établissement");
    }

    [Fact]
    public void Each_Section_Prints_Its_Own_Average_And_Rank_And_The_Combined_Average_Is_The_Weighted_One()
    {
        var card = BuildSectioned(6);
        var sections = card.Sections!;

        var text = PdfText.Extract(Pdf(card));

        FormatGrade(sections.French.Average!.Value).Should().NotBeNullOrEmpty();
        text.Should().Contain($"{FormatGrade(sections.French.Average!.Value)} /20 — Rang 2");
        text.Should().Contain($"{FormatGrade(sections.Arabic.Average!.Value)} /20 — Rang 5");
        text.Should().Contain($"{FormatGrade(card.GeneralAverage)} /20 — Rang 3");

        // La moyenne générale imprimée est bien la pondérée des deux sections.
        var combined = (sections.French.TotalPoints + sections.Arabic.TotalPoints)
                       / (sections.French.TotalCoefficients + sections.Arabic.TotalCoefficients);
        Math.Round(combined, 2).Should().Be(card.GeneralAverage);
    }

    [Fact]
    public void Subjects_Land_In_Their_Own_Section_Table()
    {
        var card = BuildSectioned(4);

        using var document = PdfDocument.Open(Pdf(card));
        var page = document.GetPage(1);
        var middle = page.Width / 2;

        // Les matières françaises sont dans la moitié gauche, les arabes dans la moitié droite.
        var words = page.GetWords().ToList();
        words.Single(w => w.Text == "Mathématiques").BoundingBox.Centroid.X.Should().BeLessThan(middle);
        words.First(w => w.Text == "Fiqh").BoundingBox.Centroid.X.Should().BeGreaterThan(middle);
    }

    [Fact]
    public void Arabic_Is_Shaped_Into_Joined_Letters()
    {
        using var document = PdfDocument.Open(Pdf(BuildSectioned(6)));
        var letters = document.GetPage(1).Letters;

        var arabicLetters = letters.Where(l => l.Value.Length > 0 && IsArabic(l.Value[0])).ToList();

        arabicLetters.Should().NotBeEmpty("le bandeau et la section arabe contiennent de l'arabe");

        // Une lettre arabe LIÉE s'extrait en forme de présentation (U+FB50 et au-delà : initiale, médiane, finale).
        // Si la mise en forme avait échoué, on n'aurait que des lettres isolées de base (U+0600-06FF) : des mots
        // épelés lettre à lettre, sans ligature.
        arabicLetters.Count(l => l.Value[0] >= (char)0xFB50).Should().BeGreaterThan(arabicLetters.Count / 2,
            "l'arabe doit être mis en forme (lettres liées), pas épelé");

        // Le texte LATIN, lui, s'extrait proprement : aucune ligature « fi » / « ti » cassée (voir PdfLigatureTests).
        // (Des glyphes arabes Type3 sans équivalent Unicode ressortent « \0 » à l'extraction : limite du
        // moteur d'extraction, le rendu visuel étant correct — contrôlé à l'œil sur le PDF généré.)
        PdfText.Extract(Pdf(BuildSectioned(6))).Should().Contain("Mathématiques").And.Contain("Histoire-Géographie");
    }

    [Fact]
    public void The_Official_Arabic_Header_Is_Printed_On_The_Right_Opposite_The_French_One()
    {
        using var document = PdfDocument.Open(Pdf(BuildSectioned(6)));
        var page = document.GetPage(1);

        // Bandeau = le haut de la page. Le français (République du Sénégal) est à gauche, son équivalent arabe à
        // droite : l'extraction de mots arabes en Type3 n'est pas fiable lettre à lettre, mais la présence d'une
        // ligne arabe dans le coin haut-droit l'est.
        var top = page.Height - 60;
        var arabicInBanner = page.Letters.Where(l => l.GlyphRectangle.Bottom > top && l.Value.Length > 0 && IsArabic(l.Value[0])).ToList();

        arabicInBanner.Should().HaveCountGreaterThan(30, "جمهورية السنغال / وزارة التربية الوطنية");
        arabicInBanner.Should().OnlyContain(l => l.GlyphRectangle.Left > page.Width / 2, "le bloc arabe est ancré à droite");

        var french = page.GetWords().Single(w => w.Text == "RÉPUBLIQUE");
        french.BoundingBox.Left.Should().BeLessThan(page.Width / 4);
        french.BoundingBox.Bottom.Should().BeGreaterThan(top);
    }

    // ---------------------------------------------------------------- Aucun chevauchement

    [Theory]
    [InlineData(4, false)]
    [InlineData(8, false)]
    [InlineData(7, true)]
    public void No_Text_Overlaps_Another_Text(int perSection, bool withExempt)
    {
        using var document = PdfDocument.Open(Pdf(BuildSectioned(perSection, withExempt)));

        // Le filigrane (corps 56) passe volontairement SOUS le contenu : seuls les mots de corps courant comptent.
        var words = document.GetPage(1).GetWords()
            .Where(w => w.Letters.Max(l => l.PointSize) < 20)
            .ToList();

        var overlaps = new List<string>();
        for (var i = 0; i < words.Count; i++)
        {
            for (var j = i + 1; j < words.Count; j++)
            {
                if (OverlapRatio(words[i], words[j]) > 0.3)
                {
                    overlaps.Add($"« {words[i].Text} » × « {words[j].Text} »");
                }
            }
        }

        overlaps.Should().BeEmpty("aucun mot ne doit en recouvrir un autre");
    }

    // ---------------------------------------------------------------- Gabarit d'origine préservé

    [Fact]
    public void A_Report_Card_Without_Sections_Keeps_The_Original_A5_Portrait_Layout()
    {
        var card = BuildSectioned(3) with { Sections = null };

        using var document = PdfDocument.Open(Pdf(card));
        var page = document.GetPage(1);

        page.Height.Should().BeGreaterThan(page.Width, "sans section arabe, le bulletin reste l'A5 portrait de la référence");
        document.NumberOfPages.Should().Be(1);
    }

    [Fact]
    public void A_Report_Card_Whose_School_Has_Not_Enabled_The_Module_Stays_A5_Even_If_Sections_Were_Passed()
    {
        var card = BuildSectioned(3) with { IsBilingualArabic = false };

        using var document = PdfDocument.Open(Pdf(card));

        document.GetPage(1).Height.Should().BeGreaterThan(document.GetPage(1).Width);
        PdfText.Extract(Pdf(card)).Should().NotContain("SECTION ARABE");
    }

    // ---------------------------------------------------------------- PDF fusionné de la classe

    [Fact]
    public void A_Class_Pdf_Mixes_Landscape_Sectioned_Cards_And_Portrait_Ordinary_Ones()
    {
        var sectioned = BuildSectioned(5);
        var ordinary = BuildSectioned(3) with { Sections = null, IsBilingualArabic = false };

        var pdf = new ClassBulletinsDocument([sectioned, ordinary, sectioned], logo: null).GeneratePdf();

        using var document = PdfDocument.Open(pdf);
        document.NumberOfPages.Should().Be(3, "une page par élève, quel que soit son format");
        document.GetPage(1).Width.Should().BeGreaterThan(document.GetPage(1).Height);
        document.GetPage(2).Height.Should().BeGreaterThan(document.GetPage(2).Width);
        document.GetPage(3).Width.Should().BeGreaterThan(document.GetPage(3).Height);
    }

    // ---------------------------------------------------------------- Observations du conseil en arabe

    [Fact]
    public void A_Council_Observation_Written_In_Arabic_Is_Rendered_In_The_Arabic_Font_On_The_Ordinary_Layout()
    {
        var card = BuildSectioned(3) with { Sections = null, CouncilObservations = "تلميذة مجتهدة ونشيطة" };

        using var document = PdfDocument.Open(Pdf(card));
        var arabic = document.GetPage(1).Letters.Where(l => IsArabic(l.Value[0])).ToList();

        arabic.Should().NotBeEmpty();
        arabic.Count(l => l.Value[0] >= (char)0xFB50).Should().BeGreaterThan(arabic.Count / 2, "lettres liées, pas des carrés");
        document.NumberOfPages.Should().Be(1);
    }

    [Theory]
    [InlineData("Élève sérieuse", false)]
    [InlineData("تلميذة مجتهدة", true)]
    [InlineData("Excellent — ممتاز", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ContainsArabic_Detects_Arabic_Letters_Only(string? text, bool expected)
    {
        Bilingual.ContainsArabic(text).Should().Be(expected);
    }

    // ---------------------------------------------------------------- Aides

    private static string FormatGrade(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');

    private static bool IsArabic(char c) =>
        c is >= (char)0x0600 and <= (char)0x06FF or >= (char)0x0750 and <= (char)0x077F
            or >= (char)0xFB50 and <= (char)0xFDFF or >= (char)0xFE70 and <= (char)0xFEFF;

    private static double OverlapRatio(Word a, Word b)
    {
        var x = Math.Min(a.BoundingBox.Right, b.BoundingBox.Right) - Math.Max(a.BoundingBox.Left, b.BoundingBox.Left);
        var y = Math.Min(a.BoundingBox.Top, b.BoundingBox.Top) - Math.Max(a.BoundingBox.Bottom, b.BoundingBox.Bottom);
        if (x <= 0 || y <= 0)
        {
            return 0;
        }

        var smaller = Math.Min(a.BoundingBox.Area, b.BoundingBox.Area);
        return smaller <= 0 ? 0 : x * y / smaller;
    }
}
