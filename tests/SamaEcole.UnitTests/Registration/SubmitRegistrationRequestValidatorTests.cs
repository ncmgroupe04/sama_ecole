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
        Ownership = SchoolOwnership.Private,
        CycleProfile = SchoolCycleProfile.Primaire,
        SizeTier = SchoolSizeTier.Small
    };

    [Fact]
    public void Should_Succeed_When_All_Fields_Are_Valid()
    {
        var result = _validator.Validate(ValidCommand());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Should_Fail_When_The_School_Type_Is_Missing()
    {
        var command = ValidCommand() with { Ownership = null };

        _validator.Validate(command).Errors.Should().Contain(e => e.PropertyName == nameof(command.Ownership));
    }

    [Fact]
    public void Should_Fail_When_The_Cycles_Are_Missing()
    {
        var command = ValidCommand() with { CycleProfile = null };

        _validator.Validate(command).Errors.Should().Contain(e => e.PropertyName == nameof(command.CycleProfile));
    }

    [Fact]
    public void A_Private_School_Must_Give_Its_Size()
    {
        var command = ValidCommand() with { SizeTier = null };

        _validator.Validate(command).Errors.Should().Contain(e => e.PropertyName == nameof(command.SizeTier));
    }

    [Theory]
    [InlineData(SchoolCycleProfile.Primaire)]
    [InlineData(SchoolCycleProfile.College)]
    [InlineData(SchoolCycleProfile.Lycee)]
    public void A_Public_School_With_One_Cycle_And_No_Size_Is_Valid(SchoolCycleProfile profile)
    {
        var command = ValidCommand() with { Ownership = SchoolOwnership.Public, CycleProfile = profile, SizeTier = null };

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(SchoolCycleProfile.Bicycle)]
    [InlineData(SchoolCycleProfile.Complexe)]
    public void A_Public_School_Cannot_Be_A_Bicycle_Or_A_Complex(SchoolCycleProfile profile)
    {
        var command = ValidCommand() with { Ownership = SchoolOwnership.Public, CycleProfile = profile, SizeTier = null };

        _validator.Validate(command).Errors.Should().Contain(e => e.PropertyName == nameof(command.CycleProfile));
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
        var command = ValidCommand() with { DirectorPassword = "Ab1!ab1" };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("8 caractères"));
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
    public void Should_Fail_When_Director_Phone_Is_Not_A_Valid_Senegal_Number()
    {
        var command = ValidCommand() with { DirectorPhone = "0123456789" };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.DirectorPhone));
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
