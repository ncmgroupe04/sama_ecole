using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Inventory.Commands.CancelItemAssignment;
using SamaEcole.Application.Inventory.Commands.CreateItemAssignment;
using SamaEcole.Application.Inventory.Commands.DeleteInventoryItem;
using SamaEcole.Application.Inventory.Commands.RecordStockMovement;
using SamaEcole.Application.Inventory.Commands.ReturnItemAssignment;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Inventory;

/// <summary>
/// Règles de stock du module Inventaire, sur PostgreSQL réel et sous le rôle applicatif bridé.
///
/// Ce que ces tests protègent, et qu'aucun test unitaire ne peut couvrir : le verrou optimiste xmin
/// (il faut deux contextes et une vraie ligne), les contraintes CHECK (elles n'existent qu'en base),
/// et l'enchaînement Handler + journal + compteurs dans une seule transaction. Le calcul pur, lui,
/// est couvert par StockLedgerTests côté unitaire — inutile de le rejouer ici.
/// </summary>
[Trait("Category", "MultiTenant")]
public class InventoryStockRulesTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000a");
    private static readonly Guid Categorie = Guid.Parse("cccccccc-1111-0000-0000-00000000000a");

    /// <summary>Lot prêtable : 12 manuels, tous disponibles.</summary>
    private static readonly Guid Manuels = Guid.Parse("dddddddd-1111-0000-0000-00000000000a");

    /// <summary>Lot à exemplaire unique — le support des tests de concurrence.</summary>
    private static readonly Guid Projecteur = Guid.Parse("dddddddd-1111-0000-0000-00000000000b");

    /// <summary>Consommable : se distribue, ne se prête jamais.</summary>
    private static readonly Guid Craie = Guid.Parse("dddddddd-1111-0000-0000-00000000000c");

    private static readonly DateOnly Rentree = new(2026, 9, 15);
    private static readonly Guid Operateur = Guid.Parse("99999999-9999-9999-9999-999999999999");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = Ecole, Name = "École de test" });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "CM2 A", Level = "Primaire", Capacity = 40 });
        owner.Students.Add(new Student
        {
            Id = Eleve, SchoolId = Ecole, Matricule = "ELEV-2026-0001", FullName = "Awa Fall",
            BirthDate = new DateOnly(2015, 3, 12), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe
        });

        owner.InventoryCategories.Add(new InventoryCategory { Id = Categorie, SchoolId = Ecole, Name = "Manuels scolaires" });

        owner.InventoryItems.AddRange(
            new InventoryItem
            {
                Id = Manuels, SchoolId = Ecole, Name = "Manuel de mathématiques CM2", CategoryId = Categorie,
                QuantityTotal = 12, QuantityAvailable = 12
            },
            new InventoryItem
            {
                Id = Projecteur, SchoolId = Ecole, Name = "Vidéoprojecteur Epson", CategoryId = Categorie,
                QuantityTotal = 1, QuantityAvailable = 1
            },
            new InventoryItem
            {
                Id = Craie, SchoolId = Ecole, Name = "Craie blanche (boîte)", CategoryId = Categorie,
                QuantityTotal = 50, QuantityAvailable = 50, IsConsumable = true
            });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // ------------------------------------------------------------------ Disponibilité

    [Fact]
    public async Task Taking_More_Than_Available_Is_Refused_With_A_Business_Message()
    {
        await using var ctx = _db.NewAppContext(Ecole);

        var act = async () => await MovementHandler(ctx).Handle(
            new RecordStockMovementCommand
            {
                ItemId = Manuels,
                Type = StockMovementRequestType.Sortie,
                Quantity = 20,
                Reason = "Transfert vers l'école voisine",
                RowVersion = await ItemVersionAsync(ctx, Manuels)
            },
            CancellationToken.None);

        // 422 et non 500 : la contrainte CHECK aurait aussi refusé, mais avec un message que personne
        // ne peut lire à l'écran.
        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Values.SelectMany(messages => messages)
            .Should().Contain(message => message.Contains("12", StringComparison.Ordinal));

        await using var verify = _db.NewAppContext(Ecole);
        (await verify.InventoryItems.FindAsync(Manuels))!.QuantityAvailable.Should().Be(12);
        verify.StockMovements.Should().BeEmpty("un mouvement refusé ne laisse aucune trace au journal");
    }

    [Fact]
    public async Task The_Second_Loan_Of_The_Last_Unit_Is_Refused_With_A_Readable_Message()
    {
        // Deux surveillants veulent le SEUL vidéoprojecteur. Le second arrive après le premier : son
        // Handler relit le lot et n'y trouve plus rien à prêter. Il doit lire « 0 disponible » (422),
        // pas un conflit d'écriture — rien n'a été écrasé, il n'y a simplement plus de matériel.
        await using var lecture = _db.NewAppContext(Ecole);
        var version = await ItemVersionAsync(lecture, Projecteur);

        await using var premier = _db.NewAppContext(Ecole);
        await AssignmentHandler(premier).Handle(Loan(Projecteur, version), CancellationToken.None);

        await using var second = _db.NewAppContext(Ecole);
        var act = async () => await AssignmentHandler(second).Handle(
            Loan(Projecteur, await ItemVersionAsync(second, Projecteur)), CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Values.SelectMany(messages => messages)
            .Should().Contain(message => message.Contains("0 unité(s) disponible(s)", StringComparison.Ordinal));

        await using var verify = _db.NewAppContext(Ecole);
        (await verify.InventoryItems.FindAsync(Projecteur))!.QuantityAvailable.Should().Be(0);
        (await verify.ItemAssignments.CountAsync(a => a.ItemId == Projecteur)).Should().Be(1);
    }

    [Fact]
    public async Task A_Loan_Sent_With_A_Stale_Token_Is_Refused_Even_When_Stock_Would_Allow_It()
    {
        // Le cas que le verrou optimiste protège vraiment, et que le test précédent ne couvre PAS :
        // la quantité suffirait, mais le client travaille sur une vue périmée du lot. Deux surveillants
        // ouvrent l'écran de prêt en même temps (même jeton xmin) ; le premier prête, le second envoie
        // sa demande avec le jeton d'AVANT. Le stock permettrait l'opération — c'est la fraîcheur de
        // la lecture qui ne le permet pas, et le refus doit être un 409 franc (AGENTS.md règle #5).
        await using var lecture = _db.NewAppContext(Ecole);
        var versionCommune = await ItemVersionAsync(lecture, Manuels);

        await using var premier = _db.NewAppContext(Ecole);
        await AssignmentHandler(premier).Handle(
            Loan(Manuels, versionCommune, quantity: 1), CancellationToken.None);

        await using var second = _db.NewAppContext(Ecole);
        var act = async () => await AssignmentHandler(second).Handle(
            Loan(Manuels, versionCommune, quantity: 1), CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();

        await using var verify = _db.NewAppContext(Ecole);
        (await verify.InventoryItems.FindAsync(Manuels))!.QuantityAvailable.Should().Be(11);
        (await verify.ItemAssignments.CountAsync(a => a.ItemId == Manuels)).Should().Be(1);
        (await verify.StockMovements.CountAsync(m => m.ItemId == Manuels)).Should().Be(
            1, "la fiche refusée ne doit laisser aucune ligne au journal");
    }

    [Fact]
    public async Task A_Consumable_Cannot_Be_Lent()
    {
        await using var ctx = _db.NewAppContext(Ecole);

        var act = async () => await AssignmentHandler(ctx).Handle(
            Loan(Craie, await ItemVersionAsync(ctx, Craie)), CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Values.SelectMany(messages => messages)
            .Should().Contain(message => message.Contains("consommable", StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------ Cycle de vie d'un prêt

    [Fact]
    public async Task Loan_Then_Return_Restores_Availability_And_Leaves_Two_Journal_Lines()
    {
        await using var pret = _db.NewAppContext(Ecole);
        var fiche = await AssignmentHandler(pret).Handle(
            Loan(Manuels, await ItemVersionAsync(pret, Manuels), quantity: 4), CancellationToken.None);

        await using var apresPret = _db.NewAppContext(Ecole);
        var lot = await apresPret.InventoryItems.FindAsync(Manuels);
        lot!.QuantityAvailable.Should().Be(8);
        lot.QuantityTotal.Should().Be(12, "prêter ne fait pas sortir un bien du patrimoine");

        await using var retour = _db.NewAppContext(Ecole);
        var resultat = await ReturnHandler(retour).Handle(
            new ReturnItemAssignmentCommand
            {
                Id = fiche.Id,
                ReturnedQuantity = 4,
                ReturnCondition = ItemCondition.Bon,
                RowVersion = fiche.RowVersion
            },
            CancellationToken.None);

        resultat.Status.Should().Be(nameof(AssignmentStatus.Restitue));

        await using var verify = _db.NewAppContext(Ecole);
        (await verify.InventoryItems.FindAsync(Manuels))!.QuantityAvailable.Should().Be(12);

        var journal = await verify.StockMovements.Where(m => m.ItemId == Manuels).ToListAsync();
        journal.Select(m => m.Type).Should().BeEquivalentTo(
            [StockMovementType.Attribution, StockMovementType.Restitution],
            "le journal doit raconter la remise ET le retour, pas seulement le solde");
    }

    [Fact]
    public async Task Returning_A_Broken_Item_Scraps_It_Instead_Of_Putting_It_Back_In_Circulation()
    {
        await using var pret = _db.NewAppContext(Ecole);
        var fiche = await AssignmentHandler(pret).Handle(
            Loan(Manuels, await ItemVersionAsync(pret, Manuels), quantity: 2), CancellationToken.None);

        await using var retour = _db.NewAppContext(Ecole);
        await ReturnHandler(retour).Handle(
            new ReturnItemAssignmentCommand
            {
                Id = fiche.Id,
                ReturnedQuantity = 2,
                ReturnCondition = ItemCondition.HorsService,
                RowVersion = fiche.RowVersion
            },
            CancellationToken.None);

        await using var verify = _db.NewAppContext(Ecole);
        var lot = await verify.InventoryItems.FindAsync(Manuels);

        lot!.QuantityTotal.Should().Be(10, "deux manuels détruits quittent le patrimoine");
        lot.QuantityAvailable.Should().Be(10, "ils ne doivent pas retourner en circulation");

        var journal = await verify.StockMovements.Where(m => m.ItemId == Manuels).Select(m => m.Type).ToListAsync();
        journal.Should().BeEquivalentTo(
            [StockMovementType.Attribution, StockMovementType.Restitution, StockMovementType.MiseAuRebut],
            "le retour et la réforme sont deux faits distincts : les fondre en une ligne ferait disparaître les unités du récit");
    }

    [Fact]
    public async Task Declaring_The_Remainder_Lost_Removes_It_From_The_Patrimony_Only()
    {
        await using var pret = _db.NewAppContext(Ecole);
        var fiche = await AssignmentHandler(pret).Handle(
            Loan(Manuels, await ItemVersionAsync(pret, Manuels), quantity: 5), CancellationToken.None);

        await using var retour = _db.NewAppContext(Ecole);
        var resultat = await ReturnHandler(retour).Handle(
            new ReturnItemAssignmentCommand
            {
                Id = fiche.Id,
                ReturnedQuantity = 3,
                ReturnCondition = ItemCondition.Bon,
                DeclareRemainderLost = true,
                RowVersion = fiche.RowVersion
            },
            CancellationToken.None);

        resultat.Status.Should().Be(nameof(AssignmentStatus.Perdu));

        await using var verify = _db.NewAppContext(Ecole);
        var lot = await verify.InventoryItems.FindAsync(Manuels);

        // 3 manuels reviennent (8 -> 11 disponibles), 2 sont perdus : ils sortent du TOTAL sans
        // jamais avoir réintégré le disponible.
        lot!.QuantityAvailable.Should().Be(10);
        lot.QuantityTotal.Should().Be(10);

        var perte = await verify.StockMovements.SingleAsync(m => m.Type == StockMovementType.PerteSurPret);
        perte.Quantity.Should().Be(2);
    }

    [Fact]
    public async Task Partial_Return_Keeps_The_Loan_Open()
    {
        await using var pret = _db.NewAppContext(Ecole);
        var fiche = await AssignmentHandler(pret).Handle(
            Loan(Manuels, await ItemVersionAsync(pret, Manuels), quantity: 6), CancellationToken.None);

        await using var retour = _db.NewAppContext(Ecole);
        var resultat = await ReturnHandler(retour).Handle(
            new ReturnItemAssignmentCommand
            {
                Id = fiche.Id,
                ReturnedQuantity = 2,
                ReturnCondition = ItemCondition.Bon,
                RowVersion = fiche.RowVersion
            },
            CancellationToken.None);

        resultat.Status.Should().Be(nameof(AssignmentStatus.PartiellementRestitue));
        resultat.OutstandingQuantity.Should().Be(4);

        // Le second retour doit repartir du NOUVEAU jeton : la fiche a changé.
        await using var solde = _db.NewAppContext(Ecole);
        var final = await ReturnHandler(solde).Handle(
            new ReturnItemAssignmentCommand
            {
                Id = fiche.Id,
                ReturnedQuantity = 4,
                ReturnCondition = ItemCondition.Bon,
                RowVersion = resultat.RowVersion
            },
            CancellationToken.None);

        final.Status.Should().Be(nameof(AssignmentStatus.Restitue));
        final.ReturnedQuantity.Should().Be(6, "la fiche cumule les restitutions successives");
    }

    [Fact]
    public async Task Cancelling_A_Loan_Restores_Availability_And_Never_Erases_The_Journal()
    {
        await using var pret = _db.NewAppContext(Ecole);
        var fiche = await AssignmentHandler(pret).Handle(
            Loan(Manuels, await ItemVersionAsync(pret, Manuels), quantity: 3), CancellationToken.None);

        await using var annulation = _db.NewAppContext(Ecole);
        await new CancelItemAssignmentCommandHandler(annulation, new FixedUser(Operateur), Clock())
            .Handle(new CancelItemAssignmentCommand(fiche.Id, fiche.RowVersion), CancellationToken.None);

        await using var verify = _db.NewAppContext(Ecole);
        (await verify.InventoryItems.FindAsync(Manuels))!.QuantityAvailable.Should().Be(12);

        // Soft delete : la fiche disparaît des listes (Global Query Filter) mais les deux lignes de
        // journal restent — l'attribution ET sa contre-passation.
        (await verify.ItemAssignments.CountAsync(a => a.Id == fiche.Id)).Should().Be(0);
        (await verify.StockMovements.CountAsync(m => m.AssignmentId == fiche.Id)).Should().Be(2);
    }

    [Fact]
    public async Task Archiving_An_Item_With_An_Open_Loan_Is_Refused()
    {
        await using var pret = _db.NewAppContext(Ecole);
        await AssignmentHandler(pret).Handle(
            Loan(Manuels, await ItemVersionAsync(pret, Manuels), quantity: 1), CancellationToken.None);

        await using var suppression = _db.NewAppContext(Ecole);
        var act = async () => await new DeleteInventoryItemCommandHandler(suppression, new FixedUser(Operateur))
            .Handle(
                new DeleteInventoryItemCommand(Manuels, await ItemVersionAsync(suppression, Manuels)),
                CancellationToken.None);

        // BusinessRuleException -> 409 : c'est l'ÉTAT du bien qui bloque, pas la forme de la requête.
        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    // ------------------------------------------------------------------ Ajustement d'inventaire

    [Fact]
    public async Task An_Adjustment_Records_The_Gap_And_Its_Direction_Not_The_Counted_Total()
    {
        // Le magasinier saisit ce qu'il a compté (9 sur 12 annoncés) ; le journal, lui, doit porter
        // l'ÉCART et son sens — c'est ce qui rend une ligne isolée lisible sans rejeu.
        await using var ctx = _db.NewAppContext(Ecole);

        await MovementHandler(ctx).Handle(
            new RecordStockMovementCommand
            {
                ItemId = Manuels,
                Type = StockMovementRequestType.Ajustement,
                Quantity = 9,
                Reason = "Inventaire physique de rentrée",
                RowVersion = await ItemVersionAsync(ctx, Manuels)
            },
            CancellationToken.None);

        await using var verify = _db.NewAppContext(Ecole);
        var mouvement = await verify.StockMovements.SingleAsync();

        mouvement.Type.Should().Be(StockMovementType.AjustementNegatif);
        mouvement.Quantity.Should().Be(3);
        mouvement.QuantityTotalAfter.Should().Be(9);

        (await verify.InventoryItems.FindAsync(Manuels))!.QuantityTotal.Should().Be(9);
    }

    [Fact]
    public async Task An_Adjustment_That_Confirms_The_Sheet_Records_Nothing()
    {
        await using var ctx = _db.NewAppContext(Ecole);

        var act = async () => await MovementHandler(ctx).Handle(
            new RecordStockMovementCommand
            {
                ItemId = Manuels,
                Type = StockMovementRequestType.Ajustement,
                Quantity = 12,
                Reason = "Inventaire physique de rentrée",
                RowVersion = await ItemVersionAsync(ctx, Manuels)
            },
            CancellationToken.None);

        // Une ligne de zéro unité polluerait le journal et violerait la contrainte CHECK Quantity > 0.
        await act.Should().ThrowAsync<ValidationException>();

        await using var verify = _db.NewAppContext(Ecole);
        verify.StockMovements.Should().BeEmpty();
    }

    // ------------------------------------------------------------------ Outillage

    private static CreateItemAssignmentCommand Loan(Guid itemId, uint rowVersion, int quantity = 1) => new()
    {
        ItemId = itemId,
        Quantity = quantity,
        BeneficiaryType = AssignmentBeneficiaryType.Eleve,
        BeneficiaryId = Eleve,
        AssignedOn = Rentree,
        DueOn = new DateOnly(2027, 6, 30),
        RowVersion = rowVersion
    };

    private static TimeProvider Clock() => new FixedClock(Rentree);

    private static RecordStockMovementCommandHandler MovementHandler(ApplicationDbContext ctx) =>
        new(ctx, Clock());

    private static CreateItemAssignmentCommandHandler AssignmentHandler(ApplicationDbContext ctx) =>
        new(ctx, new FixedTenant(Ecole), Clock());

    private static ReturnItemAssignmentCommandHandler ReturnHandler(ApplicationDbContext ctx) =>
        new(ctx, Clock());

    private static Task<uint> ItemVersionAsync(ApplicationDbContext ctx, Guid itemId) =>
        ctx.InventoryItems.AsNoTracking()
            .Where(i => i.Id == itemId)
            .Select(i => EF.Property<uint>(i, "xmin"))
            .FirstAsync();
}

file sealed class FixedTenant(Guid schoolId) : ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}

file sealed class FixedUser(Guid userId) : ICurrentUserService
{
    public Guid? UserId => userId;

    public Role? Role => SamaEcole.Domain.Enums.Role.Surveillant;

    public string? IpAddress => null;
}

/// <summary>Voir la remarque de FixedClock dans InventoryIsolationTests : mêmes raisons.</summary>
file sealed class FixedClock(DateOnly today) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() =>
        new(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
