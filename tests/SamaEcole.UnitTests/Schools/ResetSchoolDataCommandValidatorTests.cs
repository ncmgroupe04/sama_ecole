using FluentAssertions;
using SamaEcole.Application.Schools.Commands.ResetSchoolData;
using Xunit;

namespace SamaEcole.UnitTests.Schools;

/// <summary>
/// Le validateur n'exige que la PRÉSENCE de la confirmation : sa valeur attendue dépend du nom de
/// l'établissement, que seul le Handler peut lire (voir ResetSchoolDataCommandValidator).
/// </summary>
public class ResetSchoolDataCommandValidatorTests
{
    private readonly ResetSchoolDataCommandValidator _validator = new();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_Empty_Confirmation_Should_Be_Rejected(string confirmation)
        => _validator.Validate(new ResetSchoolDataCommand(confirmation)).IsValid.Should().BeFalse();

    [Fact]
    public void A_Filled_Confirmation_Should_Pass_The_Validator()
        => _validator.Validate(new ResetSchoolDataCommand(ResetSchoolDataConfirmation.Keyword))
            .IsValid.Should().BeTrue();

    [Fact]
    public void A_Wrong_Word_Should_Pass_The_Validator_And_Be_Refused_By_The_Handler()
        => _validator.Validate(new ResetSchoolDataCommand("n'importe quoi"))
            .IsValid.Should().BeTrue(
                "c'est le Handler, seul à connaître le nom de l'école, qui tranche la valeur exacte");
}
