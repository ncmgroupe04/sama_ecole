using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Inventory.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Inventory;

/// <summary>
/// Arithmétique du grand livre de stock, testée SANS base : <see cref="StockLedger"/> ne persiste
/// rien, il mute une entité et renvoie la ligne de journal. C'est le seul endroit du module où les
/// deux compteurs d'un lot varient — s'il se trompe, tout le reste se trompe avec lui, y compris la
/// fiche d'inventaire présentée à la mairie.
///
/// Chaque test vérifie les DEUX compteurs, jamais un seul : les erreurs les plus coûteuses de ce
/// module sont celles où le total et le disponible divergent silencieusement (un prêt qui décrémente
/// le patrimoine, une perte qui décrémente deux fois).
/// </summary>
public class StockLedgerTests
{
    private static readonly DateOnly Today = new(2026, 9, 15);

    private static InventoryItem Lot(int total, int available) => new()
    {
        SchoolId = Guid.NewGuid(),
        Name = "Tables-bancs",
        CategoryId = Guid.NewGuid(),
        QuantityTotal = total,
        QuantityAvailable = available
    };

    private static StockMovement Apply(InventoryItem item, StockMovementType type, int quantity) =>
        StockLedger.Apply(item, type, quantity, Today, "Test");

    [Fact]
    public void Entree_Increases_Both_Counters()
    {
        var item = Lot(total: 100, available: 80);

        var movement = Apply(item, StockMovementType.Entree, 20);

        item.QuantityTotal.Should().Be(120);
        item.QuantityAvailable.Should().Be(100);
        movement.QuantityTotalAfter.Should().Be(120, "le journal fige l'état d'après, pour se relire sans rejeu");
        movement.QuantityAvailableAfter.Should().Be(100);
    }

    [Fact]
    public void Sortie_Decreases_Both_Counters()
    {
        var item = Lot(total: 100, available: 80);

        Apply(item, StockMovementType.Sortie, 30);

        item.QuantityTotal.Should().Be(70);
        item.QuantityAvailable.Should().Be(50);
    }

    [Fact]
    public void Attribution_Only_Reduces_Availability()
    {
        var item = Lot(total: 10, available: 10);

        Apply(item, StockMovementType.Attribution, 4);

        item.QuantityTotal.Should().Be(10, "un bien prêté reste au patrimoine de l'établissement");
        item.QuantityAvailable.Should().Be(6);
    }

    [Fact]
    public void Restitution_Only_Restores_Availability()
    {
        var item = Lot(total: 10, available: 6);

        Apply(item, StockMovementType.Restitution, 4);

        item.QuantityTotal.Should().Be(10);
        item.QuantityAvailable.Should().Be(10);
    }

    [Fact]
    public void MiseAuRebut_Removes_From_Patrimony_And_Availability()
    {
        var item = Lot(total: 10, available: 10);

        Apply(item, StockMovementType.MiseAuRebut, 3);

        item.QuantityTotal.Should().Be(7);
        item.QuantityAvailable.Should().Be(7);
    }

    [Fact]
    public void PerteSurPret_Removes_From_Patrimony_Without_Touching_Availability()
    {
        // 4 unités sont dehors : elles ne sont PAS dans le disponible. Les déclarer perdues doit
        // baisser le seul patrimoine — c'est toute la raison d'être de ce type de mouvement.
        var item = Lot(total: 10, available: 6);

        Apply(item, StockMovementType.PerteSurPret, 4);

        item.QuantityTotal.Should().Be(6);
        item.QuantityAvailable.Should().Be(6, "ces unités étaient déjà sorties du disponible par l'attribution");
    }

    [Theory]
    [InlineData(StockMovementType.Sortie)]
    [InlineData(StockMovementType.Attribution)]
    [InlineData(StockMovementType.MiseAuRebut)]
    [InlineData(StockMovementType.AjustementNegatif)]
    public void Taking_More_Than_Available_Is_Refused(StockMovementType type)
    {
        var item = Lot(total: 10, available: 3);

        var act = () => Apply(item, type, 4);

        // ValidationException (422) et non un conflit : la requête est refusée pour ce qu'elle
        // demande, pas parce qu'une écriture concurrente l'a doublée.
        act.Should().Throw<ValidationException>();

        item.QuantityAvailable.Should().Be(3, "un mouvement refusé ne doit rien avoir modifié");
        item.QuantityTotal.Should().Be(10);
    }

    [Fact]
    public void Returning_More_Than_Was_Lent_Is_Refused()
    {
        // Le disponible dépasserait le total : la contrainte CHECK le refuserait en 500, autant
        // l'expliquer en 422.
        var item = Lot(total: 10, available: 9);

        var act = () => Apply(item, StockMovementType.Restitution, 2);

        act.Should().Throw<ValidationException>();
        item.QuantityAvailable.Should().Be(9);
    }

    [Fact]
    public void PerteSurPret_Cannot_Sink_Total_Below_Availability()
    {
        // Rien n'est prêté : déclarer une perte sur prêt n'a pas de sens, et laisserait un lot dont
        // le disponible dépasse le total.
        var item = Lot(total: 5, available: 5);

        var act = () => Apply(item, StockMovementType.PerteSurPret, 1);

        act.Should().Throw<ValidationException>();
        item.QuantityTotal.Should().Be(5);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Non_Positive_Quantity_Is_Refused(int quantity)
    {
        var item = Lot(total: 10, available: 10);

        var act = () => Apply(item, StockMovementType.Entree, quantity);

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void Movement_Carries_The_Items_School_Not_A_Caller_Supplied_One()
    {
        // Le SchoolId d'une ligne de journal est recopié du lot, jamais fourni par l'appelant : c'est
        // ce qui garantit qu'un mouvement ne peut pas atterrir dans l'école d'à côté (règle #10).
        var item = Lot(total: 10, available: 10);

        var movement = Apply(item, StockMovementType.Entree, 1);

        movement.SchoolId.Should().Be(item.SchoolId);
        movement.ItemId.Should().Be(item.Id);
    }
}
