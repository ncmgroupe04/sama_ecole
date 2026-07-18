using FluentAssertions;
using SamaEcole.Application.Registration.Commands.SubmitRegistrationRequest;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Registration;

public class SubmitRegistrationRequestValidatorTests
{
    private readonly SubmitRegistrationRequestValidator _validator = new();

    private static SubmitRegistrationRequestCommand ValidCommand() => new()
    {
        DirectorFullName = "Awa Ndiaye",
        DirectorEmail = "awa@filaos.sn",
        DirectorPhone = "+221771234567",
        DirectorPassword = "Correct-Horse-9",
        SchoolName = "Complexe Les Baobabs",
        RequestedPlan = SubscriptionPlan.Standard
    };

    [Fact]
    public void Should_Succeed_When_All_Fields_Are_Valid()
    {
        var result = _validator.Validate(ValidCommand());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Should_Fail_When_Director_Email_Is_Invalid()
    {
        var command = ValidCommand() with { DirectorEmail = "pas-un-email" };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.DirectorEmail));
    }

    [Fact]
    public void Should_Fail_When_School_Name_Contains_Html()
    {
        // JGK-F01 — formulaire PUBLIC : le champ libre le plus exposé de toute l'application
        // (payloads exhaustifs : SafeTextValidationTests).
        var command = ValidCommand() with { SchoolName = "<script>alert(1)</script>" };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.SchoolName));
    }

    [Fact]
    public void Should_Fail_When_Password_Is_Too_Short()
    {
        // La politique partagée (PasswordPolicy) est déléguée : un seul cas suffit à prouver le
        // branchement, sa couverture complète (majuscule/chiffre/suite évidente/nom personnel…) est
        // déjà testée par PasswordPolicyTests.
        var command = ValidCommand() with { DirectorPassword = "Ab1!ab1!" };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("12 caractères"));
    }

    [Fact]
    public void Should_Fail_When_Password_Contains_The_Directors_Own_Name()
    {
        // Preuve que PasswordPolicy.Validate reçoit bien DirectorFullName ET SchoolName comme termes
        // personnels — pas seulement le premier.
        var command = ValidCommand() with { DirectorPassword = "AwaNdiaye-2026!" };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("nom"));
    }

    [Fact]
    public void Should_Fail_When_School_Name_Is_Empty()
    {
        var command = ValidCommand() with { SchoolName = "" };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.SchoolName));
    }

    [Fact]
    public void Should_Fail_When_Estimated_Student_Count_Is_Zero_Or_Negative()
    {
        var command = ValidCommand() with { EstimatedStudentCount = 0 };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.EstimatedStudentCount));
    }

    [Fact]
    public void Should_Succeed_When_Estimated_Student_Count_Is_Null()
    {
        var command = ValidCommand() with { EstimatedStudentCount = null };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }
}
