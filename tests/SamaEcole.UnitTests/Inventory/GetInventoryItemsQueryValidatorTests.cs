using FluentAssertions;
using SamaEcole.Application.Inventory.Queries.GetInventoryItems;
using Xunit;

namespace SamaEcole.UnitTests.Inventory;

public class GetInventoryItemsQueryValidatorTests
{
    private readonly GetInventoryItemsQueryValidator _validator = new();

    [Fact]
    public void Default_Query_Should_Pass()
    {
        _validator.Validate(new GetInventoryItemsQuery()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void PageSize_Beyond_The_Cap_Should_Fail()
    {
        // Un client (ici les sélecteurs de biens du front, wwwroot/js/inventory.js) ne doit jamais
        // pouvoir demander une page plus grande que ce plafond en une seule requête.
        var query = new GetInventoryItemsQuery { PageSize = GetInventoryItemsQueryValidator.MaxPageSize + 1 };

        _validator.Validate(query).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_Positive_Page_Should_Fail(int page)
    {
        _validator.Validate(new GetInventoryItemsQuery { Page = page }).IsValid.Should().BeFalse();
    }
}
