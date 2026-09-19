using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.Schools.Commands.LockProduction;

/// <summary>
/// Verrouille DÉFINITIVEMENT la « Zone de danger » de l'établissement courant. Le Handler ne fait que
/// garder la porte — résoudre le tenant, vérifier le rôle, refuser un second verrouillage, exiger le
/// mot de confirmation — puis pose le drapeau sur <c>schools</c>.
///
/// <c>schools</c> échappe à la RLS (elle DÉFINIT le tenant) : c'est le filtre applicatif sur l'Id du
/// JWT qui tient lieu d'isolation ici, jamais une école désignée par le client (règle #10).
/// </summary>
public class LockProductionCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    ILogger<LockProductionCommandHandler> logger)
    : IRequestHandler<LockProductionCommand, LockProductionResult>
{
    public async Task<LockProductionResult> Handle(LockProductionCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Double garde du rôle : l'endpoint est déjà [Authorize(Roles = Directeur)], mais une décision
        // irréversible ne doit pas dépendre d'un seul attribut qu'un futur refactor pourrait déplacer
        // (même défense que GoLiveCommandHandler et ResetSchoolDataCommandHandler).
        if (currentUser.Role != Role.Directeur)
        {
            throw new UnauthorizedAccessException("Seul le Directeur peut verrouiller définitivement l'établissement.");
        }

        var school = await dbContext.Schools
            .FirstOrDefaultAsync(s => s.Id == schoolId, cancellationToken)
            ?? throw new NotFoundException("École", schoolId);

        // Non rejouable : aucune commande ne remet IsProductionLocked à faux.
        if (school.IsProductionLocked)
        {
            throw new BusinessRuleException(
                "Cet établissement est déjà verrouillé définitivement.",
                "ALREADY_LOCKED");
        }

        EnsureConfirmed(request.Confirmation, school.Name);

        var lockedAt = timeProvider.GetUtcNow();
        school.IsProductionLocked = true;
        school.ProductionLockedAt = lockedAt;

        await dbContext.SaveChangesAsync(cancellationToken);

        // LogWarning et non LogInformation : une décision irréversible doit ressortir dans les
        // journaux d'exploitation sans avoir à les filtrer (même choix que GoLive et la purge).
        logger.LogWarning(
            "Verrouillage définitif de l'établissement {SchoolId} ({SchoolName}) demandé par l'utilisateur {UserId}, effectif au {LockedAt:o}.",
            schoolId, school.Name, currentUser.UserId, lockedAt);

        return new LockProductionResult(lockedAt);
    }

    private static void EnsureConfirmed(string confirmation, string schoolName)
    {
        if (LockProductionConfirmation.Matches(confirmation, schoolName))
        {
            return;
        }

        throw new ValidationException(
        [
            new ValidationFailure(
                "confirmation",
                $"Saisissez exactement « {LockProductionConfirmation.Keyword} » ou le nom de votre établissement pour confirmer.")
        ]);
    }
}
