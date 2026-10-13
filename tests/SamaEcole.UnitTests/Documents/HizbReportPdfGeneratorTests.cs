using System.Text;
using FluentAssertions;
using SamaEcole.Application.Internat;
using SamaEcole.Application.Internat.Queries.GetStudentHizbReport;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Documents;
using UglyToad.PdfPig;
using Xunit;

namespace SamaEcole.UnitTests.Documents;

/// <summary>
/// Bulletin coranique : le document se met-il en page sans lever, dans tous les cas limites que la mise en page peut
/// rencontrer ? Une barre d'avancement à 0 % ou à 100 % a un segment de poids nul, qu'un moteur de mise en page refuse :
/// ces cas-là ont leur propre test.
/// </summary>
public class HizbReportPdfGeneratorTests
{
    public HizbReportPdfGeneratorTests() => PdfFonts.EnsureRegistered();

    private static HizbCellDto Cell(int number, int quarters, int? rating = null, DateTimeOffset? at = null) =>
        new(number, quarters, HizbRules.StateFor(quarters), at, rating, quarters == 0 && at is null ? null : 7u);

    private static HizbReportDto Report(
        Func<int, int>? quartersOf = null,
        string? fullNameAr = "عائشة جوب",
        string? instructor = "Serigne Modou",
        string? instructorAr = "سيرين مودو",
        string? address = "Touba, Sénégal",
        string? year = "2026-2027",
        HizbEvolutionDto? evolution = null)
    {
        quartersOf ??= n => n <= 3 ? 4 : n == 4 ? 2 : 0;
        var at = new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero);
        var hizbs = Enumerable.Range(1, 60)
            .Select(n => quartersOf(n) == 0 ? Cell(n, 0) : Cell(n, quartersOf(n), rating: 1 + n % 5, at: at))
            .ToList();
        var quarters = hizbs.Sum(c => c.CompletedQuarters);
        var summary = new HizbSummaryDto(
            hizbs.Count(c => c.State == HizbMemorizationState.Completed),
            hizbs.Count(c => c.State == HizbMemorizationState.InProgress),
            quarters, HizbRules.TotalQuarters, HizbRules.ProgressPercent(quarters));

        return new HizbReportDto(
            "Daara Al Azhar", address, null,
            Guid.NewGuid(), "ELEV-2026-0001", "Awa Diop", fullNameAr,
            new DateOnly(2012, 3, 4), "Touba", "Halqa A", year,
            instructor, instructorAr,
            new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero),
            summary, hizbs.Any(c => c.Rating is not null) ? 3.0m : null,
            hizbs.Any(c => c.LastEvaluatedAt is not null) ? at : null, hizbs, evolution);
    }

    private static string TextOf(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join(" ", document.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
    }

    private static HizbEvolutionDto Evolution(
        int? opening = 4, DateTimeOffset? firstEvaluationAt = null, IReadOnlyList<HizbHistoryPointDto>? points = null,
        IReadOnlyList<HizbEvaluationEntryDto>? recent = null) =>
        new(new DateOnly(2025, 10, 6), new DateOnly(2026, 10, 6), opening, firstEvaluationAt ?? new DateTimeOffset(2025, 9, 1, 10, 0, 0, TimeSpan.Zero),
            points ??
            [
                new HizbHistoryPointDto(new DateOnly(2026, 1, 12), 20, HizbRules.ProgressPercent(20)),
                new HizbHistoryPointDto(new DateOnly(2026, 6, 3), 60, HizbRules.ProgressPercent(60)),
                new HizbHistoryPointDto(new DateOnly(2026, 9, 20), 94, HizbRules.ProgressPercent(94))
            ],
            recent ??
            [
                new HizbEvaluationEntryDto(new DateTimeOffset(2026, 9, 20, 9, 0, 0, TimeSpan.Zero), 24, 1, 3, 4, 94, false, "Ahmad Lô"),
                new HizbEvaluationEntryDto(new DateTimeOffset(2025, 9, 1, 10, 0, 0, TimeSpan.Zero), 1, 0, 4, null, 4, true, null)
            ]);

    private static void ShouldBeAPdf(byte[] pdf)
    {
        pdf.Length.Should().BeGreaterThan(3000);
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void A_Typical_Report_Renders_As_A_Pdf() => ShouldBeAPdf(new HizbReportPdfGenerator().Generate(Report(), null));

    [Fact]
    public void A_Student_At_Zero_Percent_Renders_Without_A_Zero_Weight_Segment() =>
        ShouldBeAPdf(new HizbReportPdfGenerator().Generate(Report(quartersOf: _ => 0), null));

    [Fact]
    public void A_Student_Who_Finished_The_Quran_Renders_At_One_Hundred_Percent() =>
        ShouldBeAPdf(new HizbReportPdfGenerator().Generate(Report(quartersOf: _ => 4), null));

    [Fact]
    public void A_Student_Without_Oustaz_Arabic_Name_Address_Or_Year_Renders_Without_Placeholders() =>
        ShouldBeAPdf(new HizbReportPdfGenerator().Generate(
            Report(fullNameAr: null, instructor: null, instructorAr: null, address: null, year: null), null));

    [Fact]
    public void The_Arabic_Text_Embeds_The_Arabic_Font()
    {
        var withArabic = new HizbReportPdfGenerator().Generate(Report(), null);
        var withoutArabic = new HizbReportPdfGenerator().Generate(Report(fullNameAr: null, instructorAr: null), null);

        // Le titre arabe est toujours imprimé : la police arabe est donc dans les deux, mais les noms arabes en plus
        // allongent le document. On vérifie surtout que l'arabe ne fait pas échouer le rendu.
        ShouldBeAPdf(withArabic);
        ShouldBeAPdf(withoutArabic);
    }

    [Fact]
    public void A_Garbage_Logo_Does_Not_Prevent_The_Report()
    {
        // Le garde de rendu retombe sur une version sans logo si l'image est inexploitable : le bulletin s'émet toujours.
        ShouldBeAPdf(new HizbReportPdfGenerator().Generate(Report(), new byte[] { 1, 2, 3, 4 }));
    }

    // ------------------------------------------------------------------ évolution (journal des évaluations)

    [Fact]
    public void A_Report_With_An_Evolution_Prints_The_Curve_Section_And_The_Last_Evaluations()
    {
        var pdf = new HizbReportPdfGenerator().Generate(Report(evolution: Evolution()), null);

        ShouldBeAPdf(pdf);
        var text = TextOf(pdf);
        text.Should().Contain("Évolution sur 12 mois");
        text.Should().Contain("Dernières évaluations");
        text.Should().Contain("06/10/2025").And.Contain("06/10/2026", "bornes de la période, sous la courbe");
        text.Should().Contain("20/09/2026").And.Contain("Ahmad").And.Contain("3/4");
        text.Should().Contain("Point de départ", "la ligne de reprise du journal est nommée");
    }

    [Fact]
    public void A_Report_Without_Evolution_Data_Does_Not_Print_The_Section()
    {
        var text = TextOf(new HizbReportPdfGenerator().Generate(Report(), null));

        text.Should().NotContain("Évolution sur 12 mois");
    }

    [Fact]
    public void A_Student_Never_Evaluated_Gets_A_Plain_Sentence_Instead_Of_A_Flat_Curve()
    {
        var empty = new HizbEvolutionDto(new DateOnly(2025, 10, 6), new DateOnly(2026, 10, 6), null, null, [], []);

        var text = TextOf(new HizbReportPdfGenerator().Generate(Report(evolution: empty), null));

        text.Should().Contain("Aucune évaluation enregistrée sur cette période.");
        text.Should().Contain("commence à la première évaluation saisie");
        text.Should().NotContain("Dernières évaluations");
    }

    [Fact]
    public void A_Period_That_Starts_Before_The_Journal_Says_Where_The_History_Begins()
    {
        var late = Evolution(opening: null, firstEvaluationAt: new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero));

        var text = TextOf(new HizbReportPdfGenerator().Generate(Report(evolution: late), null));

        text.Should().Contain("Historique disponible à partir du 15/03/2026");
    }

    [Fact]
    public void An_Evolution_With_One_Single_Point_Renders_Without_A_Line()
    {
        var one = Evolution(opening: null, points: [new HizbHistoryPointDto(new DateOnly(2026, 5, 1), 8, HizbRules.ProgressPercent(8))], recent: []);

        ShouldBeAPdf(new HizbReportPdfGenerator().Generate(Report(evolution: one), null));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(3.2, 10)]
    [InlineData(10, 10)]
    [InlineData(10.1, 20)]
    [InlineData(23, 25)]
    [InlineData(51, 75)]
    [InlineData(100, 100)]
    public void The_Vertical_Axis_Rounds_Up_To_A_Readable_Graduation(double value, double expected) =>
        EvolutionChartSvg.NiceMax((decimal)value).Should().Be((decimal)expected);

    [Fact]
    public void The_Chart_Svg_Has_One_Circle_Per_Point_A_Line_And_No_Text()
    {
        var points = new List<HizbHistoryPointDto>
        {
            new(new DateOnly(2026, 1, 12), 20, 8.3m),
            new(new DateOnly(2026, 6, 3), 60, 25m),
            new(new DateOnly(2026, 9, 20), 94, 39.2m)
        };

        var result = EvolutionChartSvg.Build(new DateOnly(2025, 10, 6), new DateOnly(2026, 10, 6), 4, points);

        result.YMax.Should().Be(50m);
        System.Text.RegularExpressions.Regex.Matches(result.Svg, "<circle ").Count.Should().Be(4, "ouverture + trois jours");
        result.Svg.Should().Contain("<path").And.NotContain("<text", "le texte est imprimé par le document, dans sa police");
        result.Svg.Should().NotContain(",,").And.NotContain("NaN").And.NotContain("Infinity");
    }

    [Fact]
    public void The_Chart_Svg_Uses_Decimal_Points_Whatever_The_Server_Culture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
            var svg = EvolutionChartSvg.Build(
                new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), null,
                [new HizbHistoryPointDto(new DateOnly(2026, 3, 3), 7, 2.9m), new HizbHistoryPointDto(new DateOnly(2026, 9, 9), 9, 3.8m)]).Svg;

            // Les coordonnées s'écrivent « x,y » séparées par des espaces : trois nombres collés par des virgules (« 12,3,45,6 »)
            // signaleraient une virgule décimale.
            svg.Should().NotMatchRegex(@"\d,\d+,\d", "une virgule décimale casserait le SVG sur un serveur en français");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }
}
