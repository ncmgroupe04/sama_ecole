using FluentAssertions;
using SamaEcole.Application.Users.Common;
using Xunit;

namespace SamaEcole.UnitTests.Users;

public class PasswordPolicyTests
{
    [Fact]
    public void A_Password_Meeting_Every_Rule_Should_Pass()
    {
        PasswordPolicy.Validate("Correct-Horse-9").Should().BeEmpty();
    }

    [Fact]
    public void Too_Short_Should_Fail()
    {
        PasswordPolicy.Validate("Ab1!ab1").Should().Contain(e => e.Contains("8 caractères"));
    }

    [Fact]
    public void Exactly_Eight_Characters_Should_Not_Trigger_The_Length_Rule()
    {
        PasswordPolicy.Validate("Ab1!ab1!").Should().NotContain(e => e.Contains("8 caractères"));
    }

    [Fact]
    public void Missing_Uppercase_Should_Fail()
    {
        PasswordPolicy.Validate("correct-horse-9").Should().Contain(e => e.Contains("majuscule"));
    }

    [Fact]
    public void Missing_Lowercase_Should_Fail()
    {
        PasswordPolicy.Validate("CORRECT-HORSE-9").Should().Contain(e => e.Contains("minuscule"));
    }

    [Fact]
    public void Missing_Digit_Should_Fail()
    {
        PasswordPolicy.Validate("Correct-Horse-Battery").Should().Contain(e => e.Contains("chiffre"));
    }

    [Fact]
    public void Missing_Special_Character_Should_Fail()
    {
        PasswordPolicy.Validate("CorrectHorseBattery9").Should().Contain(e => e.Contains("caractère spécial"));
    }

    [Theory]
    [InlineData("MotDePasse123456!")]
    [InlineData("QwertyStrongEnough1!")]
    public void An_Obvious_Sequence_Should_Fail(string password)
    {
        PasswordPolicy.Validate(password).Should().Contain(e => e.Contains("suite évidente"));
    }

    [Fact]
    public void Containing_The_Holders_Own_Name_Should_Fail()
    {
        PasswordPolicy.Validate("AwaNdiaye-2026!", "Awa Ndiaye").Should().Contain(e => e.Contains("nom"));
    }

    [Fact]
    public void A_Short_Personal_Term_Should_Not_Trigger_A_False_Positive()
    {
        // Un terme de moins de 3 caractères (ex. un prénom vide ou une initiale) ne doit pas
        // condamner des mots de passe légitimes qui le contiendraient par coïncidence.
        PasswordPolicy.Validate("Correct-Horse-9", "Al").Should().NotContain(e => e.Contains("nom"));
    }
}
