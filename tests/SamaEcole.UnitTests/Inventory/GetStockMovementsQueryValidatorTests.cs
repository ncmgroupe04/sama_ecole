using FluentAssertions;
using SamaEcole.Application.Inventory.Queries.GetStockMovements;
using Xunit;

namespace SamaEcole.UnitTests.Inventory;

public class GetStockMovementsQueryValidatorTests
{
    private readonly GetStockMovementsQueryValidator _validator = new();

    [Fact]
    public void Default_Query_Should_Pass()
    {
        _validator.Validate(new GetStockMovementsQuery()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void PageSize_Beyond_The_Cap_Should_Fail()
    {
        var query = new GetStockMovementsQuery { PageSize = GetStockMovementsQueryValidator.MaxPageSize + 1 };

        _validator.Validate(query).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_Positive_Page_Should_Fail(int page)
    {
        _validator.Validate(new GetStockMovementsQuery { Page = page }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void To_Before_From_Should_Fail()
    {
        var query = new GetStockMovementsQuery { From = new DateOnly(2026, 9, 15), To = new DateOnly(2026, 9, 1) };

        _validator.Validate(query).IsValid.Should().BeFalse();
    }
}
