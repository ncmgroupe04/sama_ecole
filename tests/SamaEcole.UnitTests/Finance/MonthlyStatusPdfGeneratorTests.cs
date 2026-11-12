using System.Text;
using FluentAssertions;
using SamaEcole.Application.Finance.Queries.GetMonthlyStatus;
using SamaEcole.Application.Finance.Queries.GetMonthlyStatusPdf;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>Le PDF de la situation des mensualités se génère pour chaque catégorie et refuse un statut inconnu.</summary>
public class MonthlyStatusPdfGeneratorTests
{
    public MonthlyStatusPdfGeneratorTests() => PdfFonts.EnsureRegistered();

    private static MonthlyStatusRow Row(string name, decimal expected, decimal paid) =>
        new(Guid.NewGuid(), "M-" + name, name, "CM2", expected, paid, expected - paid, null);

    private static MonthlyStatusDto Report() => new(
        "2026-2027", 11, null,
        new MonthlyStatusSummary(3, 1, 1, 1, 45_000m, 25_000m, 20_000m),
        [Row("Awa", 15_000m, 15_000m)],
        [Row("Baba", 15_000m, 10_000m)],
        [Row("Cheikh", 15_000m, 0m)]);

    [Theory]
    [InlineData(null)]
    [InlineData("paid")]
    [InlineData("partial")]
    [InlineData("unpaid")]
    public void Generates_A_Pdf_For_Every_Category(string? status)
    {
        var bytes = new MonthlyStatusPdfGenerator().Generate(Report(), "École A", null, status);

        bytes.Should().NotBeEmpty();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void A_Single_Category_Pdf_Is_Smaller_Than_The_Full_One()
    {
        var generator = new MonthlyStatusPdfGenerator();

        generator.Generate(Report(), "École A", null, "paid").Length
            .Should().BeLessThan(generator.Generate(Report(), "École A", null, null).Length);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("paid", true)]
    [InlineData("partial", true)]
    [InlineData("unpaid", true)]
    [InlineData("PAID", false)]
    [InlineData("'; drop table", false)]
    public void Status_Is_Whitelisted(string? status, bool valid)
    {
        var result = new GetMonthlyStatusPdfQueryValidator()
            .Validate(new GetMonthlyStatusPdfQuery(Guid.NewGuid(), 11, null, status));

        result.IsValid.Should().Be(valid);
    }

    [Fact]
    public void Long_Names_And_Big_Amounts_Do_Not_Break_The_Layout()
    {
        var longName = "Abdoulaye Mamadou Ibrahima Ousmane Diallo-Ndiaye Sow Fall";
        MonthlyStatusRow Big(int i) => new(
            Guid.NewGuid(), $"ELEV-2026-{i:0000}", longName, "Terminale S2 — série scientifique",
            1_250_000m, 750_000m, 500_000m, DateTimeOffset.UtcNow);

        var rows = Enumerable.Range(1, 120).Select(Big).ToArray();
        var report = new MonthlyStatusDto(
            "2026-2027", 11, null,
            new MonthlyStatusSummary(360, 120, 120, 120, 450_000_000m, 270_000_000m, 180_000_000m), rows, rows, rows);

        var act = () => new MonthlyStatusPdfGenerator().Generate(report, "École Primaire Les Baobabs", null, null);

        act.Should().NotThrow();
    }
}
