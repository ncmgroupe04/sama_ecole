using ClosedXML.Excel;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Institutional;

namespace SamaEcole.Infrastructure.Files;

/// <summary>
/// Rapport de rentrée IEF en classeur .xlsx (Évolution N°7) : une feuille par tableau du canevas, chiffres BRUTS
/// (nombres, pas du texte) pour que l'Inspection puisse consolider plusieurs établissements.
/// </summary>
public class IefReportExcelGenerator : IIefReportExcelGenerator
{
    private static readonly XLColor HeaderFill = XLColor.FromHtml("#EEF2FF");

    public byte[] Generate(IefReportDto report)
    {
        using var workbook = new XLWorkbook();
        BuildClasses(workbook, report);
        BuildRepetition(workbook, report);
        BuildTeachers(workbook, report);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void Title(IXLWorksheet sheet, IefReportDto report, string title)
    {
        sheet.Cell(1, 1).Value = $"{report.SchoolName} — {title}";
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(2, 1).Value = $"Année {report.SchoolYearLabel} · IA {report.InspectionAcademie} · IEF {report.InspectionEducationFormation} · âges au {report.AgeReferenceDate:dd/MM/yyyy}";
        sheet.Cell(2, 1).Style.Font.Italic = true;
    }

    private static void Header(IXLWorksheet sheet, int row, IEnumerable<string> titles)
    {
        var col = 1;
        foreach (var title in titles)
        {
            var cell = sheet.Cell(row, col++);
            cell.Value = title;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = HeaderFill;
        }
    }

    private static void BuildClasses(XLWorkbook workbook, IefReportDto report)
    {
        var sheet = workbook.Worksheets.Add("Effectifs par classe");
        Title(sheet, report, "Effectifs par classe, âge et sexe");

        var titles = new List<string> { "Classe", "Niveau", "Âge normal" };
        foreach (var b in report.AgeColumns) { titles.Add($"{b.Label} F"); titles.Add($"{b.Label} G"); }
        titles.AddRange(["Filles", "Garçons", "Total", "Nouveaux", "Redoublants", "Transférés", "En avance", "En retard", "Âge inconnu"]);
        Header(sheet, 4, titles);

        var row = 5;
        foreach (var c in report.Classes)
        {
            var col = 1;
            sheet.Cell(row, col++).Value = c.ClassroomName;
            sheet.Cell(row, col++).Value = c.GradeLevel ?? "";
            sheet.Cell(row, col++).Value = c.NormLabel ?? "";
            foreach (var cell in c.Cells) { sheet.Cell(row, col++).Value = cell.Girls; sheet.Cell(row, col++).Value = cell.Boys; }
            foreach (var v in new[] { c.Girls, c.Boys, c.Total, c.New, c.Repeaters, c.Transferred, c.Early, c.Late, c.UnknownAge })
            {
                sheet.Cell(row, col++).Value = v;
            }
            row++;
        }

        sheet.Cell(row, 1).Value = "TOTAL";
        sheet.Cell(row, 1).Style.Font.Bold = true;
        for (var col = 4; col <= titles.Count; col++)
        {
            sheet.Cell(row, col).FormulaA1 = $"SUM({sheet.Cell(5, col).Address}:{sheet.Cell(row - 1, col).Address})";
            sheet.Cell(row, col).Style.Font.Bold = true;
        }

        sheet.Columns().AdjustToContents();
    }

    private static void BuildRepetition(XLWorkbook workbook, IefReportDto report)
    {
        var sheet = workbook.Worksheets.Add("Redoublement");
        Title(sheet, report, "Taux de redoublement par niveau");
        Header(sheet, 4, ["Niveau", "Effectif", "Redoublants", "dont Filles", "dont Garçons", "Taux (%)"]);

        var row = 5;
        foreach (var r in report.RepetitionByLevel)
        {
            sheet.Cell(row, 1).Value = r.Level;
            sheet.Cell(row, 2).Value = r.Enrolled;
            sheet.Cell(row, 3).Value = r.Repeaters;
            sheet.Cell(row, 4).Value = r.RepeaterGirls;
            sheet.Cell(row, 5).Value = r.RepeaterBoys;
            if (r.RepetitionRate is { } rate) sheet.Cell(row, 6).Value = rate;
            row++;
        }

        sheet.Columns().AdjustToContents();
    }

    private static void BuildTeachers(XLWorkbook workbook, IefReportDto report)
    {
        var sheet = workbook.Worksheets.Add("Corps professoral");
        Title(sheet, report, "Corps professoral");

        Header(sheet, 4, ["Discipline", "Enseignants", "Hommes", "Femmes", "Heures / semaine"]);
        var row = 5;
        foreach (var d in report.TeachersByDiscipline)
        {
            sheet.Cell(row, 1).Value = d.Discipline;
            sheet.Cell(row, 2).Value = d.Teachers;
            sheet.Cell(row, 3).Value = d.Men;
            sheet.Cell(row, 4).Value = d.Women;
            sheet.Cell(row, 5).Value = d.WeeklyHours;
            row++;
        }

        row += 2;
        Header(sheet, row++, ["Diplôme académique", "Diplôme professionnel", "Hommes", "Femmes", "Non renseigné", "Total"]);
        foreach (var d in report.TeachersByDiploma)
        {
            sheet.Cell(row, 1).Value = d.Academic.ToString();
            sheet.Cell(row, 2).Value = d.Professional.ToString();
            sheet.Cell(row, 3).Value = d.Men;
            sheet.Cell(row, 4).Value = d.Women;
            sheet.Cell(row, 5).Value = d.GenderNotReported;
            sheet.Cell(row, 6).Value = d.Count;
            row++;
        }

        row += 2;
        Header(sheet, row++, ["Enseignant", "Sexe", "Disciplines", "Diplôme académique", "Diplôme professionnel", "Heures / semaine"]);
        foreach (var t in report.Teachers)
        {
            sheet.Cell(row, 1).Value = t.FullName;
            sheet.Cell(row, 2).Value = t.Gender ?? "";
            sheet.Cell(row, 3).Value = string.Join(", ", t.Disciplines);
            sheet.Cell(row, 4).Value = t.Academic.ToString();
            sheet.Cell(row, 5).Value = t.Professional.ToString();
            sheet.Cell(row, 6).Value = t.WeeklyHours;
            row++;
        }

        sheet.Columns().AdjustToContents();
    }
}
