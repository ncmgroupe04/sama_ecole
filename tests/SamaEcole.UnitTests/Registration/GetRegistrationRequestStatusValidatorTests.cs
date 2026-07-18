using FluentAssertions;
using SamaEcole.Application.Registration.Queries.GetRegistrationRequestStatus;
using Xunit;

namespace SamaEcole.UnitTests.Registration;

public class GetRegistrationRequestStatusValidatorTests
{
    private readonly GetRegistrationRequestStatusValidator _validator = new();

    [Fact]
    public void Should_Succeed_For_A_Well_Formed_Reference()
    {
        var result = _validator.Validate(new GetRegistrationRequestStatusQuery("REG-7QK3M9PZ"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Should_Fail_When_Reference_Is_Empty()
    {
        var result = _validator.Validate(new GetRegistrationRequestStatusQuery(""));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Should_Fail_When_Reference_Exceeds_Column_Length()
    {
        var result = _validator.Validate(new GetRegistrationRequestStatusQuery(new string('A', 21)));

        result.IsValid.Should().BeFalse();
    }
}
