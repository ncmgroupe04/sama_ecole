using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Enrollments.Commands.CancelEnrollment;

public class CancelEnrollmentCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<CancelEnrollmentCommand, Unit>
{
    public async Task<Unit> Handle(CancelEnrollmentCommand request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la policy RLS bornent déjà la recherche à l'école courante : viser
        // une inscription d'une autre école renvoie 404, jamais une annulation silencieuse.
        var enrollment = await dbContext.Enrollments
            .FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Inscription {request.Id} introuvable.");

        // Un statut déjà terminal (annulée, en abandon, transférée) n'est pas une « erreur de saisie » à
        // corriger : DroppedOut/Transferred représentent une scolarité RÉELLE qui s'est arrêtée, ce
        // n'est pas au Cancel de la faire disparaître rétroactivement (voir ChangeEnrollmentStatusCommand).
        if (enrollment.Status is EnrollmentStatus.Cancelled or EnrollmentStatus.DroppedOut or EnrollmentStatus.Transferred)
        {
            throw new BusinessRuleException(
                $"Cette inscription est déjà au statut '{enrollment.Status}' : elle ne peut plus être annulée comme une simple erreur de saisie.");
        }

        // Règle métier obligatoire : une inscription déjà encaissée (même partiellement) ne peut plus
        // être présentée comme une simple erreur de saisie — le reçu de paiement porte un numéro
        // officiel gapless (AGENTS.md règle #3) qui doit rester rattaché à une inscription réelle.
        var hasPayment = await dbContext.Payments
            .AnyAsync(p => p.EnrollmentId == request.Id, cancellationToken);

        if (hasPayment)
        {
            throw new BusinessRuleException(
                "Impossible d'annuler : un reçu de paiement existe déjà pour cette inscription. " +
                "Utilisez un changement de statut (abandon ou transfert) à la place.");
        }

        // Cœur du verrou optimiste (AGENTS.md règle #5) : une inscription modifiée en base depuis sa
        // lecture (ex. un encaissement concurrent) fait échouer SaveChangesAsync en 409.
        dbContext.SetOriginalConcurrencyToken(enrollment, request.RowVersion);

        // Statut Cancelled SANS SoftDelete — voir le commentaire de CancelEnrollmentCommand : la ligne
        // doit rester visible dans l'historique scolaire, jamais masquée par le Global Query Filter.
        enrollment.Status = EnrollmentStatus.Cancelled;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
