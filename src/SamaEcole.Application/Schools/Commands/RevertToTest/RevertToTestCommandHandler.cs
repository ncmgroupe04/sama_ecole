using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Schools.Commands.RevertToTest;

/// <summary>
/// Repasse l'établissement courant en mode test (<c>WentLiveAt = null</c>), rendant la « Zone de
/// danger » de nouveau disponible.
///
/// DOUBLE GARDE avec le drapeau d'environnement : l'endpoint n'est monté que si
/// <see cref="ISandboxModeProvider.RevertToTestEnabled"/> (sinon la route n'existe pas), et ce
/// Handler le revérifie — une requête qui parviendrait jusqu'ici sur un environnement où le retour
/// est interdit reçoit un 404, jamais un effet.
/// </summary>
public class RevertToTestCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    ISandboxModeProvider sandboxMode,
    ILogger<RevertToTestCommandHandler> logger)
    : IRequestHandler<RevertToTestCommand, RevertToTestResult>
{
    public async Task<RevertToTestResult> Handle(RevertToTestCommand request, CancellationToken cancellationToken)
    {
        // Défense en profondeur : la route n'est pas censée exister quand c'est faux (elle n'est
        // montée que si revertToTestEnabled, voir Program.cs), mais on ne laisse aucune chance à un
        // mauvais câblage de rendre le retour possible en production. 404, comme une route absente.
        if (!sandboxMode.RevertToTestEnabled)
        {
            throw new NotFoundException("Cette fonctionnalité n'est pas disponible sur cet environnement.");
        }

        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        if (currentUser.Role != Role.Directeur)
        {
            throw new UnauthorizedAccessException("Seul le Directeur peut repasser l'établissement en mode test.");
        }

        var school = await dbContext.Schools
            .FirstOrDefaultAsync(s => s.Id == schoolId, cancellationToken)
            ?? throw new NotFoundException("École", schoolId);

        var wasLive = school.WentLiveAt is not null;

        school.WentLiveAt = null;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "RETOUR MODE TEST (recette) de l'établissement {SchoolId} ({SchoolName}) par l'utilisateur {UserId} — était en mode réel : {WasLive}.",
            schoolId, school.Name, currentUser.UserId, wasLive);

        return new RevertToTestResult(wasLive);
    }
}
