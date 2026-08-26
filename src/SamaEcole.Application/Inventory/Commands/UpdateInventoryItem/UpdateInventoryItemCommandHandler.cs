using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Inventory.Commands.CreateInventoryItem;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Inventory.Commands.UpdateInventoryItem;

public class UpdateInventoryItemCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<UpdateInventoryItemCommand, InventoryItemResult>
{
    public async Task<InventoryItemResult> Handle(
        UpdateInventoryItemCommand request, CancellationToken cancellationToken)
    {
        var item = await dbContext.InventoryItems
            .FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Bien d'inventaire {request.Id} introuvable.");

        if (!await dbContext.InventoryCategories.AnyAsync(c => c.Id == request.CategoryId, cancellationToken))
        {
            throw Invalid(nameof(request.CategoryId), "La catégorie indiquée n'existe pas dans votre établissement.");
        }

        if (request.RoomId is { } roomId
            && !await dbContext.Rooms.AnyAsync(r => r.Id == roomId, cancellationToken))
        {
            throw Invalid(nameof(request.RoomId), "La salle indiquée n'existe pas dans votre établissement.");
        }

        // Un consommable ne se prête pas : basculer un lot en consommable alors qu'il est encore sorti
        // chez un bénéficiaire rendrait sa restitution impossible à raconter.
        if (request.IsConsumable && !item.IsConsumable)
        {
            var hasOpenAssignment = await dbContext.ItemAssignments.AnyAsync(
                a => a.ItemId == item.Id
                     && (a.Status == AssignmentStatus.EnCours || a.Status == AssignmentStatus.PartiellementRestitue),
                cancellationToken);

            if (hasOpenAssignment)
            {
                throw new BusinessRuleException(
                    "Impossible de marquer ce bien comme consommable : des prêts sont encore en cours.");
            }
        }

        dbContext.SetOriginalConcurrencyToken(item, request.RowVersion);

        item.Name = request.Name.Trim();
        item.Code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim();
        item.CategoryId = request.CategoryId;
        item.Condition = request.Condition;
        item.RoomId = request.RoomId;
        item.LocationLabel = string.IsNullOrWhiteSpace(request.LocationLabel) ? null : request.LocationLabel.Trim();
        item.UnitPrice = request.UnitPrice;
        item.IsConsumable = request.IsConsumable;
        item.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();

        await dbContext.SaveChangesAsync(cancellationToken);

        var newRowVersion = await dbContext.InventoryItems.AsNoTracking()
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
            newRowVersion);
    }

    private static ValidationException Invalid(string property, string message) =>
        new([new ValidationFailure(property, message)]);
}
