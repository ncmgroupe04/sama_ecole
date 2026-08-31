using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SamaEcole.Application.Students;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.Students;

/// <summary>
/// Non-régression du moteur PDF (QuestPDF) de l'export tabulaire des élèves
/// (GET /students/export/pdf). Ce générateur n'avait aucun test dédié : on couvre le PDF valide et
/// non trivial, la liste vide (branche « Aucun élève pour ce périmètre »), les champs tuteur
/// absents, et un identifiant/nom d'école dégénéré qui ne doit jamais faire échouer l'émission.
/// </summary>
public class StudentsExportPdfGeneratorTests
{
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private static StudentsExportPdfGenerator NewGenerator() =>
        new(Mock.Of<ILogger<StudentsExportPdfGenerator>>());

    private static StudentExportRow Row(
        string matricule = "ELEV-2025-0001",
        string fullName = "Awa Fall",
        string gender = "F",
        string classroomName = "CM2 A",
        string? guardianName = "Moussa Fall",
        string? guardianPhone = "+221771234567") =>
        new(matricule, fullName, new DateOnly(2015, 3, 12), gender, classroomName, guardianName, guardianPhone);

    private static StudentsExportModel Model(params StudentExportRow[] rows) =>
        new("Complexe Scolaire Touba Darou Karim", "CM2 A", "2026-2027", rows);

    private static void ShouldBeAValidPdf(byte[] pdf)
    {
        pdf.Should().NotBeNullOrEmpty();
        Encoding.ASCII.GetString(pdf, 0, PdfMagic.Length).Should().Be("%PDF-", "l'en-tête magique d'un PDF");
    }

    [Fact]
    public void Generate_Produces_A_Valid_Non_Trivial_Pdf()
    {
        var pdf = NewGenerator().Generate(Model(Row(), Row(matricule: "ELEV-2025-0002", fullName: "Mamadou Sow", gender: "M")));

        ShouldBeAValidPdf(pdf);
        pdf.Length.Should().BeGreaterThan(1000);
    }

    [Fact]
    public void Generate_Is_Robust_To_An_Empty_Roster()
    {
        // Branche `model.Students.Count == 0` du document : « Aucun élève pour ce périmètre ».
        var pdf = NewGenerator().Generate(new StudentsExportModel("École X", null, null, []));

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Is_Robust_To_Missing_Guardian_Fields()
    {
        var pdf = NewGenerator().Generate(Model(Row(guardianName: null, guardianPhone: null)));

        ShouldBeAValidPdf(pdf);
    }

    [Fact]
    public void Generate_Does_Not_Crash_On_A_Degenerate_Identifier_Or_School_Name()
    {
        // NoBreakText.NoBreak(null) et SchoolName null ne doivent jamais faire échouer tout l'export.
        var model = new StudentsExportModel(null!, null, null, [Row(matricule: null!)]);

        var act = () => NewGenerator().Generate(model);

        act.Should().NotThrow();
        ShouldBeAValidPdf(NewGenerator().Generate(model));
    }
}
