using ClosedXML.Excel;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Exams.Queries.GetExamRegistrationExport;

namespace SamaEcole.Infrastructure.Files;

/// <summary>
/// Relevé d'inscription d'une session d'examen, format provisoire — à faire valider contre un
/// gabarit ministériel réel (ticket JGK-J06). Une feuille, colonnes larges, dates au format Excel
/// natif pour rester exploitables en formule côté IEF/IA (même arbitrage que RevenueReportExcelGenerator).
/// </summary>
public class ExamRegistrationExcelGenerator : IExamRegistrationExcelGenerator
{
    private static readonly XLColor HeaderFill = XLColor.FromHtml("#EEF2FF");

    public byte[] Generate(IReadOnlyList<ExamRegistrationRow> rows, string schoolName, string sessionLabel)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Inscriptions");

        sheet.Cell(1, 1).Value = schoolName;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;

        sheet.Cell(2, 1).Value = $"Relevé d'inscription — {sessionLabel}";
        sheet.Cell(2, 1).Style.Font.Italic = true;

        string[] headers =
        [
            "N° table", "Nom et prénom(s)", "Matricule", "Date de naissance", "Lieu de naissance",
            "Sexe", "Classe", "Série", "Centre d'examen", "Statut du dossier"
        ];

        const int headerRow = 4;

        for (var col = 0; col < headers.Length; col++)
        {
            var header = sheet.Cell(headerRow, col + 1);
            header.Value = headers[col];
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = HeaderFill;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var excelRow = headerRow + 1 + i;

            sheet.Cell(excelRow, 1).Value = row.CandidateNumber ?? "";
            sheet.Cell(excelRow, 2).Value = row.StudentFullName;
            sheet.Cell(excelRow, 3).Value = row.StudentMatricule;

            var birthDateCell = sheet.Cell(excelRow, 4);
            birthDateCell.Value = row.BirthDate.ToDateTime(TimeOnly.MinValue);
            birthDateCell.Style.DateFormat.Format = "dd/mm/yyyy";

            sheet.Cell(excelRow, 5).Value = row.BirthPlace;
            sheet.Cell(excelRow, 6).Value = row.Gender;
            sheet.Cell(excelRow, 7).Value = row.ClassroomName;
            sheet.Cell(excelRow, 8).Value = row.Series ?? "";
            sheet.Cell(excelRow, 9).Value = row.ExamCenterName ?? "";
            sheet.Cell(excelRow, 10).Value = row.Status;
        }

        sheet.Columns(1, headers.Length).AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
