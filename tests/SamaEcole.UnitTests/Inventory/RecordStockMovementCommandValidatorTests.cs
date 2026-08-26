using FluentAssertions;
using SamaEcole.Application.Inventory.Commands.RecordStockMovement;
using Xunit;

namespace SamaEcole.UnitTests.Inventory;

public class RecordStockMovementCommandValidatorTests
{
    private readonly RecordStockMovementCommandValidator _validator = new();

    private static RecordStockMovementCommand Valid() => new()
    {
        ItemId = Guid.NewGuid(),
        Type = StockMovementRequestType.Entree,
        Quantity = 12,
        Reason = "Dotation mairie 2026",
        RowVersion = 1
    };

    [Fact]
    public void Valid_Movement_Should_Pass()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Missing_Reason_Should_Fail()
    {
        // Le motif n'est pas décoratif : c'est lui qui rend le journal opposable lors d'un contrôle.
        _validator.Validate(Valid() with { Reason = "  " }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Zero_Quantity_Should_Fail_On_A_Regular_Movement()
    {
        _validator.Validate(Valid() with { Quantity = 0 }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Zero_Quantity_Should_Pass_On_An_Adjustment()
    {
        // Sur un ajustement, la quantité est l'effectif COMPTÉ : zéro signifie « je n'ai rien
        // retrouvé », un résultat d'inventaire physique parfaitement légitime.
        var command = Valid() with { Type = StockMovementRequestType.Ajustement, Quantity = 0 };

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Negative_Counted_Quantity_Should_Fail_On_An_Adjustment()
    {
        var command = Valid() with { Type = StockMovementRequestType.Ajustement, Quantity = -1 };

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Html_In_Reason_Should_Fail()
    {
        _validator.Validate(Valid() with { Reason = "<script>alert(1)</script>" }).IsValid.Should().BeFalse();
    }
}
