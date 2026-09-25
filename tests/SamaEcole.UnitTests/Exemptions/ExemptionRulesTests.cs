using FluentAssertions;
using SamaEcole.Application.Exemptions;
using Xunit;

namespace SamaEcole.UnitTests.Exemptions;

public class ExemptionRulesTests
{
    private static readonly Guid Eps = Guid.NewGuid();
    private static readonly Guid Maths = Guid.NewGuid();
    private static readonly IReadOnlyList<DispensableSubject> Dispensable = [new(Eps, "EPS"), new(Maths, "Mathématiques")];

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_Blank_Reason_Is_Refused(string? reason)
        => ExemptionRules.ValidateReason(reason).Should().NotBeNull();

    [Fact]
    public void A_Reason_Is_Valid_Up_To_Two_Hundred_Characters_After_Trimming()
    {
        ExemptionRules.ValidateReason("  Inaptitude médicale  ").Should().BeNull();
        ExemptionRules.ValidateReason(new string('x', 200)).Should().BeNull();
        ExemptionRules.ValidateReason(new string('x', 201)).Should().NotBeNull();
    }

    [Fact]
    public void A_Dispensable_Subject_With_A_Reason_Is_Valid()
        => ExemptionRules.Validate(Dispensable, [new(Eps, "Inaptitude médicale")]).Should().BeNull();

    [Fact]
    public void No_Exemption_At_All_Is_Valid()
        => ExemptionRules.Validate(Dispensable, []).Should().BeNull();

    [Fact]
    public void A_Missing_Reason_Is_Refused_And_Names_The_Subject()
        => ExemptionRules.Validate(Dispensable, [new(Eps, null)]).Should().NotBeNull().And.Contain("EPS");

    [Fact]
    public void A_Subject_That_Is_Not_Dispensable_Is_Refused()
        => ExemptionRules.Validate(Dispensable, [new(Guid.NewGuid(), "Raison")]).Should().NotBeNull();

    [Fact]
    public void The_Same_Subject_Cannot_Be_Exempted_Twice()
        => ExemptionRules.Validate(Dispensable, [new(Eps, "A"), new(Eps, "B")]).Should().NotBeNull();

    [Theory]
    [InlineData("Collège", " collège ", true)]
    [InlineData("Terminale S2", "terminale s2", true)]
    [InlineData("Collège", "Lycée", false)]
    public void Levels_Match_Ignoring_Case_And_Edge_Spaces(string a, string b, bool expected)
        => ExemptionRules.LevelMatches(a, b).Should().Be(expected);

    // Le motif peut être médical : un message d'erreur nomme la matière, JAMAIS le motif saisi.
    [Fact]
    public void An_Error_Message_Never_Echoes_The_Reason()
    {
        var reason = new string('x', 201) + " diabète";

        var message = ExemptionRules.Validate(Dispensable, [new(Eps, reason)]);

        message.Should().NotBeNull().And.NotContain("diabète");
    }
}
