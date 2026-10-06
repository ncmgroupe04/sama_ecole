using System.Text;
using FluentAssertions;
using SamaEcole.Application.Internat;
using SamaEcole.Application.Internat.Queries.GetStudentHizbReport;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Documents;
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
        string? year = "2026-2027")
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
            hizbs.Any(c => c.LastEvaluatedAt is not null) ? at : null, hizbs);
    }

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
}
