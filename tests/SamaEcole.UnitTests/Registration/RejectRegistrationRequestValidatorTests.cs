using FluentAssertions;
using SamaEcole.Application.Registration.Commands.RejectRegistrationRequest;
using Xunit;

namespace SamaEcole.UnitTests.Registration;

public class RejectRegistrationRequestValidatorTests
{
    private readonly RejectRegistrationRequestValidator _validator = new();

    [Fact]
    public void Should_Fail_When_Reason_Is_Empty()
    {
        // Critère du ticket : le motif de rejet est obligatoire.
        var result = _validator.Validate(new RejectRegistrationRequestCommand(Guid.NewGuid(), ""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("obligatoire"));
    }

    [Fact]
    public void Should_Fail_When_Reason_Is_Whitespace()
    {
        var result = _validator.Validate(new RejectRegistrationRequestCommand(Guid.NewGuid(), "   "));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Should_Fail_When_Reason_Exceeds_Max_Length()
    {
        var result = _validator.Validate(
            new RejectRegistrationRequestCommand(Guid.NewGuid(), new string('x', 1001)));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Should_Succeed_With_A_Concrete_Reason()
    {
        var result = _validator.Validate(
            new RejectRegistrationRequestCommand(Guid.NewGuid(), "Informations incomplètes."));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Should_Fail_When_Reason_Contains_Html()
    {
        // JGK-F01 — le motif est réaffiché tel quel au candidat (suivi public JGK-I02) : champ libre
        // le plus exposé, il DOIT être branché sur NoHtml (payloads exhaustifs : SafeTextValidationTests).
        var result = _validator.Validate(
            new RejectRegistrationRequestCommand(Guid.NewGuid(), "<script>alert(1)</script>"));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Should_Succeed_When_Reason_Mentions_Inscription()
    {
        // « inscription » contient la sous-chaîne « script » : la règle ne bloque pas le mot, sinon le
        // vocabulaire même de l'application deviendrait insaisissable.
        var result = _validator.Validate(
            new RejectRegistrationRequestCommand(Guid.NewGuid(), "Votre demande d'inscription est incomplète."));

        result.IsValid.Should().BeTrue();
    }
}
