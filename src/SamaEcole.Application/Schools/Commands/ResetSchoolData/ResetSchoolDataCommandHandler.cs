using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.Schools.Commands.ResetSchoolData;

/// <summary>
/// Remet l'établissement courant à neuf. Le Handler ne fait que garder la porte — résoudre le tenant,
/// vérifier le rôle, exiger le mot de confirmation — puis déléguer : la suppression elle-même dépend
/// du schéma relationnel et des droits PostgreSQL, elle vit donc dans SamaEcole.Persistence (voir
/// IResetSchoolDataService).
/// </summary>
public class ResetSchoolDataCommandHandler(
    IApplicationDbContext dbContext,
    IResetSchoolDataService resetService,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    ILogger<ResetSchoolDataCommandHandler> logger)
    : IRequestHandler<ResetSchoolDataCommand, SchoolDataResetSummary>
{
    public async Task<SchoolDataResetSummary> Handle(
        ResetSchoolDataCommand request,
        CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Double garde du rôle : l'endpoint est déjà [Authorize(Roles = Directeur)], mais une purge
        // irréversible ne doit pas dépendre d'un seul attribut qu'un futur refactor pourrait déplacer.
        if (currentUser.Role != Role.Directeur)
        {
            throw new UnauthorizedAccessException("Seul le Directeur peut réinitialiser les données de l'établissement.");
        }

        var school = await dbContext.Schools
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == schoolId, cancellationToken)
            ?? throw new NotFoundException("École", schoolId);

        // Verrou PERMANENT : une fois qu'une école est passée en mode réel une seule fois, la purge
        // reste interdite pour toujours (AGENTS.md règle #6) — même après un retour en mode test
        // (RevertToTestCommand, disponible à tout moment), qui remet WentLiveAt à null mais jamais
        // HasEverGoneLive. On échoue AVANT même de regarder le mot de confirmation. La fonction
        // PostgreSQL reset_school_data porte la MÊME garde, en défense de profondeur (migration
        // GuardResetSchoolDataAgainstLiveMode / AddHasEverGoneLiveLock).
        if (school.HasEverGoneLive)
        {
            var message = school.WentLiveAt is { } wentLiveAt
                ? $"La réinitialisation n'est possible qu'en mode test. Cet établissement est passé en mode réel le {wentLiveAt:dd/MM/yyyy} : les données enregistrées ne peuvent plus être effacées."
                : "La réinitialisation n'est plus possible : cet établissement est déjà passé en mode réel par le passé. Les données enregistrées à ce moment-là font partie de la comptabilité et restent inaltérables, même après un retour en mode test.";

            throw new BusinessRuleException(message, "RESET_UNAVAILABLE_LIVE_MODE");
        }

        EnsureConfirmed(request.Confirmation, school.Name);

        logger.LogWarning(
            "Réinitialisation des données demandée par l'utilisateur {UserId} sur l'établissement {SchoolId} ({SchoolName}).",
            currentUser.UserId, schoolId, school.Name);

        return await resetService.ResetAsync(schoolId, cancellationToken);
    }

    private static void EnsureConfirmed(string confirmation, string schoolName)
    {
        if (ResetSchoolDataConfirmation.Matches(confirmation, schoolName))
        {
            return;
        }

        throw new ValidationException(
        [
            new ValidationFailure(
                "confirmation",
                $"Saisissez exactement « {ResetSchoolDataConfirmation.Keyword} » ou le nom de votre établissement pour confirmer.")
        ]);
    }
}
