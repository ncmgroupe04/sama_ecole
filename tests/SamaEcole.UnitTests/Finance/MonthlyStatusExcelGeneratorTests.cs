using ClosedXML.Excel;
using FluentAssertions;
using SamaEcole.Application.Finance.Queries.GetMonthlyStatus;
using SamaEcole.Infrastructure.Files;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>L'export Excel doit reprendre exactement les lignes et totaux de la situation affichée.</summary>
public class MonthlyStatusExcelGeneratorTests
{
    private static MonthlyStatusRow Row(string name, string cls, decimal expected, decimal paid, DateTimeOffset? at = null) =>
        new(Guid.NewGuid(), "M-" + name, name, cls, expected, paid, expected - paid, at);

    [Fact]
    public void The_Workbook_Contains_The_Same_Rows_And_Totals_As_The_Report()
    {
        var paidAt = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
        var paid = new[] { Row("Awa", "CM2", 15_000m, 15_000m, paidAt) };
        var partial = new[] { Row("Baba", "CM2", 15_000m, 10_000m) };
        var unpaid = new[] { Row("Cheikh", "6e", 15_000m, 0m), Row("Diary", "6e", 15_000m, 0m) };
        var report = new MonthlyStatusDto(
            "2026-2027", 10, null,
            new MonthlyStatusSummary(4, 1, 1, 2, 60_000m, 25_000m, 35_000m), paid, partial, unpaid);

        var bytes = new MonthlyStatusExcelGenerator().Generate(report, "École A", null);

        using var wb = new XLWorkbook(new MemoryStream(bytes));
        wb.Worksheets.Select(w => w.Name).Should().Equal("Synthèse", "Payé", "Partiel", "Non payé");

        var summary = wb.Worksheet("Synthèse");
        summary.Cell(6, 2).GetString().Should().Be("Toutes les classes");
        summary.Cell(7, 2).GetValue<int>().Should().Be(4);
        summary.Cell(11, 2).GetValue<decimal>().Should().Be(60_000m);
        summary.Cell(12, 2).GetValue<decimal>().Should().Be(25_000m);
        summary.Cell(13, 2).GetValue<decimal>().Should().Be(35_000m);

        var paidSheet = wb.Worksheet("Payé");
        paidSheet.LastRowUsed()!.RowNumber().Should().Be(2);
        paidSheet.Cell(2, 2).GetString().Should().Be("Awa");
        paidSheet.Cell(2, 5).GetValue<decimal>().Should().Be(15_000m);
        paidSheet.Cell(2, 6).GetDateTime().Date.Should().Be(new DateTime(2026, 10, 20));

        var partialSheet = wb.Worksheet("Partiel");
        partialSheet.Cell(2, 6).GetValue<decimal>().Should().Be(5_000m);

        wb.Worksheet("Non payé").LastRowUsed()!.RowNumber().Should().Be(3);
    }
}
