using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Auth.Commands.SwitchSchool;

/// <summary>
/// POST /auth/switch-school — bascule la session vers un autre établissement du groupe scolaire.
///
/// PRINCIPE DE SÛRETÉ : un jeton ne porte JAMAIS deux établissements. Basculer consiste à en émettre
/// un NOUVEAU pour l'école cible — exactement le mécanisme d'ImpersonateSchoolCommand. Conséquence
/// directe : la RLS PostgreSQL, les Global Query Filters et [Authorize] restent inchangés, il n'y a
/// à aucun instant plus d'un tenant actif. Faire porter plusieurs SchoolId à un jeton aurait au
/// contraire exigé de réécrire l'isolation de bout en bout (AGENTS.md règles #2 et #10).
///
/// L'appartenance est vérifiée EN BASE à chaque bascule, jamais déduite du jeton présenté : un
/// rattachement retiré par le Super Admin doit fermer la porte immédiatement, sans attendre
/// l'expiration du jeton en circulation.
/// </summary>
public record SwitchSchoolCommand(Guid SchoolId) : IRequest<SwitchSchoolResult>;

/// <summary>AccessToken/ExpiresIn : mêmes noms qu'AuthTokensResponse — le client réutilise la même
/// auth.saveSession() côté navigateur, un nom différent la casserait.</summary>
public record SwitchSchoolResult(string AccessToken, int ExpiresIn, Guid SchoolId, string SchoolName);

public class SwitchSchoolCommandHandler(
    IApplicationDbContext dbContext,
    IAuthStore authStore,
    IJwtTokenGenerator tokenGenerator,
    IAuditLogStore auditLogStore,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    ILogger<SwitchSchoolCommandHandler> logger)
    : IRequestHandler<SwitchSchoolCommand, SwitchSchoolResult>
{
    public async Task<SwitchSchoolResult> Handle(
        SwitchSchoolCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Aucun utilisateur authentifié.");

        // `users` est sous RLS et la session courante appartient à une AUTRE école que la cible :
        // la relecture du compte passe donc par la même porte SECURITY DEFINER que le login.
        var user = await authStore.FindUserByIdAsync(userId, cancellationToken)
            ?? throw new KeyNotFoundException("Compte introuvable.");

        if (user.Status != EntityStatus.Active)
        {
            throw new BusinessRuleException("Votre compte n'est pas actif.");
        }

        // Appartenance vérifiée en base — c'est LA garde de cette commande. Sans elle, n'importe quel
        // utilisateur authentifié obtiendrait un jeton pour l'école de son choix.
        var isAttached = await dbContext.UserSchools
            .AsNoTracking()
            .AnyAsync(us => us.UserId == userId && us.SchoolId == request.SchoolId, cancellationToken);

        // L'école d'origine du compte reste toujours accessible, même sans ligne de rattachement :
        // sinon un promoteur ayant basculé ne pourrait jamais revenir chez lui.
        if (!isAttached && user.SchoolId != request.SchoolId)
        {
            throw new UnauthorizedAccessException("Cet établissement ne vous est pas rattaché.");
        }

        var school = await dbContext.Schools
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == request.SchoolId, cancellationToken)
            ?? throw new KeyNotFoundException("Établissement introuvable.");

        if (school.Status != EntityStatus.Active)
        {
            throw new BusinessRuleException("Cet établissement n'est pas actif.");
        }

        var accessToken = tokenGenerator.Generate(user.Id, user.FullName, school.Id, user.Role);

        // Journalisée sous l'école CIBLE : c'est là qu'un audit doit voir arriver cette session.
        await auditLogStore.AppendAsync(
            school.Id, user.Id, "Auth", "SwitchSchool",
            success: true, failureReason: null, currentUser.IpAddress, timeProvider.GetUtcNow(), cancellationToken);

        logger.LogInformation(
            "Utilisateur {UserId} a basculé vers l'établissement {SchoolId}.", user.Id, school.Id);

        return new SwitchSchoolResult(accessToken.Value, accessToken.ExpiresInSeconds, school.Id, school.Name);
    }
}
