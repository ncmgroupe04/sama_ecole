using FluentAssertions;
using SamaEcole.Application.Grades;
using Xunit;

namespace SamaEcole.UnitTests.Grades;

/// <summary>
/// Formule officielle (docs/BACKLOG_TICKETS.md, harmonisation import/export Excel des notes) : la
/// moyenne de matière se calcule en DEUX étapes — 1) moyenne des devoirs (Devoir1/Devoir2), 2) moyenne
/// de ce résultat avec la Composition. Chaque étape tolère qu'une seule des deux valeurs soit présente
/// (la saisie progresse au fil du trimestre) : c'est le même comportement "moyenne de ce qui existe"
/// des deux côtés, testé une fois pour DevoirAverage et une fois pour SubjectAverage.
/// </summary>
public class GradeCalculatorTests
{
    [Fact]
    public void DevoirAverage_Of_Two_Present_Values_Is_Their_Simple_Average()
    {
        GradeCalculator.DevoirAverage(10, 14).Should().Be(12);
    }

    [Fact]
    public void DevoirAverage_With_Only_Devoir1_Returns_Devoir1()
    {
        GradeCalculator.DevoirAverage(10, null).Should().Be(10);
    }

    [Fact]
    public void DevoirAverage_With_Only_Devoir2_Returns_Devoir2()
    {
        GradeCalculator.DevoirAverage(null, 14).Should().Be(14);
    }

    [Fact]
    public void DevoirAverage_With_Neither_Value_Is_Null()
    {
        GradeCalculator.DevoirAverage(null, null).Should().BeNull();
    }

    [Fact]
    public void SubjectAverage_Averages_The_Devoir_Average_With_The_Composition()
    {
        // Devoir1=10, Devoir2=14 -> M_D=12 ; Composition=16 -> (12+16)/2 = 14.
        GradeCalculator.SubjectAverage(10, 14, 16).Should().Be(14);
    }

    [Fact]
    public void SubjectAverage_With_A_Single_Devoir_Still_Averages_With_The_Composition()
    {
        // Devoir1=10 seul -> M_D=10 ; Composition=16 -> (10+16)/2 = 13.
        GradeCalculator.SubjectAverage(10, null, 16).Should().Be(13);
    }

    [Fact]
    public void SubjectAverage_With_No_Composition_Returns_The_Devoir_Average()
    {
        GradeCalculator.SubjectAverage(10, 14, null).Should().Be(12);
    }

    [Fact]
    public void SubjectAverage_With_Only_A_Composition_Returns_The_Composition()
    {
        GradeCalculator.SubjectAverage(null, null, 16).Should().Be(16);
    }

    [Fact]
    public void SubjectAverage_With_Nothing_Saisi_Is_Null()
    {
        GradeCalculator.SubjectAverage(null, null, null).Should().BeNull();
    }

    [Fact]
    public void MentionFor_Returns_The_Strongest_Threshold_Reached()
    {
        var mentions = new (string Label, decimal MinAverage)[]
        {
            ("Excellent", 16m), ("Bien", 14m), ("Assez bien", 12m)
        };

        GradeCalculator.MentionFor(15m, mentions).Should().Be("Bien");
    }

    [Fact]
    public void MentionFor_Returns_Null_Below_Every_Threshold()
    {
        var mentions = new (string Label, decimal MinAverage)[] { ("Assez bien", 12m) };

        GradeCalculator.MentionFor(10m, mentions).Should().BeNull();
    }
}
