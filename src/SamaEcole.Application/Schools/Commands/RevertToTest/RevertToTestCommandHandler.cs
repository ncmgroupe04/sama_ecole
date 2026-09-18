using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.Schools.Commands.RevertToTest;

/// <summary>
/// Repasse l'établissement courant en mode test (<c>WentLiveAt = null</c>), rendant la bascule
/// « Passer en mode réel » de nouveau jouable. Réservé au Directeur, confirmé par « TEST » (ou le nom
/// de l'école) — même garde que GoLiveCommandHandler.
///
/// Ne touche JAMAIS à <c>School.HasEverGoneLive</c> : la « Zone de danger » (purge) reste verrouillée
/// pour toujours dès qu'une école a un jour été réelle (AGENTS.md règle #6) — voir
/// ResetSchoolDataCommandHandler et la fonction PostgreSQL reset_school_data.
/// </summary>
public class RevertToTestCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    ILogger<RevertToTestCommandHandler> logger)
    : IRequestHandler<RevertToTestCommand, RevertToTestResult>
{
    public async Task<RevertToTestResult> Handle(RevertToTestCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Double garde du rôle : l'endpoint est déjà [Authorize(Roles = Directeur)], mais un
        // changement structurant ne doit pas dépendre d'un seul attribut qu'un futur refactor
        // pourrait déplacer (même défense que GoLiveCommandHandler).
        if (currentUser.Role != Role.Directeur)
        {
            throw new UnauthorizedAccessException("Seul le Directeur peut repasser l'établissement en mode test.");
        }

        var school = await dbContext.Schools
            .FirstOrDefaultAsync(s => s.Id == schoolId, cancellationToken)
            ?? throw new NotFoundException("École", schoolId);

        EnsureConfirmed(request.Confirmation, school.Name);

        var wasLive = school.WentLiveAt is not null;

        school.WentLiveAt = null;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "RETOUR MODE TEST de l'établissement {SchoolId} ({SchoolName}) par l'utilisateur {UserId} — était en mode réel : {WasLive}.",
            schoolId, school.Name, currentUser.UserId, wasLive);

        return new RevertToTestResult(wasLive);
    }

    private static void EnsureConfirmed(string confirmation, string schoolName)
    {
        if (RevertToTestConfirmation.Matches(confirmation, schoolName))
        {
            return;
        }

        throw new ValidationException(
        [
            new ValidationFailure(
                "confirmation",
                $"Saisissez exactement « {RevertToTestConfirmation.Keyword} » ou le nom de votre établissement pour confirmer.")
        ]);
    }
}
