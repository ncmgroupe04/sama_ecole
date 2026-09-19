using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.Schools.Commands.GoLive;

/// <summary>
/// Fait passer l'établissement courant en mode réel. Comme <c>ResetSchoolDataCommandHandler</c>, le
/// Handler ne fait que garder la porte — résoudre le tenant, vérifier le rôle, exiger le mot de
/// confirmation, refuser un second passage — puis pose l'horodatage sur <c>schools</c>.
///
/// Bascule PLEINEMENT réversible (voir <c>RevertToTestCommand</c>) : elle ne verrouille plus jamais la
/// « Zone de danger » par effet de bord. Seul <c>LockProductionCommand</c>, une action manuelle
/// distincte, pose ce verrou (School.IsProductionLocked).
///
/// <c>schools</c> échappe à la RLS (elle DÉFINIT le tenant) : c'est le filtre applicatif sur l'Id du
/// JWT qui tient lieu d'isolation ici, jamais une école désignée par le client (règle #10).
/// </summary>
public class GoLiveCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    ILogger<GoLiveCommandHandler> logger)
    : IRequestHandler<GoLiveCommand, GoLiveResult>
{
    public async Task<GoLiveResult> Handle(GoLiveCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Double garde du rôle : l'endpoint est déjà [Authorize(Roles = Directeur)], mais un
        // changement structurant irréversible ne doit pas dépendre d'un seul attribut qu'un futur
        // refactor pourrait déplacer (même défense que ResetSchoolDataCommandHandler).
        if (currentUser.Role != Role.Directeur)
        {
            throw new UnauthorizedAccessException("Seul le Directeur peut faire passer l'établissement en mode réel.");
        }

        var school = await dbContext.Schools
            .FirstOrDefaultAsync(s => s.Id == schoolId, cancellationToken)
            ?? throw new NotFoundException("École", schoolId);

        // Non rejouable : une fois daté, on n'y revient pas (sauf porte de recette RepasserEnModeTest).
        if (school.WentLiveAt is { } alreadyLiveAt)
        {
            throw new BusinessRuleException(
                $"Cet établissement est déjà en mode réel depuis le {alreadyLiveAt:dd/MM/yyyy}.",
                "ALREADY_LIVE");
        }

        EnsureConfirmed(request.Confirmation, school.Name);

        var wentLiveAt = timeProvider.GetUtcNow();
        school.WentLiveAt = wentLiveAt;

        // Bascule PLEINEMENT réversible depuis le 19/09/2026 : elle ne pose plus aucun verrou sur la
        // « Zone de danger ». Seul LockProductionCommand, une action manuelle et distincte, verrouille
        // désormais la purge de façon définitive (School.IsProductionLocked).
        await dbContext.SaveChangesAsync(cancellationToken);

        // LogWarning et non LogInformation : une bascule définitive doit ressortir dans les journaux
        // d'exploitation sans avoir à les filtrer (même choix que la purge).
        logger.LogWarning(
            "Passage en mode réel de l'établissement {SchoolId} ({SchoolName}) demandé par l'utilisateur {UserId}, effectif au {WentLiveAt:o}.",
            schoolId, school.Name, currentUser.UserId, wentLiveAt);

        return new GoLiveResult(wentLiveAt);
    }

    private static void EnsureConfirmed(string confirmation, string schoolName)
    {
        if (GoLiveConfirmation.Matches(confirmation, schoolName))
        {
            return;
        }

        throw new ValidationException(
        [
            new ValidationFailure(
                "confirmation",
                $"Saisissez exactement « {GoLiveConfirmation.Keyword} » ou le nom de votre établissement pour confirmer.")
        ]);
    }
}
