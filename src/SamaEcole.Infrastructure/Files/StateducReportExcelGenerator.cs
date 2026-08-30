using ClosedXML.Excel;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.StateIntegration;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Infrastructure.Files;

/// <summary>
/// Rapport STATEDUC en classeur .xlsx — une feuille par tableau réglementaire (Volume 1 §23.3).
///
/// Tous les effectifs sont des NOMBRES, jamais des chaînes déjà mises en forme : l'agent de l'IEF
/// consolide plusieurs établissements dans son propre tableur, et un « 214 élèves » textuel casserait
/// chacune de ses formules. Même règle que <see cref="RevenueReportExcelGenerator"/>.
///
/// Les ratios sortent en FRACTION (0,532) avec un format de cellule pourcentage, et non en nombre
/// déjà multiplié par 100 : c'est ce qui permet de les moyenner correctement en aval. Un « 53,2 »
/// stocké tel quel produirait des moyennes de moyennes fausses d'un facteur 100.
/// </summary>
public class StateducReportExcelGenerator : IStateducReportExcelGenerator
{
    private const string PercentFormat = "0.0%";
    private const string DecimalFormat = "0.0";

    private static readonly XLColor HeaderFill = XLColor.FromHtml("#EEF2FF");

    public byte[] Generate(StateducReportDto report)
    {
        using var workbook = new XLWorkbook();

        BuildSummarySheet(workbook, report);
        BuildEnrollmentsSheet(workbook, report);
        BuildAgePyramidSheet(workbook, report);
        BuildQualificationsSheet(workbook, report);
        BuildStatusesSheet(workbook, report);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void BuildSummarySheet(XLWorkbook workbook, StateducReportDto report)
    {
        var sheet = workbook.Worksheets.Add("Synthèse");

        sheet.Cell(1, 1).Value = report.SchoolName;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;

        sheet.Cell(2, 1).Value = $"Rapport annuel STATEDUC — {report.SchoolYearLabel}";
        sheet.Cell(2, 1).Style.Font.Italic = true;

        var row = 4;

        // L'en-tête réglementaire d'abord : sans lui, une feuille consolidée ne sait plus de quel
        // établissement viennent les chiffres.
        WriteText(sheet, ref row, "Code établissement (SIMEN)", report.NationalSchoolCode);
        WriteText(sheet, ref row, "N° d'autorisation ministérielle", report.MinistryAuthorizationNumber);
        WriteText(sheet, ref row, "Code circonscription", report.SchoolDistrictCode);
        WriteText(sheet, ref row, "Inspection d'Académie", report.InspectionAcademie);
        WriteText(sheet, ref row, "Inspection de l'Éducation et de la Formation", report.InspectionEducationFormation);
        WriteText(sheet, ref row, "Coordonnées GPS", report.GpsCoordinates);
        WriteText(sheet, ref row, "Adresse", report.Address);
        WriteText(sheet, ref row, "Téléphone", report.Phone);
        WriteText(sheet, ref row, "Courriel", report.Email);

        row++;
        WriteDate(sheet, ref row, "Effectifs arrêtés au", report.ObservationDate);
        WriteText(sheet, ref row, "Généré le", report.GeneratedAt.ToString("dd/MM/yyyy HH:mm"));

        row++;
        WriteNumber(sheet, ref row, "Effectif total", report.TotalStudents);
        WriteNumber(sheet, ref row, "Filles", report.TotalGirls);
        WriteNumber(sheet, ref row, "Garçons", report.TotalBoys);
        WriteRatio(sheet, ref row, "Part de filles", report.GirlsRatio);
        WriteNumber(sheet, ref row, "Redoublants", report.TotalRepeaters);

        row++;
        WriteNumber(sheet, ref row, "Enseignants", report.TotalTeachers);
        WriteNumber(sheet, ref row, "Enseignants qualifiés (diplôme professionnel)", report.QualifiedTeachers);
        WriteRatio(sheet, ref row, "Taux de qualification",
            StateducReportDto.Ratio(report.QualifiedTeachers, report.TotalTeachers));
        WriteDecimal(sheet, ref row, "Élèves par enseignant", report.StudentsPerTeacher);

        row++;
        WriteNumber(sheet, ref row, "Salles de classe", report.ClassroomCount);
        WriteNumber(sheet, ref row, "Salles physiques", report.PhysicalRoomCount);
        WriteNumber(sheet, ref row, "Bâtiments", report.BuildingCount);
        WriteDecimal(sheet, ref row, "Élèves par classe", report.StudentsPerClassroom);

        // Bloc QUALITÉ DE SAISIE, séparé des effectifs déclarés. Il figure dans le classeur pour la
        // même raison qu'il figure sur le PDF : ces lacunes ne sont imputées à aucune catégorie, et
        // sans elles la somme des tableaux ne s'explique pas.
        row++;
        sheet.Cell(row, 1).Value = "Données incomplètes (non imputées)";
        sheet.Cell(row, 1).Style.Font.Bold = true;
        row++;
        WriteNumber(sheet, ref row, "Élèves sans IEN", report.StudentsWithoutIen);
        WriteNumber(sheet, ref row, "Enseignants sans diplôme professionnel saisi", report.UnreportedQualificationTeachers);
        WriteNumber(sheet, ref row, "Enseignants sans genre saisi", report.TeachersWithoutGender);
        WriteNumber(sheet, ref row, "Élèves à date de naissance invraisemblable",
            report.AgePyramid.FirstOrDefault(r => r.Age is null)?.Total ?? 0);

        sheet.Columns(1, 2).AdjustToContents();
    }

    private static void BuildEnrollmentsSheet(XLWorkbook workbook, StateducReportDto report)
    {
        var sheet = workbook.Worksheets.Add("Effectifs par niveau");

        WriteHeaders(sheet, [
            "Niveau", "Cycle", "Garçons", "Filles", "Total", "% filles",
            "Redoublants", "Divisions", "Effectif / division", "Sans IEN"
        ]);

        var row = 2;
        foreach (var line in report.EnrollmentsByLevel)
        {
            sheet.Cell(row, 1).Value = line.Level;
            sheet.Cell(row, 2).Value = line.Cycle;
            sheet.Cell(row, 3).Value = line.Boys;
            sheet.Cell(row, 4).Value = line.Girls;
            sheet.Cell(row, 5).Value = line.Total;
            SetRatio(sheet.Cell(row, 6), line.GirlsRatio);
            sheet.Cell(row, 7).Value = line.Repeaters;
            sheet.Cell(row, 8).Value = line.ClassroomCount;
            SetDecimal(sheet.Cell(row, 9), line.AverageClassSize);
            sheet.Cell(row, 10).Value = line.WithoutIen;
            row++;
        }

        sheet.Cell(row, 1).Value = "TOTAL";
        sheet.Cell(row, 3).Value = report.TotalBoys;
        sheet.Cell(row, 4).Value = report.TotalGirls;
        sheet.Cell(row, 5).Value = report.TotalStudents;
        SetRatio(sheet.Cell(row, 6), report.GirlsRatio);
        sheet.Cell(row, 7).Value = report.TotalRepeaters;
        sheet.Cell(row, 8).Value = report.ClassroomCount;
        SetDecimal(sheet.Cell(row, 9), report.StudentsPerClassroom);
        sheet.Cell(row, 10).Value = report.StudentsWithoutIen;
        sheet.Row(row).Style.Font.Bold = true;

        sheet.Columns(1, 10).AdjustToContents();
    }

    private static void BuildAgePyramidSheet(XLWorkbook workbook, StateducReportDto report)
    {
        var sheet = workbook.Worksheets.Add("Pyramide des âges");

        WriteHeaders(sheet, ["Âge révolu", "Garçons", "Filles", "Total", "% effectif"]);

        var row = 2;
        foreach (var line in report.AgePyramid)
        {
            // L'âge part en NOMBRE quand il est déterminé — pour que la pyramide reste triable et
            // graphable dans Excel. La tranche d'anomalie, elle, n'a pas d'âge : elle porte son
            // libellé textuel, ce qui la rend impossible à confondre avec une classe d'âge réelle.
            if (line.Age is { } age)
            {
                sheet.Cell(row, 1).Value = age;
            }
            else
            {
                sheet.Cell(row, 1).Value = line.Label;
                sheet.Cell(row, 1).Style.Font.Italic = true;
            }

            sheet.Cell(row, 2).Value = line.Boys;
            sheet.Cell(row, 3).Value = line.Girls;
            sheet.Cell(row, 4).Value = line.Total;
            SetRatio(sheet.Cell(row, 5), StateducReportDto.Ratio(line.Total, report.TotalStudents));
            row++;
        }

        sheet.Columns(1, 5).AdjustToContents();
    }

    private static void BuildQualificationsSheet(XLWorkbook workbook, StateducReportDto report)
    {
        var sheet = workbook.Worksheets.Add("Qualifications");

        WriteHeaders(sheet, [
            "Diplôme académique", "Diplôme professionnel", "Hommes", "Femmes", "Genre non saisi", "Total"
        ]);

        var row = 2;
        foreach (var line in report.TeacherQualifications)
        {
            sheet.Cell(row, 1).Value = Describe(line.AcademicQualification);
            sheet.Cell(row, 2).Value = Describe(line.ProfessionalQualification);
            sheet.Cell(row, 3).Value = line.Men;
            sheet.Cell(row, 4).Value = line.Women;
            sheet.Cell(row, 5).Value = line.GenderNotReported;
            sheet.Cell(row, 6).Value = line.Count;
            row++;
        }

        sheet.Cell(row, 1).Value = "TOTAL";
        sheet.Cell(row, 3).Value = report.TeacherQualifications.Sum(r => r.Men);
        sheet.Cell(row, 4).Value = report.TeacherQualifications.Sum(r => r.Women);
        sheet.Cell(row, 5).Value = report.TeachersWithoutGender;
        sheet.Cell(row, 6).Value = report.TotalTeachers;
        sheet.Row(row).Style.Font.Bold = true;

        sheet.Columns(1, 6).AdjustToContents();
    }

    private static void BuildStatusesSheet(XLWorkbook workbook, StateducReportDto report)
    {
        var sheet = workbook.Worksheets.Add("Statuts");

        WriteHeaders(sheet, ["Statut administratif", "Hommes", "Femmes", "Genre non saisi", "Total"]);

        var row = 2;
        foreach (var line in report.TeacherStatuses)
        {
            sheet.Cell(row, 1).Value = Describe(line.Status);
            sheet.Cell(row, 2).Value = line.Men;
            sheet.Cell(row, 3).Value = line.Women;
            sheet.Cell(row, 4).Value = line.GenderNotReported;
            sheet.Cell(row, 5).Value = line.Count;
            row++;
        }

        sheet.Columns(1, 5).AdjustToContents();
    }

    // ------------------------------------------------------------------------------- Fragments

    private static void WriteHeaders(IXLWorksheet sheet, string[] headers)
    {
        for (var col = 0; col < headers.Length; col++)
        {
            var cell = sheet.Cell(1, col + 1);
            cell.Value = headers[col];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = HeaderFill;
        }
    }

    /// <summary>
    /// Une valeur absente laisse la cellule VIDE, jamais « — » ni « N/A » : un tiret dans une colonne
    /// par ailleurs numérique la transforme en colonne texte, et toutes les formules de l'agent
    /// cessent de fonctionner sans qu'il comprenne pourquoi.
    /// </summary>
    private static void WriteText(IXLWorksheet sheet, ref int row, string label, string? value)
    {
        sheet.Cell(row, 1).Value = label;
        sheet.Cell(row, 1).Style.Font.Bold = true;

        if (!string.IsNullOrWhiteSpace(value))
        {
            sheet.Cell(row, 2).Value = value;
        }

        row++;
    }

    private static void WriteNumber(IXLWorksheet sheet, ref int row, string label, int value)
    {
        sheet.Cell(row, 1).Value = label;
        sheet.Cell(row, 1).Style.Font.Bold = true;
        sheet.Cell(row, 2).Value = value;
        row++;
    }

    private static void WriteDate(IXLWorksheet sheet, ref int row, string label, DateOnly value)
    {
        sheet.Cell(row, 1).Value = label;
        sheet.Cell(row, 1).Style.Font.Bold = true;
        sheet.Cell(row, 2).Value = value.ToDateTime(TimeOnly.MinValue);
        sheet.Cell(row, 2).Style.DateFormat.Format = "dd/MM/yyyy";
        row++;
    }

    private static void WriteRatio(IXLWorksheet sheet, ref int row, string label, decimal? ratio)
    {
        sheet.Cell(row, 1).Value = label;
        sheet.Cell(row, 1).Style.Font.Bold = true;
        SetRatio(sheet.Cell(row, 2), ratio);
        row++;
    }

    private static void WriteDecimal(IXLWorksheet sheet, ref int row, string label, decimal? value)
    {
        sheet.Cell(row, 1).Value = label;
        sheet.Cell(row, 1).Style.Font.Bold = true;
        SetDecimal(sheet.Cell(row, 2), value);
        row++;
    }

    /// <summary>
    /// Le DTO porte le ratio en pourcentage (0–100) pour l'impression ; Excel attend une FRACTION avec
    /// un format pourcentage. La division par 100 se fait donc ici, au seul endroit qui écrit dans une
    /// cellule — la faire dans le DTO casserait le PDF, la faire dans les deux la doublerait.
    /// </summary>
    private static void SetRatio(IXLCell cell, decimal? ratio)
    {
        if (ratio is not { } value)
        {
            return;
        }

        cell.Value = value / 100m;
        cell.Style.NumberFormat.Format = PercentFormat;
    }

    private static void SetDecimal(IXLCell cell, decimal? value)
    {
        if (value is not { } v)
        {
            return;
        }

        cell.Value = v;
        cell.Style.NumberFormat.Format = DecimalFormat;
    }

    private static string Describe(AcademicQualification value) => value switch
    {
        AcademicQualification.NonRenseigne => "Non renseigné",
        AcademicQualification.Aucun => "Aucun",
        AcademicQualification.BAC => "Baccalauréat",
        _ => value.ToString()
    };

    private static string Describe(ProfessionalQualification value) => value switch
    {
        ProfessionalQualification.NonRenseigne => "Non renseigné",
        ProfessionalQualification.Aucun => "Aucun",
        _ => value.ToString()
    };

    private static string Describe(TeacherCivilServiceStatus value) => value switch
    {
        TeacherCivilServiceStatus.NonRenseigne => "Non renseigné",
        TeacherCivilServiceStatus.Benevole => "Bénévole",
        _ => value.ToString()
    };
}
