using ClosedXML.Excel;
using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Infrastructure.Files;
using Xunit;

namespace SamaEcole.UnitTests.Grades;

/// <summary>
/// Exigences du ticket (harmonisation import/export Excel des notes) : en-têtes explicites, ligne de
/// consigne, colonnes d'identification (Matricule, Nom &amp; Prénom) verrouillées pour empêcher toute
/// modification accidentelle — seules les trois colonnes de notes restent éditables.
/// </summary>
public class GradeSheetExcelGeneratorTests
{
    private readonly GradeSheetExcelGenerator _generator = new();

    private static IXLWorksheet ReadBack(byte[] content)
    {
        var workbook = new XLWorkbook(new MemoryStream(content));
        return workbook.Worksheets.First();
    }

    [Fact]
    public void The_Header_Row_Lists_The_Five_Columns_With_The_Grading_Scale()
    {
        var rows = new[] { new GradeSheetStudentRow("ELEV-0001", "Awa Ndiaye", 15, 14, 16) };

        var sheet = ReadBack(_generator.Generate(rows, gradingScale: 20));

        var headerCells = sheet.Row(2).CellsUsed().Select(c => c.GetString()).ToList();
        headerCells.Should().Contain("MATRICULE");
        headerCells.Should().Contain("NOM & PRÉNOM");
        headerCells.Should().Contain("DEVOIR 1 (/20)");
        headerCells.Should().Contain("DEVOIR 2 (/20)");
        headerCells.Should().Contain("COMPOSITION (/20)");
    }

    [Fact]
    public void The_Grading_Scale_Adapts_To_A_Primaire_Cycle()
    {
        var rows = new[] { new GradeSheetStudentRow("ELEV-0001", "Awa Ndiaye", 8, null, null) };

        var sheet = ReadBack(_generator.Generate(rows, gradingScale: 10));

        var headerCells = sheet.Row(2).CellsUsed().Select(c => c.GetString()).ToList();
        headerCells.Should().Contain("DEVOIR 1 (/10)");
    }

    [Fact]
    public void The_First_Row_Carries_Instructions_Mentioning_The_Scale()
    {
        var sheet = ReadBack(_generator.Generate([], gradingScale: 20));

        sheet.Cell(1, 1).GetString().Should().Contain("0").And.Contain("20");
    }

    [Fact]
    public void Existing_Grades_Are_Prefilled()
    {
        var rows = new[] { new GradeSheetStudentRow("ELEV-0001", "Awa Ndiaye", 15, null, 16) };

        var sheet = ReadBack(_generator.Generate(rows, gradingScale: 20));

        var dataRow = sheet.Row(3);
        dataRow.Cell(1).GetString().Should().Be("ELEV-0001");
        dataRow.Cell(2).GetString().Should().Be("Awa Ndiaye");
        dataRow.Cell(3).GetDouble().Should().Be(15);
        dataRow.Cell(4).IsEmpty().Should().BeTrue("Devoir 2 n'a pas encore été saisi");
        dataRow.Cell(5).GetDouble().Should().Be(16);
    }

    [Fact]
    public void The_Sheet_Is_Protected()
    {
        var sheet = ReadBack(_generator.Generate([new GradeSheetStudentRow("ELEV-0001", "Awa Ndiaye", null, null, null)], 20));

        sheet.Protection.IsProtected.Should().BeTrue();
    }

    [Fact]
    public void The_Matricule_And_Full_Name_Columns_Are_Locked()
    {
        var sheet = ReadBack(_generator.Generate([new GradeSheetStudentRow("ELEV-0001", "Awa Ndiaye", null, null, null)], 20));

        var dataRow = sheet.Row(3);
        dataRow.Cell(1).Style.Protection.Locked.Should().BeTrue("la colonne Matricule ne doit pas pouvoir être modifiée");
        dataRow.Cell(2).Style.Protection.Locked.Should().BeTrue("la colonne Nom & Prénom ne doit pas pouvoir être modifiée");
    }

    [Fact]
    public void The_Note_Columns_Are_Unlocked()
    {
        var sheet = ReadBack(_generator.Generate([new GradeSheetStudentRow("ELEV-0001", "Awa Ndiaye", null, null, null)], 20));

        var dataRow = sheet.Row(3);
        dataRow.Cell(3).Style.Protection.Locked.Should().BeFalse("Devoir 1 doit rester éditable");
        dataRow.Cell(4).Style.Protection.Locked.Should().BeFalse("Devoir 2 doit rester éditable");
        dataRow.Cell(5).Style.Protection.Locked.Should().BeFalse("Composition doit rester éditable");
    }
}
