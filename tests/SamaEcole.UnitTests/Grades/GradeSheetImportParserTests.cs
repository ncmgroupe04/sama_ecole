using ClosedXML.Excel;
using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Infrastructure.Files;
using Xunit;

namespace SamaEcole.UnitTests.Grades;

/// <summary>
/// Le cœur du ticket (harmonisation import/export Excel des notes) : les colonnes sont reconnues par le
/// TEXTE de leur en-tête, jamais par leur position — un fichier dont les colonnes ont été réordonnées,
/// ou dont l'en-tête est mal orthographié, ne doit JAMAIS silencieusement associer une note à la
/// mauvaise épreuve. Ces tests ne valident QUE la structure du fichier — le contrôle métier (matricule
/// connu, note dans le barème) vit dans ImportGradeSheetCommandHandler, pas ici.
/// </summary>
public class GradeSheetImportParserTests
{
    private readonly GradeSheetImportParser _parser = new();

    private static byte[] BuildXlsx(string[] headers, IEnumerable<string?[]> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Notes");

        for (var col = 0; col < headers.Length; col++)
        {
            sheet.Cell(1, col + 1).Value = headers[col];
        }

        var rowNumber = 2;
        foreach (var row in rows)
        {
            for (var col = 0; col < row.Length; col++)
            {
                if (row[col] is { } value) sheet.Cell(rowNumber, col + 1).Value = value;
            }
            rowNumber++;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    [Fact]
    public void Columns_Are_Mapped_By_Header_Name_In_Their_Natural_Order()
    {
        var file = BuildXlsx(
            ["Matricule", "Nom & Prénom", "Devoir 1", "Devoir 2", "Composition"],
            [["ELEV-0001", "Awa Ndiaye", "15", "14", "16"]]);

        var rows = _parser.Parse(file, "notes.xlsx");

        rows.Should().ContainSingle();
        rows[0].Matricule.Should().Be("ELEV-0001");
        rows[0].Devoir1Raw.Should().Be("15");
        rows[0].Devoir2Raw.Should().Be("14");
        rows[0].CompositionRaw.Should().Be("16");
    }

    [Fact]
    public void Columns_In_A_Reordered_File_Still_Map_To_The_Right_Evaluation()
    {
        // Composition avant Devoir 2 avant Devoir 1 avant Matricule — l'ordre ne doit avoir aucune importance.
        var file = BuildXlsx(
            ["Composition", "Devoir 2", "Devoir 1", "Matricule"],
            [["16", "14", "15", "ELEV-0001"]]);

        var rows = _parser.Parse(file, "notes.xlsx");

        rows.Should().ContainSingle();
        rows[0].Matricule.Should().Be("ELEV-0001");
        rows[0].Devoir1Raw.Should().Be("15");
        rows[0].Devoir2Raw.Should().Be("14");
        rows[0].CompositionRaw.Should().Be("16");
    }

    [Fact]
    public void The_Grading_Scale_Suffix_And_Accents_In_The_Header_Are_Ignored()
    {
        var file = BuildXlsx(
            ["MATRICULE", "DEVOIR 1 (/20)", "Composition (/20)"],
            [["ELEV-0001", "15", "16"]]);

        var rows = _parser.Parse(file, "notes.xlsx");

        rows[0].Devoir1Raw.Should().Be("15");
        rows[0].CompositionRaw.Should().Be("16");
    }

    [Fact]
    public void An_Unrecognized_Column_Such_As_Full_Name_Is_Simply_Ignored()
    {
        var file = BuildXlsx(
            ["Matricule", "Nom & Prénom", "Devoir 1"],
            [["ELEV-0001", "Awa Ndiaye", "15"]]);

        var rows = _parser.Parse(file, "notes.xlsx");

        rows.Should().ContainSingle();
        rows[0].Devoir1Raw.Should().Be("15");
    }

    [Fact]
    public void A_Missing_Cell_For_An_Evaluation_Column_Yields_An_Empty_Raw_Value_Not_A_Zero()
    {
        var file = BuildXlsx(
            ["Matricule", "Devoir 1", "Devoir 2", "Composition"],
            [["ELEV-0001", "15", null, null]]);

        var rows = _parser.Parse(file, "notes.xlsx");

        rows[0].Devoir1Raw.Should().Be("15");
        rows[0].Devoir2Raw.Should().Be("");
        rows[0].CompositionRaw.Should().Be("");
    }

    [Fact]
    public void A_File_Without_A_Matricule_Header_Is_Rejected()
    {
        var file = BuildXlsx(["Nom", "Devoir 1"], [["Awa Ndiaye", "15"]]);

        var act = () => _parser.Parse(file, "notes.xlsx");

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void A_File_With_No_Evaluation_Column_At_All_Is_Rejected()
    {
        var file = BuildXlsx(["Matricule", "Nom & Prénom"], [["ELEV-0001", "Awa Ndiaye"]]);

        var act = () => _parser.Parse(file, "notes.xlsx");

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void Two_Columns_Claiming_The_Same_Evaluation_Are_Rejected()
    {
        var file = BuildXlsx(["Matricule", "Devoir 1", "Devoir 1"], [["ELEV-0001", "15", "17"]]);

        var act = () => _parser.Parse(file, "notes.xlsx");

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void A_Header_Row_Preceded_By_An_Instructions_Row_Is_Still_Found()
    {
        // Reproduit la feuille exportée par GradeSheetExcelGenerator : ligne 1 = consigne, ligne 2 = en-tête.
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Notes");
        sheet.Cell(1, 1).Value = "Saisir des notes entre 0 et 20.";
        sheet.Cell(2, 1).Value = "Matricule";
        sheet.Cell(2, 2).Value = "Devoir 1";
        sheet.Cell(3, 1).Value = "ELEV-0001";
        sheet.Cell(3, 2).Value = "15";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var rows = _parser.Parse(stream.ToArray(), "notes.xlsx");

        rows.Should().ContainSingle();
        rows[0].RowNumber.Should().Be(3);
        rows[0].Devoir1Raw.Should().Be("15");
    }

    [Fact]
    public void A_Blank_Row_Between_Data_Rows_Is_Skipped()
    {
        var file = BuildXlsx(
            ["Matricule", "Devoir 1"],
            [["ELEV-0001", "15"], ["", ""], ["ELEV-0002", "12"]]);

        var rows = _parser.Parse(file, "notes.xlsx");

        rows.Should().HaveCount(2);
    }

    [Fact]
    public void An_Empty_File_Is_Rejected()
    {
        using var workbook = new XLWorkbook();
        workbook.Worksheets.Add("Notes");
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var act = () => _parser.Parse(stream.ToArray(), "notes.xlsx");

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void An_Unsupported_Extension_Is_Rejected()
    {
        var file = BuildXlsx(["Matricule", "Devoir 1"], [["ELEV-0001", "15"]]);

        var act = () => _parser.Parse(file, "notes.docx");

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void A_Corrupted_File_Is_Rejected_With_A_Readable_Message()
    {
        var act = () => _parser.Parse([1, 2, 3, 4], "notes.xlsx");

        act.Should().Throw<ValidationException>();
    }
}
