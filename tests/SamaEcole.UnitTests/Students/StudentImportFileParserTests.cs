using System.Text;
using ClosedXML.Excel;
using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Infrastructure.Files;
using Xunit;

namespace SamaEcole.UnitTests.Students;

/// <summary>
/// StudentImportFileParser lit la STRUCTURE d'un fichier CSV/Excel à neuf colonnes fixes, sans
/// connaître les classes de l'école (résolues par ImportStudentsCommandHandler contre l'état en base) :
/// ces tests portent uniquement sur ce que le fichier, pris seul, permet ou non de lire.
///
/// Contrairement à GradeImportFileParser (2 colonnes, en-tête optionnel auto-détecté), la première
/// ligne est TOUJOURS un en-tête ici — voir IStudentImportFileParser.
/// </summary>
public class StudentImportFileParserTests
{
    private readonly StudentImportFileParser _parser = new();

    private static byte[] Csv(string content) => Encoding.UTF8.GetBytes(content);

    private const string Header = "Nom;Naissance;Lieu;Genre;Classe;Tuteur;TelTuteur";

    [Fact]
    public void The_Header_Row_Is_Always_Skipped()
    {
        var rows = _parser.Parse(Csv($"{Header}\nAwa Ndiaye;12/03/2015;Dakar;F;CM2 A;Moussa;+221771234567"), "eleves.csv");

        rows.Should().ContainSingle();
        rows[0].RowNumber.Should().Be(2, "le numéro de ligne doit rester celui du fichier réel, en-tête compris");
        rows[0].FullName.Should().Be("Awa Ndiaye");
        rows[0].BirthDate.Should().Be("12/03/2015");
        rows[0].BirthPlace.Should().Be("Dakar");
        rows[0].Gender.Should().Be("F");
        rows[0].ClassroomName.Should().Be("CM2 A");
        rows[0].GuardianName.Should().Be("Moussa");
        rows[0].GuardianPhone.Should().Be("+221771234567");
    }

    [Fact]
    public void Blank_Lines_Do_Not_Shift_Reported_Row_Numbers()
    {
        var rows = _parser.Parse(Csv($"{Header}\n\nAwa Ndiaye;12/03/2015;Dakar;F;CM2 A;;\n\nModou Diop;01/01/2015;Thiès;M;CM2 A;;"), "eleves.csv");

        rows.Should().HaveCount(2);
        rows[0].RowNumber.Should().Be(3);
        rows[1].RowNumber.Should().Be(5);
    }

    [Fact]
    public void Missing_Trailing_Optional_Columns_Are_Padded_With_Empty_Strings()
    {
        // Tuteur/téléphone omis en fin de ligne : jamais une erreur de structure, ces colonnes sont
        // optionnelles (la validation du CONTENU vit dans ImportStudentsCommandHandler).
        var rows = _parser.Parse(Csv($"{Header}\nAwa Ndiaye;12/03/2015;Dakar;F;CM2 A"), "eleves.csv");

        rows.Should().ContainSingle();
        rows[0].GuardianName.Should().Be("");
        rows[0].GuardianPhone.Should().Be("");
    }

    [Fact]
    public void A_Comma_Delimiter_Is_Accepted_When_No_Semicolon_Is_Present()
    {
        var header = Header.Replace(';', ',');
        var rows = _parser.Parse(Csv($"{header}\nAwa Ndiaye,12/03/2015,Dakar,F,CM2 A,Moussa,+221771234567"), "eleves.csv");

        rows.Should().ContainSingle();
        rows[0].FullName.Should().Be("Awa Ndiaye");
        rows[0].ClassroomName.Should().Be("CM2 A");
    }

    [Fact]
    public void Quoted_Fields_With_An_Embedded_Delimiter_Are_Preserved_Whole()
    {
        // Un nom de tuteur contenant le délimiteur lui-même (ex. « Diop, Awa ») ne doit jamais être
        // coupé en deux colonnes s'il est entre guillemets.
        var rows = _parser.Parse(
            Csv($"{Header}\nAwa Ndiaye;12/03/2015;Dakar;F;CM2 A;\"Diop, Awa\";+221771234567"), "eleves.csv");

        rows.Should().ContainSingle();
        rows[0].GuardianName.Should().Be("Diop, Awa");
    }

    [Fact]
    public void A_Leading_Utf8_Bom_Does_Not_Pollute_The_Header_Detection()
    {
        var withBom = Encoding.UTF8.GetPreamble()
            .Concat(Csv($"{Header}\nAwa Ndiaye;12/03/2015;Dakar;F;CM2 A;;"))
            .ToArray();

        var rows = _parser.Parse(withBom, "eleves.csv");

        rows.Should().ContainSingle();
        rows[0].FullName.Should().Be("Awa Ndiaye");
    }

    [Fact]
    public void An_Unsupported_Extension_Is_Rejected()
    {
        var act = () => _parser.Parse(Csv($"{Header}\nAwa Ndiaye;12/03/2015;Dakar;F;CM2 A;;"), "eleves.docx");

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void An_Empty_File_Is_Rejected()
    {
        var act = () => _parser.Parse(Csv(""), "eleves.csv");

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void A_File_With_Only_A_Header_Is_Rejected_As_Empty()
    {
        var act = () => _parser.Parse(Csv(Header), "eleves.csv");

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void An_Excel_Workbook_Header_Row_Is_Always_Skipped()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Élèves");
        sheet.Cell(1, 1).Value = "Nom complet";
        sheet.Cell(2, 1).Value = "Awa Ndiaye";
        sheet.Cell(2, 2).Value = new DateTime(2015, 3, 12);
        sheet.Cell(2, 3).Value = "Dakar";
        sheet.Cell(2, 4).Value = "F";
        sheet.Cell(2, 5).Value = "CM2 A";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var rows = _parser.Parse(stream.ToArray(), "eleves.xlsx");

        rows.Should().ContainSingle();
        rows[0].RowNumber.Should().Be(2, "le numéro de ligne doit rester celui de la feuille Excel réelle, en-tête compris");
        rows[0].FullName.Should().Be("Awa Ndiaye");
        rows[0].ClassroomName.Should().Be("CM2 A");
    }

    [Fact]
    public void A_Real_Excel_Date_Cell_Is_Normalized_To_The_Same_Text_Format_As_A_Typed_Date()
    {
        // Une cellule de type DATE (widget calendrier Excel) doit produire EXACTEMENT le même texte
        // qu'une cellule où l'utilisateur a tapé "12/03/2015" à la main — la validation de format, elle,
        // reste dans le Handler, pas ici.
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Élèves");
        sheet.Cell(1, 1).Value = "en-tête";
        sheet.Cell(2, 1).Value = "Awa Ndiaye";
        sheet.Cell(2, 2).Value = new DateTime(2015, 3, 12);
        sheet.Cell(2, 2).Style.DateFormat.Format = "dd/mm/yyyy";
        sheet.Cell(2, 3).Value = "Dakar";
        sheet.Cell(2, 4).Value = "F";
        sheet.Cell(2, 5).Value = "CM2 A";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var rows = _parser.Parse(stream.ToArray(), "eleves.xlsx");

        rows.Should().ContainSingle();
        rows[0].BirthDate.Should().Be("12/03/2015");
    }

    [Fact]
    public void A_Corrupted_Excel_File_Is_Rejected_As_A_Validation_Error_Not_A_Raw_Exception()
    {
        var act = () => _parser.Parse(Csv("ceci n'est pas un classeur Excel"), "eleves.xlsx");

        act.Should().Throw<ValidationException>();
    }
}
