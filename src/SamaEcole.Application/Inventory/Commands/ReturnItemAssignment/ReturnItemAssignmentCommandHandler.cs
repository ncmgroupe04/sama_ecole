using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Inventory.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Inventory.Commands.ReturnItemAssignment;

public class ReturnItemAssignmentCommandHandler(
    IApplicationDbContext dbContext,
    TimeProvider timeProvider)
    : IRequestHandler<ReturnItemAssignmentCommand, ItemReturnResult>
{
    public async Task<ItemReturnResult> Handle(
        ReturnItemAssignmentCommand request, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var returnedOn = request.ReturnedOn ?? today;

        if (returnedOn > today)
        {
            throw Invalid(nameof(request.ReturnedOn), "La date de retour ne peut pas être dans le futur.");
        }

        var assignment = await dbContext.ItemAssignments
            .FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Fiche de prêt {request.Id} introuvable.");

        // C'est l'ÉTAT de la fiche qui bloque, pas la forme de la requête -> 409 et non 422
        // (voir la doc de BusinessRuleException).
        if (assignment.Status is AssignmentStatus.Restitue or AssignmentStatus.Perdu)
        {
            throw new BusinessRuleException("Cette fiche de prêt est déjà clôturée.");
        }

        if (returnedOn < assignment.AssignedOn)
        {
            throw Invalid(nameof(request.ReturnedOn), "La date de retour ne peut pas précéder la date de remise.");
        }

        var alreadyReturned = assignment.ReturnedQuantity ?? 0;
        var outstanding = assignment.Quantity - alreadyReturned;

        if (request.ReturnedQuantity > outstanding)
        {
            throw Invalid(
                nameof(request.ReturnedQuantity),
                $"Il ne reste que {outstanding} unité(s) à restituer sur cette fiche.");
        }

        if (request.ReturnedQuantity == 0 && !request.DeclareRemainderLost)
        {
            throw Invalid(
                nameof(request.ReturnedQuantity),
                "Indiquez les unités rendues, ou déclarez le reste perdu : sans l'un ni l'autre, il n'y a rien à enregistrer.");
        }

        var item = await dbContext.InventoryItems
            .FirstOrDefaultAsync(i => i.Id == assignment.ItemId, cancellationToken)
            ?? throw new KeyNotFoundException($"Bien d'inventaire {assignment.ItemId} introuvable.");

        dbContext.SetOriginalConcurrencyToken(assignment, request.RowVersion);

        var movements = new List<StockMovement>();

        if (request.ReturnedQuantity > 0)
        {
            movements.Add(StockLedger.Apply(
                item,
                StockMovementType.Restitution,
                request.ReturnedQuantity,
                returnedOn,
                $"Restitution par {assignment.BeneficiaryLabel}",
                assignment.BeneficiaryLabel,
                assignment.Id));

            // Le bien est bien revenu — mais cassé. Deux lignes plutôt qu'une : le journal doit
            // montrer le retour ET la réforme, sinon l'unité semblerait s'être volatilisée entre
            // l'attribution et le total du lot.
            if (request.ReturnCondition == ItemCondition.HorsService)
            {
                movements.Add(StockLedger.Apply(
                    item,
                    StockMovementType.MiseAuRebut,
                    request.ReturnedQuantity,
                    returnedOn,
                    $"Réforme au retour : matériel hors service rendu par {assignment.BeneficiaryLabel}",
                    assignment.BeneficiaryLabel,
                    assignment.Id));
            }
        }

        var totalReturned = alreadyReturned + request.ReturnedQuantity;
        var remainder = assignment.Quantity - totalReturned;

        if (request.DeclareRemainderLost && remainder > 0)
        {
            // PerteSurPret et non MiseAuRebut : ces unités n'ont jamais réintégré le disponible, seul
            // le total du patrimoine baisse (voir StockMovementType.PerteSurPret).
            movements.Add(StockLedger.Apply(
                item,
                StockMovementType.PerteSurPret,
                remainder,
                returnedOn,
                $"Perte déclarée : {remainder} unité(s) non restituée(s) par {assignment.BeneficiaryLabel}",
                assignment.BeneficiaryLabel,
                assignment.Id));
        }

        dbContext.StockMovements.AddRange(movements);

        assignment.ReturnedQuantity = totalReturned;
        assignment.ReturnedOn = returnedOn;
        assignment.ReturnCondition = request.ReturnCondition;
        assignment.Status = (remainder, request.DeclareRemainderLost) switch
        {
            (0, _) => AssignmentStatus.Restitue,
            (> 0, true) => AssignmentStatus.Perdu,
            _ => AssignmentStatus.PartiellementRestitue
        };

        await dbContext.SaveChangesAsync(cancellationToken);

        var versions = await dbContext.ItemAssignments.AsNoTracking()
            .Where(a => a.Id == assignment.Id)
            .Select(a => new
            {
                RowVersion = EF.Property<uint>(a, "xmin"),
                ItemRowVersion = dbContext.InventoryItems.AsNoTracking()
                    .Where(i => i.Id == a.ItemId)
                    .Select(i => EF.Property<uint>(i, "xmin"))
                    .First()
            })
            .FirstAsync(cancellationToken);

        return new ItemReturnResult(
            assignment.Id,
            assignment.Status.ToString(),
            totalReturned,
            assignment.Status == AssignmentStatus.PartiellementRestitue ? remainder : 0,
            returnedOn,
            item.QuantityAvailable,
            versions.RowVersion,
            versions.ItemRowVersion);
    }

    private static ValidationException Invalid(string property, string message) =>
        new([new ValidationFailure(property, message)]);
}
