using FluentAssertions;
using SamaEcole.Application.Users.Commands.CreateUser;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Users;

public class CreateUserCommandValidatorTests
{
    private readonly CreateUserCommandValidator _validator = new();

    private static CreateUserCommand ValidCommand(Role role = Role.Secretariat) =>
        new("Fatou Diop", "fatou.diop@ecole.sn", "Correct-Horse-9", role);

    [Fact]
    public void A_Fully_Valid_Command_Should_Pass()
    {
        _validator.Validate(ValidCommand()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(Role.Secretariat)]
    [InlineData(Role.Finance)]
    [InlineData(Role.Enseignant)]
    public void Every_Assignable_Role_Should_Pass(Role role)
    {
        _validator.Validate(ValidCommand(role)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(Role.SuperAdmin)]
    [InlineData(Role.Directeur)]
    public void SuperAdmin_And_Directeur_Are_Not_Assignable_Here(Role role)
    {
        // SuperAdmin est un rôle plateforme ; Directeur ne se crée que via POST /schools (JGK-B01).
        // Permettre l'un ou l'autre ici serait une escalade de privilège non demandée.
        _validator.Validate(ValidCommand(role)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Missing_FullName_Should_Fail()
    {
        var command = ValidCommand() with { FullName = "" };
        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Invalid_Email_Should_Fail()
    {
        var command = ValidCommand() with { Email = "pas-un-email" };
        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_Weak_Password_Should_Fail()
    {
        var command = ValidCommand() with { Password = "short" };
        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_Password_Containing_The_Users_Own_Name_Should_Fail()
    {
        var command = ValidCommand() with { FullName = "Fatou Diop", Password = "FatouDiop-2026!" };
        _validator.Validate(command).IsValid.Should().BeFalse();
    }
}
