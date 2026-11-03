using SamaEcole.Application.Boarding.Assignments;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Enrollments.Commands.ChangeEnrollmentStatus;

public class ChangeEnrollmentStatusCommandHandler(
    IApplicationDbContext dbContext, IKpiCacheService kpiCache, IBoardingAssignmentService boardingAssignments)
    : IRequestHandler<ChangeEnrollmentStatusCommand, Unit>
{
    public async Task<Unit> Handle(ChangeEnrollmentStatusCommand request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la policy RLS bornent déjà la recherche à l'école courante : viser
        // une inscription d'une autre école renvoie 404, jamais un changement de statut silencieux.
        var enrollment = await dbContext.Enrollments
            .FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Inscription {request.Id} introuvable.");

        // Une inscription déjà dans un statut terminal (annulée, en abandon ou transférée) ne peut pas
        // transiter à nouveau : ces statuts sont des points d'arrivée, pas des étapes intermédiaires.
        if (enrollment.Status is EnrollmentStatus.Cancelled or EnrollmentStatus.DroppedOut or EnrollmentStatus.Transferred)
        {
            throw new BusinessRuleException(
                $"Cette inscription est déjà au statut '{enrollment.Status}' : aucun changement supplémentaire n'est possible.");
        }

        // Cœur du verrou optimiste (AGENTS.md règle #5).
        dbContext.SetOriginalConcurrencyToken(enrollment, request.RowVersion);

        enrollment.Status = request.NewStatus;

        // Abandon ou transfert : la scolarité s'arrête réellement, l'élève ne dort plus à l'internat. Le séjour est CLOS (lit
        // libéré, historique conservé — contrairement à une annulation, rien n'est archivé). Refusé en 409 LEAVE_IN_PROGRESS tant
        // qu'une sortie est ouverte. Sans cela le lit restait occupé par un élève parti (lit « fantôme »).
        await boardingAssignments.EndAsync(enrollment.Id, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        // Même raisonnement que CancelEnrollmentCommandHandler : un changement de statut (abandon,
        // transfert...) sort l'inscription du périmètre "Status != Cancelled" des deux dashboards KPI.
        kpiCache.Invalidate(KpiCacheKeys.FinanceDashboard);
        kpiCache.Invalidate(KpiCacheKeys.DirectorDashboard);

        return Unit.Value;
    }
}
