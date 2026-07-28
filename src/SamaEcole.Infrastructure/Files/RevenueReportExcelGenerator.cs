using ClosedXML.Excel;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetRevenueConsolidation;

namespace SamaEcole.Infrastructure.Files;

/// <summary>
/// Export comptable de la consolidation des revenus : une feuille de synthèse, puis une feuille par
/// axe d'analyse (Cycle, Classe, Mode de paiement).
///
/// Les montants portent un FORMAT NUMÉRIQUE Excel (« # ##0 ») et non une chaîne déjà mise en forme :
/// un comptable doit pouvoir sommer, filtrer et croiser ces colonnes dans son propre tableur — un
/// montant écrit « 150 000 FCFA » serait du texte, inexploitable en formule.
/// </summary>
public class RevenueReportExcelGenerator : IRevenueReportExcelGenerator
{
    private const string AmountFormat = "# ##0";
    private static readonly XLColor HeaderFill = XLColor.FromHtml("#EEF2FF");

    public byte[] Generate(RevenueConsolidationDto report, string schoolName)
    {
        using var workbook = new XLWorkbook();

        BuildSummarySheet(workbook, report, schoolName);
        BuildBreakdownSheet(workbook, "Par cycle", "Cycle", report.ByCycle);
        BuildBreakdownSheet(workbook, "Par classe", "Classe", report.ByClassroom);
        BuildBreakdownSheet(workbook, "Par mode de paiement", "Mode de paiement", report.ByPaymentMethod);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void BuildSummarySheet(
        XLWorkbook workbook, RevenueConsolidationDto report, string schoolName)
    {
        var sheet = workbook.Worksheets.Add("Synthèse");

        sheet.Cell(1, 1).Value = schoolName;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;

        sheet.Cell(2, 1).Value = $"Consolidation des revenus du {report.From:dd/MM/yyyy} au {report.To:dd/MM/yyyy}";
        sheet.Cell(2, 1).Style.Font.Italic = true;

        sheet.Cell(4, 1).Value = "Total encaissé (FCFA)";
        sheet.Cell(4, 1).Style.Font.Bold = true;
        sheet.Cell(4, 2).Value = report.TotalCollected;
        sheet.Cell(4, 2).Style.NumberFormat.Format = AmountFormat;

        sheet.Cell(5, 1).Value = "Nombre de versements";
        sheet.Cell(5, 1).Style.Font.Bold = true;
        sheet.Cell(5, 2).Value = report.PaymentCount;

        sheet.Columns(1, 2).AdjustToContents();
    }

    private static void BuildBreakdownSheet(
        XLWorkbook workbook, string sheetName, string labelHeader, IReadOnlyList<RevenueLine> lines)
    {
        var sheet = workbook.Worksheets.Add(sheetName);

        string[] headers = [labelHeader, "Montant (FCFA)", "Versements"];
        for (var col = 0; col < headers.Length; col++)
        {
            var header = sheet.Cell(1, col + 1);
            header.Value = headers[col];
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = HeaderFill;
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var row = i + 2;
            sheet.Cell(row, 1).Value = lines[i].Label;
            sheet.Cell(row, 2).Value = lines[i].Amount;
            sheet.Cell(row, 2).Style.NumberFormat.Format = AmountFormat;
            sheet.Cell(row, 3).Value = lines[i].PaymentCount;
        }

        // Ligne de total : une VRAIE formule SUM plutôt qu'une valeur calculée en C#. Le comptable qui
        // filtre ou corrige une ligne voit le total suivre, au lieu d'un nombre figé qui contredirait
        // soudain les lignes au-dessus.
        if (lines.Count > 0)
        {
            var totalRow = lines.Count + 2;
            sheet.Cell(totalRow, 1).Value = "TOTAL";
            sheet.Cell(totalRow, 1).Style.Font.Bold = true;
            sheet.Cell(totalRow, 2).FormulaA1 = $"SUM(B2:B{lines.Count + 1})";
            sheet.Cell(totalRow, 2).Style.Font.Bold = true;
            sheet.Cell(totalRow, 2).Style.NumberFormat.Format = AmountFormat;
            sheet.Cell(totalRow, 3).FormulaA1 = $"SUM(C2:C{lines.Count + 1})";
            sheet.Cell(totalRow, 3).Style.Font.Bold = true;
        }

        sheet.Columns(1, 3).AdjustToContents();
    }
}
