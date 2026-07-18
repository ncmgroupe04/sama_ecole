using System.Text;
using ClosedXML.Excel;
using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Infrastructure.Files;
using Xunit;

namespace SamaEcole.UnitTests.Grades;

/// <summary>
/// GradeImportFileParser lit la STRUCTURE d'un fichier CSV/Excel à deux colonnes (matricule, note),
/// sans connaître le roster ni le barème (résolus par ImportGradesCommandHandler contre l'état en
/// base) : ces tests portent uniquement sur ce que le fichier, pris seul, permet ou non de lire.
/// </summary>
public class GradeImportFileParserTests
{
    private readonly GradeImportFileParser _parser = new();

    private static byte[] Csv(string content) => Encoding.UTF8.GetBytes(content);

    [Fact]
    public void A_Csv_Without_Header_Is_Read_As_Pure_Data()
    {
        var rows = _parser.Parse(Csv("ELEV-2025-0001;15\nELEV-2025-0002;12,5"), "notes.csv");

        rows.Should().HaveCount(2);
        rows[0].Should().Be(new SamaEcole.Application.Grades.GradeImportFileRow(1, "ELEV-2025-0001", "15"));
        rows[1].RowNumber.Should().Be(2);
        rows[1].Matricule.Should().Be("ELEV-2025-0002");
        rows[1].RawValue.Should().Be("12,5");
    }

    [Fact]
    public void A_Header_Line_Is_Auto_Detected_And_Skipped()
    {
        var rows = _parser.Parse(Csv("Matricule;Note\nELEV-2025-0001;15"), "notes.csv");

        rows.Should().ContainSingle();
        rows[0].RowNumber.Should().Be(2, "le numéro de ligne doit rester celui du fichier réel, en-tête compris");
        rows[0].Matricule.Should().Be("ELEV-2025-0001");
    }

    [Fact]
    public void Blank_Lines_Do_Not_Shift_Reported_Row_Numbers()
    {
        var rows = _parser.Parse(Csv("Matricule;Note\n\nELEV-2025-0001;15\n\nELEV-2025-0002;10"), "notes.csv");

        rows.Should().HaveCount(2);
        rows[0].RowNumber.Should().Be(3);
        rows[1].RowNumber.Should().Be(5);
    }

    [Fact]
    public void A_Comma_Delimiter_Is_Accepted_When_No_Semicolon_Is_Present()
    {
        var rows = _parser.Parse(Csv("ELEV-2025-0001,15\nELEV-2025-0002,12"), "notes.csv");

        rows.Should().HaveCount(2);
        rows[0].Matricule.Should().Be("ELEV-2025-0001");
        rows[0].RawValue.Should().Be("15");
    }

    [Fact]
    public void A_French_Decimal_Comma_In_The_Value_Does_Not_Break_Comma_Delimited_Files()
    {
        // Le séparateur de colonnes ET le séparateur décimal ne peuvent pas être le même caractère sans
        // ambiguïté : un seul split, sur la PREMIÈRE virgule, garde "12,5" entier comme note.
        var rows = _parser.Parse(Csv("ELEV-2025-0001,12,5"), "notes.csv");

        rows.Should().ContainSingle();
        rows[0].Matricule.Should().Be("ELEV-2025-0001");
        rows[0].RawValue.Should().Be("12,5");
    }

    [Fact]
    public void Quoted_Fields_Are_Unquoted()
    {
        var rows = _parser.Parse(Csv("\"ELEV-2025-0001\";\"15\""), "notes.csv");

        rows.Should().ContainSingle();
        rows[0].Matricule.Should().Be("ELEV-2025-0001");
        rows[0].RawValue.Should().Be("15");
    }

    [Fact]
    public void A_Leading_Utf8_Bom_Does_Not_Pollute_The_First_Matricule()
    {
        var withBom = Encoding.UTF8.GetPreamble().Concat(Csv("ELEV-2025-0001;15")).ToArray();

        var rows = _parser.Parse(withBom, "notes.csv");

        rows.Should().ContainSingle();
        rows[0].Matricule.Should().Be("ELEV-2025-0001");
    }

    [Fact]
    public void An_Unsupported_Extension_Is_Rejected()
    {
        var act = () => _parser.Parse(Csv("ELEV-2025-0001;15"), "notes.txt");

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void An_Empty_File_Is_Rejected()
    {
        var act = () => _parser.Parse(Csv(""), "notes.csv");

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void A_File_With_Only_A_Header_Is_Rejected_As_Empty()
    {
        var act = () => _parser.Parse(Csv("Matricule;Note"), "notes.csv");

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void An_Excel_Workbook_Without_Header_Is_Read_As_Pure_Data()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Notes");
        sheet.Cell(1, 1).Value = "ELEV-2025-0001";
        sheet.Cell(1, 2).Value = 15;
        sheet.Cell(2, 1).Value = "ELEV-2025-0002";
        sheet.Cell(2, 2).Value = 12.5;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var rows = _parser.Parse(stream.ToArray(), "notes.xlsx");

        rows.Should().HaveCount(2);
        rows[0].RowNumber.Should().Be(1);
        rows[0].Matricule.Should().Be("ELEV-2025-0001");
        rows[1].RowNumber.Should().Be(2);
        rows[1].Matricule.Should().Be("ELEV-2025-0002");
    }

    [Fact]
    public void An_Excel_Header_Row_Is_Auto_Detected_And_Skipped()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Notes");
        sheet.Cell(1, 1).Value = "Matricule";
        sheet.Cell(1, 2).Value = "Note";
        sheet.Cell(2, 1).Value = "ELEV-2025-0001";
        sheet.Cell(2, 2).Value = 15;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var rows = _parser.Parse(stream.ToArray(), "notes.xlsx");

        rows.Should().ContainSingle();
        rows[0].RowNumber.Should().Be(2, "le numéro de ligne doit rester celui de la feuille Excel réelle, en-tête compris");
    }

    [Fact]
    public void A_Corrupted_Excel_File_Is_Rejected_As_A_Validation_Error_Not_A_Raw_Exception()
    {
        var act = () => _parser.Parse(Csv("ceci n'est pas un classeur Excel"), "notes.xlsx");

        act.Should().Throw<ValidationException>();
    }
}
