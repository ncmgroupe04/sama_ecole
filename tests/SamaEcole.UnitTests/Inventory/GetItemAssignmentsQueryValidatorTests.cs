using FluentAssertions;
using SamaEcole.Application.Inventory.Queries.GetItemAssignments;
using Xunit;

namespace SamaEcole.UnitTests.Inventory;

public class GetItemAssignmentsQueryValidatorTests
{
    private readonly GetItemAssignmentsQueryValidator _validator = new();

    [Fact]
    public void Default_Query_Should_Pass()
    {
        _validator.Validate(new GetItemAssignmentsQuery()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void PageSize_Beyond_The_Cap_Should_Fail()
    {
        var query = new GetItemAssignmentsQuery { PageSize = GetItemAssignmentsQueryValidator.MaxPageSize + 1 };

        _validator.Validate(query).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_Positive_Page_Should_Fail(int page)
    {
        _validator.Validate(new GetItemAssignmentsQuery { Page = page }).IsValid.Should().BeFalse();
    }
}
