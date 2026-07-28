using System.Text;
using ClosedXML.Excel;
using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Infrastructure.Files;
using Xunit;

namespace SamaEcole.UnitTests.Teachers;

/// <summary>
/// TeacherImportFileParser lit la STRUCTURE d'un fichier CSV/Excel à six colonnes fixes, sans connaître
/// les matières de l'école (résolues par ImportTeachersCommandHandler contre l'état en base) : ces
/// tests portent uniquement sur ce que le fichier, pris seul, permet ou non de lire — même contrat que
/// StudentImportFileParserTests.
/// </summary>
public class TeacherImportFileParserTests
{
    private readonly TeacherImportFileParser _parser = new();

    private static byte[] Csv(string content) => Encoding.UTF8.GetBytes(content);

    private const string Header = "Nom;Email;Telephone;Naissance;Lieu;Matieres";

    [Fact]
    public void The_Header_Row_Is_Always_Skipped()
    {
        var rows = _parser.Parse(
            Csv($"{Header}\nMoussa Fall;moussa.fall@example.com;+221771234567;15/05/1985;Dakar;Mathématiques (Primaire)"),
            "enseignants.csv");

        rows.Should().ContainSingle();
        rows[0].RowNumber.Should().Be(2, "le numéro de ligne doit rester celui du fichier réel, en-tête compris");
        rows[0].FullName.Should().Be("Moussa Fall");
        rows[0].Email.Should().Be("moussa.fall@example.com");
        rows[0].Phone.Should().Be("+221771234567");
        rows[0].BirthDate.Should().Be("15/05/1985");
        rows[0].BirthPlace.Should().Be("Dakar");
        rows[0].Subjects.Should().Be("Mathématiques (Primaire)");
    }

    [Fact]
    public void Blank_Lines_Do_Not_Shift_Reported_Row_Numbers()
    {
        var rows = _parser.Parse(
            Csv($"{Header}\n\nMoussa Fall;moussa.fall@example.com;+221771234567;15/05/1985;Dakar;Maths\n\nAwa Ndiaye;awa.ndiaye@example.com;+221781234567;01/01/1980;Thiès;Anglais"),
            "enseignants.csv");

        rows.Should().HaveCount(2);
        rows[0].RowNumber.Should().Be(3);
        rows[1].RowNumber.Should().Be(5);
    }

    [Fact]
    public void Missing_Trailing_Optional_Columns_Are_Padded_With_Empty_Strings()
    {
        // Lieu de naissance omis en fin de ligne : jamais une erreur de structure, la validation du
        // CONTENU vit dans ImportTeachersCommandHandler.
        var rows = _parser.Parse(
            Csv($"{Header}\nMoussa Fall;moussa.fall@example.com;+221771234567;15/05/1985"),
            "enseignants.csv");

        rows.Should().ContainSingle();
        rows[0].BirthPlace.Should().Be("");
        rows[0].Subjects.Should().Be("");
    }

    [Fact]
    public void A_Comma_Delimiter_Is_Accepted_When_No_Semicolon_Is_Present()
    {
        var header = Header.Replace(';', ',');
        var rows = _parser.Parse(
            Csv($"{header}\nMoussa Fall,moussa.fall@example.com,+221771234567,15/05/1985,Dakar,\"Maths, Anglais\""),
            "enseignants.csv");

        rows.Should().ContainSingle();
        rows[0].FullName.Should().Be("Moussa Fall");
        rows[0].Subjects.Should().Be("Maths, Anglais");
    }

    [Fact]
    public void Quoted_Fields_With_An_Embedded_Delimiter_Are_Preserved_Whole()
    {
        // Une cellule Matières contenant plusieurs valeurs séparées par une virgule ne doit jamais être
        // coupée en deux colonnes si elle est entre guillemets (fichier délimité par virgule).
        var header = Header.Replace(';', ',');
        var rows = _parser.Parse(
            Csv($"{header}\nMoussa Fall,moussa.fall@example.com,+221771234567,15/05/1985,Dakar,\"Mathématiques (Primaire), Anglais (Primaire)\""),
            "enseignants.csv");

        rows.Should().ContainSingle();
        rows[0].Subjects.Should().Be("Mathématiques (Primaire), Anglais (Primaire)");
    }

    [Fact]
    public void A_Leading_Utf8_Bom_Does_Not_Pollute_The_Header_Detection()
    {
        var withBom = Encoding.UTF8.GetPreamble()
            .Concat(Csv($"{Header}\nMoussa Fall;moussa.fall@example.com;+221771234567;15/05/1985;Dakar;Maths"))
            .ToArray();

        var rows = _parser.Parse(withBom, "enseignants.csv");

        rows.Should().ContainSingle();
        rows[0].FullName.Should().Be("Moussa Fall");
    }

    [Fact]
    public void An_Unsupported_Extension_Is_Rejected()
    {
        var act = () => _parser.Parse(
            Csv($"{Header}\nMoussa Fall;moussa.fall@example.com;+221771234567;15/05/1985;Dakar;Maths"),
            "enseignants.docx");

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void An_Empty_File_Is_Rejected()
    {
        var act = () => _parser.Parse(Csv(""), "enseignants.csv");

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void A_File_With_Only_A_Header_Is_Rejected_As_Empty()
    {
        var act = () => _parser.Parse(Csv(Header), "enseignants.csv");

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void An_Excel_Workbook_Header_Row_Is_Always_Skipped()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Enseignants");
        sheet.Cell(1, 1).Value = "Nom complet";
        sheet.Cell(2, 1).Value = "Moussa Fall";
        sheet.Cell(2, 2).Value = "moussa.fall@example.com";
        sheet.Cell(2, 3).Value = "+221771234567";
        sheet.Cell(2, 4).Value = new DateTime(1985, 5, 15);
        sheet.Cell(2, 5).Value = "Dakar";
        sheet.Cell(2, 6).Value = "Mathématiques (Primaire)";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var rows = _parser.Parse(stream.ToArray(), "enseignants.xlsx");

        rows.Should().ContainSingle();
        rows[0].RowNumber.Should().Be(2, "le numéro de ligne doit rester celui de la feuille Excel réelle, en-tête compris");
        rows[0].FullName.Should().Be("Moussa Fall");
        rows[0].Subjects.Should().Be("Mathématiques (Primaire)");
    }

    [Fact]
    public void A_Real_Excel_Date_Cell_Is_Normalized_To_The_Same_Text_Format_As_A_Typed_Date()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Enseignants");
        sheet.Cell(1, 1).Value = "en-tête";
        sheet.Cell(2, 1).Value = "Moussa Fall";
        sheet.Cell(2, 2).Value = "moussa.fall@example.com";
        sheet.Cell(2, 3).Value = "+221771234567";
        sheet.Cell(2, 4).Value = new DateTime(1985, 5, 15);
        sheet.Cell(2, 4).Style.DateFormat.Format = "dd/mm/yyyy";
        sheet.Cell(2, 5).Value = "Dakar";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var rows = _parser.Parse(stream.ToArray(), "enseignants.xlsx");

        rows.Should().ContainSingle();
        rows[0].BirthDate.Should().Be("15/05/1985");
    }

    [Fact]
    public void A_Corrupted_Excel_File_Is_Rejected_As_A_Validation_Error_Not_A_Raw_Exception()
    {
        var act = () => _parser.Parse(Csv("ceci n'est pas un classeur Excel"), "enseignants.xlsx");

        act.Should().Throw<ValidationException>();
    }
}
