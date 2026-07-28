using System.Text;
using FluentAssertions;
using SamaEcole.Application.Reports;
using SamaEcole.Application.Reports.Queries.GetAttendanceReport;
using Xunit;

namespace SamaEcole.UnitTests.Reports;

public class AttendanceReportCsvTests
{
    private static AttendanceReportExportModel Model(params StudentAttendanceReportRow[] rows) => new(
        SchoolName: "École de test",
        StartDate: new DateOnly(2026, 7, 1),
        EndDate: new DateOnly(2026, 7, 31),
        ClassName: "CM2 A",
        AverageAttendanceRate: 0.6667m,
        Students: rows);

    private static StudentAttendanceReportRow Row(
        string matricule, string name, int calls, int present, int late, int justified, int unjustified, int lateMin, decimal rate)
        => new(Guid.NewGuid(), matricule, name, Guid.NewGuid(), "CM2 A", calls, present, late, justified, unjustified, lateMin, rate);

    private static string Decode(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    [Fact]
    public void Csv_Should_Start_With_Utf8_Bom()
    {
        var bytes = AttendanceReportCsv.Build(Model());

        var preamble = Encoding.UTF8.GetPreamble();
        bytes.Take(preamble.Length).Should().Equal(preamble, "Excel FR a besoin du BOM pour afficher les accents");
    }

    [Fact]
    public void Csv_Should_Contain_Context_And_Column_Header()
    {
        var text = Decode(AttendanceReportCsv.Build(Model()));

        text.Should().Contain("Rapport d'assiduité");
        text.Should().Contain("Période;01/07/2026 au 31/07/2026");
        text.Should().Contain("Classe;CM2 A");
        text.Should().Contain("Matricule;Nom;Classe;Appels;Présents;Retards;Minutes de retard;Absences justifiées;Absences non justifiées;Taux de présence (%)");
    }

    [Fact]
    public void Csv_Should_Render_A_Student_Row_With_Percent_And_Comma_Decimal()
    {
        var text = Decode(AttendanceReportCsv.Build(Model(
            Row("ELEV-2026-0002", "Modou Diop", calls: 3, present: 1, late: 0, justified: 1, unjustified: 1, lateMin: 0, rate: 0.3333m))));

        // 0,3333 → 33,33 %. Décimale virgule (FR), pas de suffixe « % » dans la colonne numérique.
        text.Should().Contain("ELEV-2026-0002;Modou Diop;CM2 A;3;1;0;0;1;1;33,33");
    }

    [Fact]
    public void A_Name_Containing_The_Separator_Should_Be_Quoted()
    {
        var text = Decode(AttendanceReportCsv.Build(Model(
            Row("ELEV-2026-0009", "Diop; Awa", 1, 1, 0, 0, 0, 0, 1.0m))));

        // Le point-virgule dans le nom ne doit pas casser les colonnes : le champ est encadré de guillemets.
        text.Should().Contain("\"Diop; Awa\"");
    }

    [Fact]
    public void Integer_Rate_Should_Drop_Useless_Decimals()
    {
        var text = Decode(AttendanceReportCsv.Build(Model(
            Row("ELEV-2026-0001", "Awa Fall", 3, 2, 1, 0, 0, 5, 1.0m))));

        // 1,0 → « 100 » (pas « 100,00 »).
        text.Should().Contain("ELEV-2026-0001;Awa Fall;CM2 A;3;2;1;5;0;0;100");
    }
}
