using FluentAssertions;
using SamaEcole.Application.Subscriptions.Queries.GetSubscriptionPayments;
using Xunit;

namespace SamaEcole.UnitTests.Subscriptions;

public class GetSubscriptionPaymentsQueryValidatorTests
{
    private readonly GetSubscriptionPaymentsQueryValidator _validator = new();

    private static GetSubscriptionPaymentsQuery Query(int page = 1, int pageSize = 20) =>
        new() { SchoolId = Guid.NewGuid(), Page = page, PageSize = pageSize };

    [Fact]
    public void Default_Query_Should_Pass()
    {
        _validator.Validate(Query()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_SchoolId_Should_Fail()
    {
        _validator.Validate(new GetSubscriptionPaymentsQuery { SchoolId = Guid.Empty }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void PageSize_Beyond_The_Cap_Should_Fail()
    {
        // Sans plafond, `?pageSize=1000000` laisse le client dicter la taille de la réponse.
        _validator.Validate(Query(pageSize: GetSubscriptionPaymentsQueryValidator.MaxPageSize + 1))
            .IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_Positive_Page_Should_Fail(int page)
    {
        _validator.Validate(Query(page: page)).IsValid.Should().BeFalse();
    }
}
