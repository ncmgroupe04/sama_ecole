using ClosedXML.Excel;
using FluentAssertions;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SamaEcole.Application.Classrooms;
using SamaEcole.Application.Institutional;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Documents;
using SamaEcole.Infrastructure.Files;
using Xunit;

namespace SamaEcole.UnitTests.Institutional;

/// <summary>Évolution N°7 — normes d'âge du Ministère, statut IEF de l'élève, colonnes d'âge et exports du rapport de rentrée.</summary>
public class AgeNormsTests
{
    static AgeNormsTests() => QuestPDF.Settings.License = LicenseType.Community;

    [Theory]
    [InlineData("CI", 5, 8)]
    [InlineData("CM2", 10, 13)]
    [InlineData("Sixième", 11, 14)]
    [InlineData("Troisième", 14, 17)]
    [InlineData("Terminale", 17, 20)]
    public void The_National_Template_Is_Normal_Age_Minus_One_Plus_Two(string grade, int min, int max)
        => AgeNormTemplates.For(grade).Should().BeEquivalentTo(new { GradeLevel = grade, MinAge = min, MaxAge = max });

    [Fact]
    public void Every_Grade_Of_The_Classroom_Nomenclature_Has_A_Norm()
    {
        foreach (var cycle in new[] { CycleType.Maternelle, CycleType.Primaire, CycleType.College, CycleType.Lycee })
        {
            foreach (var grade in ClassroomGradeLevels.For(cycle))
            {
                AgeNormTemplates.For(grade).Should().NotBeNull($"le niveau {grade} doit avoir une tranche d'âge");
            }
        }
    }

    [Fact]
    public void Ages_Are_Counted_On_The_31st_Of_December_Of_The_Start_Year()
    {
        var reference = AgeRules.ReferenceDate(new DateOnly(2026, 10, 5));

        reference.Should().Be(new DateOnly(2026, 12, 31));
        AgeRules.AgeAt(new DateOnly(2020, 12, 31), reference).Should().Be(6);
        AgeRules.AgeAt(new DateOnly(2021, 1, 1), reference).Should().Be(5);
        AgeRules.AgeAt(new DateOnly(2030, 1, 1), reference).Should().BeNull("date de naissance future");
        AgeRules.AgeAt(new DateOnly(1950, 1, 1), reference).Should().BeNull("âge invraisemblable pour un élève");
    }

    [Theory]
    [InlineData(4, AgeNormStatus.Early)]
    [InlineData(5, AgeNormStatus.Normal)]
    [InlineData(8, AgeNormStatus.Normal)]
    [InlineData(9, AgeNormStatus.Late)]
    [InlineData(null, AgeNormStatus.Unknown)]
    public void An_Age_Is_Classified_Against_The_Grade_Norm(int? age, AgeNormStatus expected)
        => AgeRules.Classify(age, AgeNormTemplates.For("CI")).Should().Be(expected);

    [Fact]
    public void Without_A_Known_Grade_Nothing_Is_Judged()
        => AgeRules.Classify(12, null).Should().Be(AgeNormStatus.Unknown);

    [Theory]
    [InlineData("CM2 A", CycleType.Primaire, "CM2")]
    [InlineData("6e B", CycleType.College, "Sixième")]
    [InlineData("Terminale S2", CycleType.Lycee, "Terminale")]
    [InlineData("Classe Arc-en-ciel", CycleType.Primaire, null)]
    public void The_Grade_Is_Read_On_The_Classroom_Name(string name, CycleType cycle, string? expected)
        => AgeRules.GradeOf(name, cycle).Should().Be(expected);

    [Theory]
    [InlineData(true, true, StudentEntryStatus.Repeater)]
    [InlineData(false, true, StudentEntryStatus.Transferred)]
    [InlineData(false, false, StudentEntryStatus.New)]
    public void The_Entry_Status_Puts_Repeating_First(bool repeating, bool transferred, StudentEntryStatus expected)
        => StudentEntryStatuses.Of(repeating, transferred).Should().Be(expected);

    [Fact]
    public void Age_Columns_Are_One_Per_Age_When_They_Fit()
        => AgeBuckets.Build([12, 14, 13, null, 12]).Select(b => b.Label).Should().Equal("12 ans", "13 ans", "14 ans");

    [Fact]
    public void Extreme_Ages_Are_Grouped_To_Fit_The_Page()
    {
        var ages = Enumerable.Range(3, 20).Select(a => (int?)a).ToList(); // 3 à 22 ans
        var buckets = AgeBuckets.Build(ages, maxColumns: 12);

        buckets.Should().HaveCount(12);
        buckets[0].From.Should().BeNull();
        buckets[^1].To.Should().BeNull();
        foreach (var age in ages)
        {
            buckets.Count(b => b.Contains(age)).Should().Be(1, $"l'âge {age} tombe dans une et une seule colonne");
        }
    }

    [Fact]
    public void No_Known_Age_Means_No_Column()
        => AgeBuckets.Build([null]).Should().BeEmpty();

    private static IefReportDto SampleReport()
    {
        var columns = AgeBuckets.Build([11, 12, 13]);
        return new IefReportDto(
            "CEM de test", "SN-001", "IA Dakar", "IEF Dakar Plateau", "2026-2027", new DateOnly(2026, 12, 31),
            DateTimeOffset.UtcNow, columns,
            [
                new IefClassRow("6e A", "Sixième", CycleType.College, 2, 1, 3, 2, 1, 0, 0, 1, "11–14 ans",
                    [new IefAgeCell(1, 0), new IefAgeCell(1, 1), new IefAgeCell(0, 0)], 0, 0)
            ],
            [new IefRepetitionRow("Sixième", 3, 1, 0, 1)],
            [new IefDisciplineRow("Mathématiques", 1, 1, 0, 5)],
            [new IefDiplomaRow(AcademicQualification.NonRenseigne, ProfessionalQualification.CAEM, 1, 0, 0)],
            [new IefTeacherRow("Moussa Diop", "M", ["Mathématiques"], AcademicQualification.NonRenseigne, ProfessionalQualification.CAEM, 5)]);
    }

    [Fact]
    public void The_Repetition_Rate_Is_A_Percentage_Of_The_Level()
        => SampleReport().RepetitionByLevel[0].RepetitionRate.Should().Be(33.3m);

    [Fact]
    public void The_Pdf_Report_Renders()
        => System.Text.Encoding.ASCII.GetString(new IefReportDocument(SampleReport()).GeneratePdf(), 0, 5).Should().Be("%PDF-");

    [Fact]
    public void The_Excel_Report_Has_One_Sheet_Per_Table_With_Raw_Numbers()
    {
        using var stream = new MemoryStream(new IefReportExcelGenerator().Generate(SampleReport()));
        using var workbook = new XLWorkbook(stream);

        workbook.Worksheets.Select(w => w.Name).Should().Equal("Effectifs par classe", "Redoublement", "Corps professoral");
        var classes = workbook.Worksheet("Effectifs par classe");
        classes.Cell(5, 1).GetString().Should().Be("6e A");
        classes.Cell(5, 4).GetValue<int>().Should().Be(1, "11 ans, filles");
    }
}
