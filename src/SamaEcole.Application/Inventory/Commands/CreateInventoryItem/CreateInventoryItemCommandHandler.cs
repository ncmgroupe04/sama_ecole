using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Inventory.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Inventory.Commands.CreateInventoryItem;

public class CreateInventoryItemCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    TimeProvider timeProvider)
    : IRequestHandler<CreateInventoryItemCommand, InventoryItemResult>
{
    public async Task<InventoryItemResult> Handle(
        CreateInventoryItemCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // La catégorie doit exister DANS CETTE ÉCOLE. Le Global Query Filter restreint déjà chaque
        // requête au tenant courant : une catégorie d'une autre école y est structurellement
        // introuvable (même principe que CreateRoomCommandHandler vis-à-vis du bâtiment).
        if (!await dbContext.InventoryCategories.AnyAsync(c => c.Id == request.CategoryId, cancellationToken))
        {
            throw Invalid(nameof(request.CategoryId), "La catégorie indiquée n'existe pas dans votre établissement.");
        }

        if (request.RoomId is { } roomId
            && !await dbContext.Rooms.AnyAsync(r => r.Id == roomId, cancellationToken))
        {
            throw Invalid(nameof(request.RoomId), "La salle indiquée n'existe pas dans votre établissement.");
        }

        var item = new InventoryItem
        {
            SchoolId = schoolId,
            Name = request.Name.Trim(),
            Code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim(),
            CategoryId = request.CategoryId,
            QuantityTotal = 0,
            QuantityAvailable = 0,
            Condition = request.Condition,
            RoomId = request.RoomId,
            LocationLabel = string.IsNullOrWhiteSpace(request.LocationLabel) ? null : request.LocationLabel.Trim(),
            UnitPrice = request.UnitPrice,
            IsConsumable = request.IsConsumable,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
        };

        dbContext.InventoryItems.Add(item);

        // Le lot naît à zéro puis reçoit un mouvement d'entrée, plutôt que de naître avec ses
        // quantités : le journal de stock raconte alors l'inventaire depuis sa PREMIÈRE unité, sans
        // le trou initial qu'une écriture directe des compteurs laisserait.
        if (request.InitialQuantity > 0)
        {
            var movement = StockLedger.Apply(
                item,
                StockMovementType.Entree,
                request.InitialQuantity,
                DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime),
                "Constitution du lot à la création de la fiche d'inventaire");

            dbContext.StockMovements.Add(movement);
        }

        // Un code d'inventaire déjà pris viole l'index unique partiel : SaveChangesAsync le traduit en
        // DuplicateRecordException -> 409 (AGENTS.md règle #5), jamais un 500 ni un doublon.
        await dbContext.SaveChangesAsync(cancellationToken);

        var rowVersion = await dbContext.InventoryItems.AsNoTracking()
            .Where(i => i.Id == item.Id)
            .Select(i => EF.Property<uint>(i, "xmin"))
            .FirstAsync(cancellationToken);

        return new InventoryItemResult(
            item.Id,
            item.Name,
            item.Code,
            item.CategoryId,
            item.QuantityTotal,
            item.QuantityAvailable,
            item.Condition.ToString(),
            item.RoomId,
            item.LocationLabel,
            item.UnitPrice,
            item.IsConsumable,
            rowVersion);
    }

    private static ValidationException Invalid(string property, string message) =>
        new([new ValidationFailure(property, message)]);
}
