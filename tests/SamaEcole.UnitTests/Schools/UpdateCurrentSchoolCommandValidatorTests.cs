using FluentAssertions;
using SamaEcole.Application.Schools.Commands.UpdateCurrentSchool;
using Xunit;

namespace SamaEcole.UnitTests.Schools;

/// <summary>
/// Configuration de l'établissement par le Directeur : le nom est obligatoire (il figure sur le reçu),
/// le reste est facultatif, et le logo — s'il est fourni — doit être une URL http(s).
/// </summary>
public class UpdateCurrentSchoolCommandValidatorTests
{
    private readonly UpdateCurrentSchoolCommandValidator _validator = new();

    private static UpdateCurrentSchoolCommand Valid() =>
        new("École Les Baobabs", "Rue 12, Médina, Dakar", "+221 77 123 45 67", "https://ecole.sn/logo.png");

    [Fact]
    public void A_Complete_Profile_Should_Pass()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_Name_Only_Profile_Should_Pass()
    {
        var command = new UpdateCurrentSchoolCommand("École Les Baobabs", null, null, null);

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_Missing_Name_Should_Fail(string name)
    {
        var command = Valid() with { Name = name };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.Name));
    }

    [Fact]
    public void A_Too_Long_Name_Should_Fail()
    {
        var command = Valid() with { Name = new string('A', 201) };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.Name));
    }

    [Theory]
    [InlineData("pas-une-url")]
    [InlineData("ftp://ecole.sn/logo.png")]
    [InlineData("javascript:alert(1)")]
    public void A_Non_Http_Logo_Url_Should_Fail(string logoUrl)
    {
        var command = Valid() with { LogoUrl = logoUrl };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.LogoUrl));
    }

    [Theory]
    [InlineData("http://ecole.sn/logo.png")]
    [InlineData("https://ecole.sn/logo.png")]
    [InlineData(null)]
    [InlineData("")]
    public void A_Valid_Or_Empty_Logo_Url_Should_Pass(string? logoUrl)
    {
        var command = Valid() with { LogoUrl = logoUrl };

        _validator.Validate(command).IsValid.Should().BeTrue();
    }
}
