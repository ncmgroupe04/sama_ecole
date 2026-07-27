using ClosedXML.Excel;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetDebtorAgingReport;

namespace SamaEcole.Infrastructure.Files;

/// <summary>
/// Export comptable des débiteurs (Étape 5) : une feuille, une ligne par élève débiteur, triée du
/// retard le plus ancien au plus récent. Même moule que RevenueReportExcelGenerator — montants en
/// FORMAT NUMÉRIQUE Excel, jamais une chaîne déjà mise en forme.
/// </summary>
public class DebtorAgingExcelGenerator : IDebtorAgingExcelGenerator
{
    private const string AmountFormat = "# ##0";
    private static readonly XLColor HeaderFill = XLColor.FromHtml("#EEF2FF");

    public byte[] Generate(DebtorAgingReportDto report, string schoolName)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Débiteurs");

        sheet.Cell(1, 1).Value = schoolName;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;

        sheet.Cell(2, 1).Value = $"Liste des débiteurs au {report.GeneratedOn:dd/MM/yyyy}";
        sheet.Cell(2, 1).Style.Font.Italic = true;

        string[] headers = ["Matricule", "Élève", "Classe", "Solde dû (FCFA)", "Jours de retard", "Téléphone tuteur"];
        const int headerRow = 4;
        for (var col = 0; col < headers.Length; col++)
        {
            var header = sheet.Cell(headerRow, col + 1);
            header.Value = headers[col];
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = HeaderFill;
        }

        for (var i = 0; i < report.Debtors.Count; i++)
        {
            var row = headerRow + 1 + i;
            var debtor = report.Debtors[i];

            sheet.Cell(row, 1).Value = debtor.Matricule;
            sheet.Cell(row, 2).Value = debtor.StudentFullName;
            sheet.Cell(row, 3).Value = debtor.ClassroomName;
            sheet.Cell(row, 4).Value = debtor.RemainingBalance;
            sheet.Cell(row, 4).Style.NumberFormat.Format = AmountFormat;
            sheet.Cell(row, 5).Value = debtor.DaysOverdue;
            sheet.Cell(row, 6).Value = debtor.GuardianPhone ?? string.Empty;
        }

        if (report.Debtors.Count > 0)
        {
            var totalRow = headerRow + 1 + report.Debtors.Count;
            sheet.Cell(totalRow, 3).Value = "TOTAL";
            sheet.Cell(totalRow, 3).Style.Font.Bold = true;
            sheet.Cell(totalRow, 4).FormulaA1 = $"SUM(D{headerRow + 1}:D{totalRow - 1})";
            sheet.Cell(totalRow, 4).Style.Font.Bold = true;
            sheet.Cell(totalRow, 4).Style.NumberFormat.Format = AmountFormat;
        }

        sheet.Columns(1, 6).AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
