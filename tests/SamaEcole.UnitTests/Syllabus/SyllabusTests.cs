using FluentAssertions;
using SamaEcole.Application.Syllabus;
using Xunit;

namespace SamaEcole.UnitTests.Syllabus;

/// <summary>Évolution N°7 — calcul d'avancement du programme et trames nationales codées.</summary>
public class SyllabusTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();
    private static readonly Guid Etranger = Guid.NewGuid();

    [Fact]
    public void An_Empty_Programme_Has_No_Percentage()
        => SyllabusCoverage.Percent([], [A]).Should().BeNull("« — », jamais 0 % ni 100 % d'un programme qui n'existe pas");

    [Fact]
    public void Nothing_Covered_Is_Zero()
        => SyllabusCoverage.Percent([A, B, C], []).Should().Be(0m);

    [Fact]
    public void Duplicates_And_Foreign_Units_Do_Not_Count()
        => SyllabusCoverage.Percent([A, B, C], [A, A, Etranger]).Should().Be(33.3m);

    [Fact]
    public void Full_Coverage_Is_One_Hundred()
        => SyllabusCoverage.Percent([A, B, C], [C, B, A]).Should().Be(100m);

    [Fact]
    public void Percentages_Round_Half_Away_From_Zero()
        => SyllabusCoverage.Percent([A, B, C, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()], [A])
            .Should().Be(12.5m);

    [Theory]
    [InlineData("Mathématiques")]
    [InlineData("MATHEMATIQUES")]
    [InlineData("Maths")]
    [InlineData("math")]
    public void The_Troisieme_Maths_Template_Is_Found_Under_Common_Names(string subjectName)
        => SyllabusTemplates.For(subjectName, "Troisième").Should().NotBeNull();

    [Theory]
    [InlineData("Mathématiques", "Seconde")]
    [InlineData("Français", "Troisième")]
    [InlineData("", "Troisième")]
    [InlineData("Mathématiques", null)]
    public void No_Template_Is_Invented(string subjectName, string? grade)
        => SyllabusTemplates.For(subjectName, grade).Should().BeNull();

    [Fact]
    public void Every_Template_Is_Well_Formed()
    {
        foreach (var template in SyllabusTemplates.All)
        {
            template.Units.Should().NotBeEmpty();
            template.Units.Select(u => u.Title).Should().OnlyHaveUniqueItems($"{template.Subject} {template.GradeLevel}");
            template.Units.Should().OnlyContain(u => u.Title.Length <= 200 && (u.Section == null || u.Section.Length <= 120));
        }
    }
}
