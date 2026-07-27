using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Platform.Commands.TopUpSmsCredits;

/// <summary>
/// POST /admin/platform/schools/{schoolId}/sms-credits — création de crédits SMS par le Super Admin.
/// RÉSERVÉ AU SUPER ADMIN : c'est l'acte qui donne de la valeur payante à une école, il ne peut pas
/// appartenir au Directeur qui en bénéficie (voir SchoolSettingsDto.SmsCreditBalance).
///
/// Incrément ATOMIQUE (ExecuteUpdateAsync), comme le débit de SmsDispatcher : un crédit posé pendant
/// qu'une alerte débite le solde ne doit pas écraser ce débit.
/// </summary>
public record TopUpSmsCreditsCommand : IRequest<int>
{
    public required Guid SchoolId { get; init; }

    /// <summary>Nombre de SEGMENTS crédités (unité de facturation de l'agrégateur, voir SmsSegments).</summary>
    public required int Segments { get; init; }
}

public class TopUpSmsCreditsCommandHandler(
    IApplicationDbContext dbContext,
    IAuditLogStore auditLogStore,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    ILogger<TopUpSmsCreditsCommandHandler> logger)
    : IRequestHandler<TopUpSmsCreditsCommand, int>
{
    public async Task<int> Handle(TopUpSmsCreditsCommand request, CancellationToken cancellationToken)
    {
        var superAdminId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Aucun utilisateur authentifié.");

        // IgnoreQueryFilters : le Super Admin n'a pas de tenant, le Global Query Filter de
        // school_settings ne trouverait donc AUCUNE ligne. Le filtre SchoolId est explicite.
        var updated = await dbContext.SchoolSettings
            .IgnoreQueryFilters()
            .Where(s => s.SchoolId == request.SchoolId && !s.IsDeleted)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(s => s.SmsCreditBalance, s => s.SmsCreditBalance + request.Segments),
                cancellationToken);

        if (updated == 0)
        {
            // Aucune ligne de paramètres : école antérieure au ticket JGK-B02, dont le Directeur n'a
            // jamais ouvert l'écran. La créer ICI, depuis une session sans tenant, échouerait au
            // WITH CHECK de la policy RLS — mieux vaut une erreur explicite qu'un crédit perdu.
            throw new KeyNotFoundException(
                "Cet établissement n'a pas encore de paramètres enregistrés : son Directeur doit ouvrir "
                + "l'écran Paramètres une première fois avant de pouvoir être crédité.");
        }

        var newBalance = await dbContext.SchoolSettings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.SchoolId == request.SchoolId)
            .Select(s => s.SmsCreditBalance)
            .SingleAsync(cancellationToken);

        // Attribuée à l'école CIBLE (même raisonnement que GrantComplimentaryAccessCommandHandler) :
        // l'acteur Super Admin n'a pas de SchoolId propre à qui imputer l'entrée.
        await auditLogStore.AppendAsync(
            request.SchoolId, superAdminId, "Sms", "TopUpSmsCredits",
            success: true, failureReason: null, currentUser.IpAddress, timeProvider.GetUtcNow(), cancellationToken);

        logger.LogInformation(
            "Super Admin {SuperAdminId} a crédité {Segments} segment(s) SMS à l'établissement {SchoolId} (nouveau solde : {Balance}).",
            superAdminId, request.Segments, request.SchoolId, newBalance);

        return newBalance;
    }
}
