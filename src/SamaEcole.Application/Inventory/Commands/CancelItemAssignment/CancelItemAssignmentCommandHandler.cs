using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Inventory.Common;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Inventory.Commands.CancelItemAssignment;

public class CancelItemAssignmentCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser,
    TimeProvider timeProvider)
    : IRequestHandler<CancelItemAssignmentCommand, Unit>
{
    public async Task<Unit> Handle(CancelItemAssignmentCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        var assignment = await dbContext.ItemAssignments
            .FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Fiche de prêt {request.Id} introuvable.");

        // Dès qu'une unité est revenue, la fiche raconte un fait réel : elle se clôture par une
        // restitution, elle ne s'annule plus. Sinon on effacerait un retour effectivement constaté.
        if (assignment.Status != AssignmentStatus.EnCours)
        {
            throw new BusinessRuleException(
                "Impossible d'annuler : cette fiche a déjà fait l'objet d'une restitution ou d'une perte.");
        }

        var item = await dbContext.InventoryItems
            .FirstOrDefaultAsync(i => i.Id == assignment.ItemId, cancellationToken)
            ?? throw new KeyNotFoundException($"Bien d'inventaire {assignment.ItemId} introuvable.");

        dbContext.SetOriginalConcurrencyToken(assignment, request.RowVersion);

        var movement = StockLedger.Apply(
            item,
            StockMovementType.Restitution,
            assignment.Quantity,
            DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime),
            $"Annulation de la fiche de prêt {AssignmentReference.For(assignment.Id, assignment.AssignedOn)} (saisie erronée)",
            assignment.BeneficiaryLabel,
            assignment.Id);

        dbContext.StockMovements.Add(movement);

        // Soft delete (AGENTS.md règle #6) : la fiche annulée reste consultable, et les deux lignes de
        // journal — l'attribution et sa contre-passation — restent en place.
        assignment.SoftDelete(actorId.ToString());

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
