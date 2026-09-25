using FluentAssertions;
using SamaEcole.Application.OptionalSubjects;
using Xunit;

namespace SamaEcole.UnitTests.OptionalSubjects;

public class OptionSelectionRulesTests
{
    private static readonly Guid Espagnol = Guid.NewGuid();
    private static readonly Guid Arabe = Guid.NewGuid();
    private static readonly Guid Allemand = Guid.NewGuid();
    private static readonly Guid Pc = Guid.NewGuid();
    private static readonly Guid Svt = Guid.NewGuid();
    private static readonly Guid Dessin = Guid.NewGuid();

    private static readonly IReadOnlyList<LevelSubject> Options =
    [
        new(Espagnol, "Espagnol", "LV2"),
        new(Arabe, "Arabe", " lv2 "),
        new(Allemand, "Allemand", "LV2"),
        new(Pc, "PC", "Option scientifique"),
        new(Svt, "SVT", "Option scientifique"),
        new(Dessin, "Dessin", null)
    ];

    [Fact]
    public void One_Choice_Per_Group_Is_Valid_And_The_Others_Are_Exempted()
    {
        OptionSelectionRules.Validate(Options, [Espagnol, Pc]).Should().BeNull();

        OptionSelectionRules.ExemptedSubjectIds(Options, [Espagnol, Pc])
            .Should().BeEquivalentTo([Arabe, Allemand, Svt, Dessin]);
    }

    [Fact]
    public void Two_Choices_In_The_Same_Group_Are_Refused_Whatever_The_Case_And_Spaces_Of_The_Group()
    {
        var message = OptionSelectionRules.Validate(Options, [Espagnol, Arabe]);

        message.Should().NotBeNull().And.Contain("LV2");
    }

    [Fact]
    public void Ungrouped_Options_Can_Be_Combined_And_An_Empty_Choice_Exempts_Everything()
    {
        OptionSelectionRules.Validate(Options, [Dessin]).Should().BeNull();

        OptionSelectionRules.Validate(Options, []).Should().BeNull();
        OptionSelectionRules.ExemptedSubjectIds(Options, []).Should().HaveCount(Options.Count);
    }

    [Fact]
    public void A_Subject_Outside_The_Level_Options_Is_Refused()
        => OptionSelectionRules.Validate(Options, [Guid.NewGuid()]).Should().NotBeNull();

    [Fact]
    public void A_Duplicate_Id_Is_Not_Counted_Twice()
        => OptionSelectionRules.Validate(Options, [Espagnol, Espagnol]).Should().BeNull();

    [Theory]
    [InlineData("Collège", " collège ", true)]
    [InlineData("Terminale S2", "terminale s2", true)]
    [InlineData("Collège", "Lycée", false)]
    public void Levels_Match_Ignoring_Case_And_Edge_Spaces(string a, string b, bool expected)
        => OptionSelectionRules.LevelMatches(a, b).Should().Be(expected);

    // ---- Dispense d'une matière obligatoire : le motif est imposé -------------------------------------

    private static readonly Guid Eps = Guid.NewGuid();
    private static readonly Guid Maths = Guid.NewGuid();
    private static readonly IReadOnlyList<LevelSubject> Mandatory = [new(Eps, "EPS", null), new(Maths, "Maths", null)];

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_Blank_Reason_Is_Refused(string? reason)
        => OptionSelectionRules.ValidateReason(reason).Should().NotBeNull();

    [Fact]
    public void A_Reason_Is_Valid_Up_To_Two_Hundred_Characters_After_Trimming()
    {
        OptionSelectionRules.ValidateReason("  Inaptitude médicale  ").Should().BeNull();
        OptionSelectionRules.ValidateReason(new string('x', 200)).Should().BeNull();
        OptionSelectionRules.ValidateReason(new string('x', 201)).Should().NotBeNull();
    }

    [Fact]
    public void A_Mandatory_Exemption_With_A_Reason_Is_Valid()
        => OptionSelectionRules.ValidateMandatoryExemptions(Mandatory, [new(Eps, "Inaptitude médicale")]).Should().BeNull();

    [Fact]
    public void A_Mandatory_Exemption_Without_A_Reason_Is_Refused_And_Names_The_Subject()
    {
        var message = OptionSelectionRules.ValidateMandatoryExemptions(Mandatory, [new(Eps, null)]);

        message.Should().NotBeNull().And.Contain("EPS");
    }

    [Fact]
    public void An_Option_Cannot_Be_Passed_As_A_Mandatory_Exemption()
        => OptionSelectionRules.ValidateMandatoryExemptions(Mandatory, [new(Espagnol, "Raison")]).Should().NotBeNull();

    [Fact]
    public void The_Same_Subject_Cannot_Be_Exempted_Twice()
        => OptionSelectionRules.ValidateMandatoryExemptions(Mandatory, [new(Eps, "A"), new(Eps, "B")]).Should().NotBeNull();

    [Fact]
    public void No_Mandatory_Exemption_At_All_Is_Valid()
        => OptionSelectionRules.ValidateMandatoryExemptions(Mandatory, []).Should().BeNull();
}
