using SamaEcole.Application.Auth.Commands.ChangeEmail;
using FluentAssertions;
using Xunit;

namespace SamaEcole.UnitTests.Auth;

public class ChangeUserEmailCommandValidatorTests
{
    private readonly ChangeUserEmailCommandValidator _validator = new();

    [Fact]
    public void A_Well_Formed_Request_Should_Be_Valid()
    {
        var result = _validator.Validate(new ChangeUserEmailCommand("nouveau@sama-ecole.sn", "Mot-De-Passe-9!"));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("pas-un-email")]
    [InlineData("pas-un-email@")]
    public void An_Invalid_Email_Should_Be_Rejected(string email)
    {
        var result = _validator.Validate(new ChangeUserEmailCommand(email, "Mot-De-Passe-9!"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ChangeUserEmailCommand.NewEmail));
    }

    [Fact]
    public void A_Missing_Current_Password_Should_Be_Rejected()
    {
        var result = _validator.Validate(new ChangeUserEmailCommand("nouveau@sama-ecole.sn", ""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ChangeUserEmailCommand.CurrentPassword));
    }

    /// <summary>
    /// Le validateur ne connaît que la FORME de l'e-mail : le HTML éventuel (balise, script) est
    /// refusé par NoHtml, la même garde que CreateUserCommandValidator applique déjà à l'e-mail d'un
    /// compte créé par un Directeur.
    /// </summary>
    [Fact]
    public void An_Email_Containing_Html_Should_Be_Rejected()
    {
        var result = _validator.Validate(new ChangeUserEmailCommand("<script>@sama-ecole.sn", "Mot-De-Passe-9!"));

        result.IsValid.Should().BeFalse();
    }
}
