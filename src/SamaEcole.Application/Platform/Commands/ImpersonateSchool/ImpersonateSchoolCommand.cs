using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Platform.Commands.ImpersonateSchool;

/// <summary>
/// POST /admin/platform/schools/{schoolId}/impersonate — bouton « Infiltrer » de l'écran
/// Établissements. Réservé au Super Admin.
///
/// Émet un jeton de MÊME format qu'un jeton normal (sub/schoolId/role, AGENTS.md règle #10) mais
/// portant l'identité du DIRECTEUR de l'école ciblée, pas celle du Super Admin : c'est ce jeton, une
/// fois posé côté client, qui fait passer la session dans le tenant visé — la RLS et
/// [Authorize(Roles = ...)] ne voient aucune différence avec une connexion normale du Directeur.
///
/// Deux garde-fous, volontaires :
///   - AUCUN refresh token n'accompagne ce jeton : une session d'impersonation ne se renouvelle
///     jamais, elle expire pour de bon avec l'access token (~15 min, JwtOptions.AccessTokenMinutes).
///     Pour sortir plus tôt, le client échange simplement son propre cookie de refresh (resté intact,
///     jamais touché ici) contre ses jetons Super Admin d'origine.
///   - Une entrée d'audit OBLIGATOIRE est écrite ICI, à la main (comme ApproveRegistrationRequest) :
///     l'acteur réel (Super Admin) n'a pas de SchoolId propre, le mécanisme générique
///     AuditLoggingBehavior ne saurait à qui l'imputer. Elle est attribuée à l'école CIBLE — c'est là
///     que le Directeur la verra dans son propre journal d'audit (Volume 7 §7/§15).
/// </summary>
public record ImpersonateSchoolCommand(Guid SchoolId) : IRequest<ImpersonateSchoolResult>;

/// <summary>AccessToken/ExpiresIn : mêmes noms que AuthTokensResponse (openapi.yaml) — le client
/// réutilise la même auth.saveSession() côté navigateur pour les deux, un nom différent la casserait.</summary>
public record ImpersonateSchoolResult(string AccessToken, int ExpiresIn, Guid SchoolId, string SchoolName);

public class ImpersonateSchoolCommandHandler(
    IApplicationDbContext dbContext,
    IAuthStore authStore,
    IJwtTokenGenerator tokenGenerator,
    IAuditLogStore auditLogStore,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    ILogger<ImpersonateSchoolCommandHandler> logger)
    : IRequestHandler<ImpersonateSchoolCommand, ImpersonateSchoolResult>
{
    public async Task<ImpersonateSchoolResult> Handle(
        ImpersonateSchoolCommand request, CancellationToken cancellationToken)
    {
        var superAdminId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Aucun utilisateur authentifié.");

        // `schools` n'est PAS sous RLS (AGENTS.md règle #2, voir ApproveRegistrationRequestHandler) :
        // un Super Admin sans schoolId le lit normalement via EF.
        var school = await dbContext.Schools
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == request.SchoolId, cancellationToken)
            ?? throw new KeyNotFoundException("Établissement introuvable.");

        // `users` EST sous RLS : retrouver le Directeur cible passe par la fonction SECURITY DEFINER
        // auth_find_active_director_by_school (migration AddPlatformSubscriptionsAndImpersonation).
        var director = await authStore.FindActiveDirectorForSchoolAsync(request.SchoolId, cancellationToken)
            ?? throw new BusinessRuleException(
                "Cet établissement n'a aucun compte Directeur actif : impossible de l'infiltrer.");

        var now = timeProvider.GetUtcNow();

        var accessToken = tokenGenerator.GenerateImpersonation(
            director.Id, director.FullName, school.Id, director.Role, superAdminId);

        // Entrée d'audit AVANT de renvoyer le jeton : une panne d'écriture ici ne doit jamais laisser
        // partir un accès non tracé.
        await auditLogStore.AppendAsync(
            school.Id, superAdminId, "Platform", "ImpersonateSchool",
            success: true, failureReason: null, currentUser.IpAddress, now, cancellationToken);

        logger.LogWarning(
            "Super Admin {SuperAdminId} a émis un jeton d'impersonation pour l'établissement {SchoolId} (Directeur {DirectorId}).",
            superAdminId, school.Id, director.Id);

        return new ImpersonateSchoolResult(
            accessToken.Value, accessToken.ExpiresInSeconds, school.Id, school.Name);
    }
}
