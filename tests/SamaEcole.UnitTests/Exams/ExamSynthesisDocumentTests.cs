using FluentAssertions;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SamaEcole.Application.Exams;
using SamaEcole.Application.Exams.Common;
using SamaEcole.Application.ReportCards;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Documents;
using SamaEcole.UnitTests.Boarding;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Xunit;

namespace SamaEcole.UnitTests.Exams;

/// <summary>
/// PV de session et fiche synthétique d'examen (module Franco-Arabe, bloc 4) : moyennes annuelles par section,
/// contenu imprimé, pagination, absence de chevauchement, et comportement hors module (français seul).
/// </summary>
public class ExamSynthesisDocumentTests
{
    static ExamSynthesisDocumentTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        PdfFonts.EnsureRegistered();
    }

    // ---------------------------------------------------------------- Moyennes annuelles par section

    private static SectionBreakdown Term(decimal? frenchPoints, decimal frenchCoef, decimal? arabicPoints, decimal arabicCoef) =>
        new(new SectionTotals(frenchCoef, frenchPoints ?? 0), new SectionTotals(arabicCoef, arabicPoints ?? 0));

    [Fact]
    public void The_Annual_Average_Of_A_Section_Is_The_Mean_Of_Its_Term_Averages()
    {
        var result = ExamAnnualAverages.Compute([
            Term(48, 4, 56, 5),    // français 12, arabe 11,2
            Term(56, 4, 58, 5)]);  // français 14, arabe 11,6

        result.French.Should().Be(13m);
        result.Arabic.Should().Be(11.4m);
    }

    [Fact]
    public void A_Term_Without_Grades_In_A_Section_Does_Not_Pull_Its_Average_Down()
    {
        // Période 2 : aucune note arabe. La moyenne arabe de l'année reste celle de la période 1, pas la moitié.
        var result = ExamAnnualAverages.Compute([
            Term(48, 4, 56, 5),
            Term(56, 4, 0, 0)]);

        result.Arabic.Should().Be(11.2m);
        result.French.Should().Be(13m);
    }

    [Fact]
    public void A_Section_Never_Graded_Has_No_Annual_Average()
    {
        var result = ExamAnnualAverages.Compute([Term(48, 4, 0, 0), Term(56, 4, 0, 0)]);

        result.Arabic.Should().BeNull();
        result.Combined.Should().NotBeNull();
    }

    [Fact]
    public void No_Term_At_All_Gives_No_Average()
    {
        var result = ExamAnnualAverages.Compute([]);

        result.French.Should().BeNull();
        result.Arabic.Should().BeNull();
        result.Combined.Should().BeNull();
    }

    [Fact]
    public void The_Combined_Annual_Average_Is_The_Mean_Of_The_General_Averages_Like_The_Report_Card()
    {
        var result = ExamAnnualAverages.Compute([
            Term(48, 4, 56, 5),    // (48 + 56) / 9
            Term(56, 4, 58, 5)]);  // (56 + 58) / 9

        result.Combined.Should().BeApproximately(((48m + 56m) / 9m + (56m + 58m) / 9m) / 2m, 0.0001m);
    }

    // ---------------------------------------------------------------- Documents

    private static ExamSynthesisCandidate Candidate(
        int i, bool bilingual = true, bool withResult = true, bool withIen = true, ExamMention? mention = ExamMention.Bien) => new(
        Reference: $"BFEM-{i:000}",
        StudentFullName: $"Mame Diarra Bousso Faye {i}",
        StudentFullNameAr: bilingual ? "مامي ديارا بوسو فاي" : null,
        Matricule: $"ELEV-2026-{i:0000}",
        IenNumber: withIen ? $"IEN-{i:0000000}" : null,
        BirthDate: new DateOnly(2011, 3, 14),
        BirthPlace: "Saint-Louis",
        Gender: "F",
        ClassroomName: "3ème A",
        CandidateNumber: $"BFEM-{i:000}",
        TableNumber: null,
        ExamCenterName: "Collège de Mbour",
        ExamCenterCode: null,
        DossierStatus: ExamDossierStatus.Transmis,
        GradingScale: 20,
        FrenchAverage: bilingual ? 13.25m : null,
        ArabicAverage: bilingual ? 11.4m : null,
        CombinedAverage: 12.6m,
        IsAdmitted: withResult ? true : null,
        Mention: withResult ? mention : null,
        ExamAverage: withResult ? 12.75m : null,
        DeliberatedOn: withResult ? new DateOnly(2027, 7, 10) : null);

    private static ExamSynthesisModel Model(
        int candidates, bool bilingual = true, ExamType type = ExamType.BFEM, string? series = null,
        bool withResult = true, bool withIen = true, ExamMention? mention = ExamMention.Bien) => new(
        Reference: "BFEM-2026-2027",
        SchoolName: "Collège franco-arabe de test",
        InspectionAcademie: "Thiès",
        InspectionEducationFormation: "Mbour 1",
        ExamType: type,
        Series: series,
        SchoolYearLabel: "2026-2027",
        IsBilingualArabic: bilingual,
        Candidates: Enumerable.Range(1, candidates).Select(i => Candidate(i, bilingual, withResult, withIen, mention)).ToList());

    private static byte[] Pv(ExamSynthesisModel model, byte[]? logo = null) => new ExamSynthesisPdfGenerator().GenerateSessionPv(model, logo);

    private static byte[] Sheet(ExamSynthesisModel model) => new ExamSynthesisPdfGenerator().GenerateCandidateSheet(model, null);

    private static bool IsArabic(char c) =>
        c is >= (char)0x0600 and <= (char)0x06FF or >= (char)0x0750 and <= (char)0x077F
            or >= (char)0xFB50 and <= (char)0xFDFF or >= (char)0xFE70 and <= (char)0xFEFF;

    [Fact]
    public void The_Pv_Is_A_Landscape_Page_With_The_Bilingual_Banner_And_Both_Section_Columns()
    {
        var pdf = Pv(Model(5));

        using var document = PdfDocument.Open(pdf);
        var page = document.GetPage(1);
        page.Width.Should().BeGreaterThan(page.Height);

        var text = PdfText.Extract(pdf);
        text.Should().Contain("RÉPUBLIQUE DU SÉNÉGAL").And.Contain("PROCÈS-VERBAL DE SESSION — BFEM FRANCO-ARABE");
        text.Should().Contain("Moy. FR").And.Contain("Moy. AR").And.Contain("Moy. gén.");
        text.Should().Contain("IEN-0000001").And.Contain("Mame Diarra Bousso Faye 5");
        text.Should().Contain("13,25").And.Contain("11,4").And.Contain("12,6");
        text.Should().Contain("Le Chef d'Établissement");
    }

    [Fact]
    public void The_Pv_Prints_Arabic_Names_And_Labels_As_Joined_Letters()
    {
        using var document = PdfDocument.Open(Pv(Model(5)));
        var arabic = document.GetPage(1).Letters.Where(l => l.Value.Length > 0 && IsArabic(l.Value[0])).ToList();

        arabic.Should().HaveCountGreaterThan(100, "bandeau, titre, en-têtes de colonnes et noms des cinq candidats");
        arabic.Count(l => l.Value[0] >= (char)0xFB50).Should().BeGreaterThan(arabic.Count / 2, "lettres liées, pas épelées");
    }

    [Fact]
    public void A_Long_Session_Flows_Over_Several_Pages_With_The_Table_Header_Repeated()
    {
        var pdf = Pv(Model(60));

        using var document = PdfDocument.Open(pdf);
        document.NumberOfPages.Should().BeGreaterThan(1);
        document.GetPage(1).Text.Should().Contain("PROCÈS-VERBAL", "l'en-tête officiel n'est imprimé qu'une fois, en première page");
        foreach (var page in document.GetPages())
        {
            page.Text.Should().Contain("Candidat", "l'en-tête du tableau se répète sur chaque page");
            page.Text.Should().Contain("page", "pied de page numéroté");
        }

        // Une page de suite porte plus de candidats que la première (qui porte l'en-tête officiel).
        document.GetPage(2).GetWords().Count(w => w.Text == "IEN-0000030").Should().BeLessThanOrEqualTo(1);

        PdfText.Extract(pdf).Should().Contain("Mame Diarra Bousso Faye 60", "aucun candidat n'est perdu à la pagination");
    }

    [Fact]
    public void A_Pv_With_A_School_Logo_Renders()
    {
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

        Pv(Model(3), png).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(5)]
    [InlineData(30)]
    public void No_Text_Overlaps_Another_Text_On_The_Pv(int candidates)
    {
        using var document = PdfDocument.Open(Pv(Model(candidates)));

        foreach (var page in document.GetPages())
        {
            Overlaps(page).Should().BeEmpty("aucun mot ne doit en recouvrir un autre");
        }
    }

    [Fact]
    public void No_Text_Overlaps_Another_Text_On_The_Candidate_Sheet()
    {
        using var document = PdfDocument.Open(Sheet(Model(1)));

        Overlaps(document.GetPage(1)).Should().BeEmpty();
    }

    [Fact]
    public void The_Candidate_Sheet_Is_One_Portrait_Page_Per_Candidate()
    {
        using var document = PdfDocument.Open(Sheet(Model(3)));

        document.NumberOfPages.Should().Be(3);
        document.GetPage(1).Height.Should().BeGreaterThan(document.GetPage(1).Width);
    }

    [Fact]
    public void The_Candidate_Sheet_Prints_Identity_Ien_Averages_And_Result()
    {
        var text = PdfText.Extract(Sheet(Model(1)));

        text.Should().Contain("FICHE SYNTHÉTIQUE DU CANDIDAT").And.Contain("BFEM FRANCO-ARABE");
        text.Should().Contain("Mame Diarra Bousso Faye 1").And.Contain("IEN-0000001").And.Contain("ELEV-2026-0001");
        text.Should().Contain("14/03/2011").And.Contain("Saint-Louis").And.Contain("3ème A");
        text.Should().Contain("Collège de Mbour").And.Contain("BFEM-001");
        text.Should().Contain("13,25 /20").And.Contain("11,4 /20").And.Contain("12,6 /20");
        text.Should().Contain("Admis(e) — Bien").And.Contain("Moyenne à l'examen : 12,75").And.Contain("Délibération du 10/07/2027");
    }

    // ---------------------------------------------------------------- Règles d'honnêteté

    [Fact]
    public void A_Missing_Ien_Is_Printed_As_Not_Attributed_Never_Invented()
    {
        PdfText.Extract(Sheet(Model(1, withIen: false))).Should().Contain("non attribué");
        PdfText.Extract(Pv(Model(1, withIen: false))).Should().NotContain("IEN-");
    }

    [Fact]
    public void A_Candidate_Without_A_Result_Prints_A_Dash_Not_A_Failure()
    {
        var text = PdfText.Extract(Pv(Model(2, withResult: false)));

        text.Should().NotContain("Non admis(e)").And.NotContain("Admis(e)");
    }

    [Fact]
    public void The_Cfee_Never_Prints_A_Mention_Even_If_One_Was_Entered_By_Mistake()
    {
        var text = PdfText.Extract(Pv(Model(2, type: ExamType.CFEE, mention: ExamMention.TresBien)));

        text.Should().Contain("Admis(e)").And.NotContain("Très Bien");
        text.Should().Contain("CFEE FRANCO-ARABE");
    }

    [Fact]
    public void A_Bfem_Prints_The_Mention()
    {
        PdfText.Extract(Pv(Model(1, mention: ExamMention.TresBien))).Should().Contain("Admis(e) — Très Bien");
    }

    [Fact]
    public void The_Series_Of_A_Baccalaureat_Is_Printed()
    {
        PdfText.Extract(Pv(Model(1, type: ExamType.BAC, series: "L"))).Should().Contain("Série L").And.Contain("BAC FRANCO-ARABE");
    }

    [Fact]
    public void A_Non_Bilingual_School_Gets_A_French_Only_Pv_Without_Arabic_Or_Section_Columns()
    {
        var pdf = Pv(Model(3, bilingual: false));

        var text = PdfText.Extract(pdf);
        text.Should().Contain("PROCÈS-VERBAL DE SESSION — BFEM").And.NotContain("FRANCO-ARABE");
        text.Should().NotContain("Moy. FR").And.NotContain("Moy. AR");

        using var document = PdfDocument.Open(pdf);
        document.GetPage(1).Letters.Should().NotContain(l => l.Value.Length > 0 && IsArabic(l.Value[0]), "aucun arabe hors module");
    }

    [Fact]
    public void A_Non_Bilingual_Candidate_Sheet_Has_No_Section_Boxes()
    {
        var text = PdfText.Extract(Sheet(Model(1, bilingual: false)));

        text.Should().NotContain("Moyenne section");
        text.Should().Contain("Moyenne générale de l'année");
    }

    [Fact]
    public void A_Candidate_Without_Section_Average_Prints_A_Dash()
    {
        var model = Model(1) with { Candidates = [Candidate(1) with { ArabicAverage = null }] };

        PdfText.Extract(Sheet(model)).Should().Contain("—");
    }

    // ---------------------------------------------------------------- Aides

    private static List<string> Overlaps(Page page)
    {
        var words = page.GetWords().ToList();
        var found = new List<string>();
        for (var i = 0; i < words.Count; i++)
        {
            for (var j = i + 1; j < words.Count; j++)
            {
                if (OverlapRatio(words[i], words[j]) > 0.3)
                {
                    found.Add($"« {words[i].Text} » × « {words[j].Text} »");
                }
            }
        }

        return found;
    }

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
