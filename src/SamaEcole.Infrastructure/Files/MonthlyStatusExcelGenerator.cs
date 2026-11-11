using ClosedXML.Excel;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetMonthlyStatus;

namespace SamaEcole.Infrastructure.Files;

/// <summary>
/// Export de la Situation des mensualités : une feuille « Synthèse » (filtres + indicateurs) puis une feuille par
/// catégorie (Payé, Partiel, Non payé). Montants en FORMAT NUMÉRIQUE Excel, jamais du texte déjà formaté.
/// </summary>
public class MonthlyStatusExcelGenerator : IMonthlyStatusExcelGenerator
{
    private const string AmountFormat = "# ##0";
    private static readonly XLColor HeaderFill = XLColor.FromHtml("#EEF2FF");

    private static readonly string[] MonthNames =
    [
        "Janvier", "Février", "Mars", "Avril", "Mai", "Juin",
        "Juillet", "Août", "Septembre", "Octobre", "Novembre", "Décembre"
    ];

    public byte[] Generate(MonthlyStatusDto report, string schoolName, string? classroomName)
    {
        using var workbook = new XLWorkbook();

        var sheet = workbook.Worksheets.Add("Synthèse");
        sheet.Cell(1, 1).Value = schoolName;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;
        sheet.Cell(2, 1).Value = "Situation des mensualités";
        sheet.Cell(2, 1).Style.Font.Italic = true;

        var s = report.Summary;
        (string Label, object Value, bool Amount)[] lines =
        [
            ("Année scolaire", report.SchoolYearLabel, false),
            ("Mois", MonthNames[report.Month - 1], false),
            ("Classe", classroomName ?? "Toutes les classes", false),
            ("Élèves concernés", s.TotalStudents, false),
            ("Ont intégralement payé", s.PaidCount, false),
            ("Ont partiellement payé", s.PartialCount, false),
            ("N'ont rien payé", s.UnpaidCount, false),
            ("Montant total attendu (FCFA)", s.TotalExpected, true),
            ("Montant total encaissé (FCFA)", s.TotalCollected, true),
            ("Reste à recouvrer (FCFA)", s.TotalRemaining, true)
        ];
        for (var i = 0; i < lines.Length; i++)
        {
            var row = 4 + i;
            sheet.Cell(row, 1).Value = lines[i].Label;
            sheet.Cell(row, 1).Style.Font.Bold = true;
            sheet.Cell(row, 2).Value = XLCellValue.FromObject(lines[i].Value);
            if (lines[i].Amount)
            {
                sheet.Cell(row, 2).Style.NumberFormat.Format = AmountFormat;
            }
        }
        sheet.Columns(1, 2).AdjustToContents();

        BuildList(workbook, "Payé", report.Paid, includeLastPayment: true, includeRemaining: false);
        BuildList(workbook, "Partiel", report.Partial, includeLastPayment: false, includeRemaining: true);
        BuildList(workbook, "Non payé", report.Unpaid, includeLastPayment: false, includeRemaining: true);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void BuildList(
        XLWorkbook workbook, string name, IReadOnlyList<MonthlyStatusRow> rows, bool includeLastPayment, bool includeRemaining)
    {
        var sheet = workbook.Worksheets.Add(name);

        var headers = new List<string> { "Matricule", "Élève", "Classe", "Montant attendu (FCFA)", "Montant payé (FCFA)" };
        if (includeRemaining) headers.Add("Reste à payer (FCFA)");
        if (includeLastPayment) headers.Add("Date du dernier paiement");

        for (var col = 0; col < headers.Count; col++)
        {
            var header = sheet.Cell(1, col + 1);
            header.Value = headers[col];
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = HeaderFill;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var row = 2 + i;
            sheet.Cell(row, 1).Value = r.Matricule;
            sheet.Cell(row, 2).Value = r.StudentFullName;
            sheet.Cell(row, 3).Value = r.ClassroomName;
            sheet.Cell(row, 4).Value = r.ExpectedAmount;
            sheet.Cell(row, 5).Value = r.PaidAmount;
            var col = 6;
            if (includeRemaining)
            {
                sheet.Cell(row, col).Value = r.RemainingAmount;
                sheet.Cell(row, col).Style.NumberFormat.Format = AmountFormat;
                col++;
            }
            if (includeLastPayment)
            {
                if (r.LastPaymentAt is { } at)
                {
                    sheet.Cell(row, col).Value = at.UtcDateTime.Date;
                    sheet.Cell(row, col).Style.DateFormat.Format = "dd/MM/yyyy";
                }
            }
            sheet.Cell(row, 4).Style.NumberFormat.Format = AmountFormat;
            sheet.Cell(row, 5).Style.NumberFormat.Format = AmountFormat;
        }

        sheet.Columns(1, headers.Count).AdjustToContents();
    }
}
