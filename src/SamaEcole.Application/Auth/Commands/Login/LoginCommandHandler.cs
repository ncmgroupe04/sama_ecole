using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Auth.Commands.Login;

/// <summary>
/// Ticket JGK-A04 — authentification par e-mail + mot de passe.
///
/// Toutes les branches d'échec lèvent la MÊME InvalidCredentialsException (401) : distinguer
/// « e-mail inconnu » de « mot de passe faux » ou de « compte verrouillé » permettrait d'énumérer
/// les comptes de la plateforme. Le motif réel est journalisé, jamais renvoyé.
///
/// Journal d'audit (JGK-H01) : écrit ICI, à la main, plutôt que via AuditLoggingBehavior — le login
/// s'exécute AVANT qu'un tenant existe, le mécanisme générique (qui lit tenantProvider.CurrentSchoolId)
/// ne peut donc rien journaliser. IAuditLogStore contourne la RLS comme IAuthStore le fait déjà pour
/// `users`. Un e-mail INCONNU n'a ni utilisateur ni école à qui l'imputer : volontairement pas
/// journalisé dans cette table tenant (bruit de sécurité, pas un événement d'établissement). Un
/// Super Admin qui se connecte n'a lui non plus aucune école : également hors de cette table (relève
/// de PlatformAuditLogs, hors périmètre MVP — docs/Volume_3_DDS.md §2.3).
/// </summary>
public class LoginCommandHandler(
    IApplicationDbContext dbContext,
    IAuthStore authStore,
    IAuditLogStore auditLogStore,
    ICurrentUserService currentUser,
    IPasswordHasher passwordHasher,
    IJwtTokenGenerator tokenGenerator,
    AuthSettings settings,
    TimeProvider timeProvider,
    ILogger<LoginCommandHandler> logger)
    : IRequestHandler<LoginCommand, AuthTokensResult>
{
    public async Task<AuthTokensResult> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var user = await authStore.FindUserByEmailAsync(request.Email, cancellationToken);

        if (user is null)
        {
            // On hache quand même, pour rien : sans ce travail équivalent, une réponse quasi
            // instantanée trahirait qu'aucun compte ne porte cet e-mail (attaque temporelle par
            // énumération). Hash() coûte le même PBKDF2 que Verify().
            passwordHasher.Hash(request.Password);
            logger.LogWarning("Échec de connexion : e-mail inconnu.");
            throw new InvalidCredentialsException();
        }

        if (user.LockoutEndAt is { } lockoutEnd && now < lockoutEnd)
        {
            logger.LogWarning("Échec de connexion : compte {UserId} verrouillé jusqu'à {LockoutEnd}.", user.Id, lockoutEnd);
            await TryAuditAsync(user, success: false, $"Compte verrouillé jusqu'à {lockoutEnd:u}.", now, cancellationToken);
            throw new InvalidCredentialsException();
        }

        if (user.Status is not EntityStatus.Active)
        {
            // Un compte suspendu ou bloqué ne doit plus obtenir de token (ticket JGK-A05).
            logger.LogWarning("Échec de connexion : compte {UserId} au statut {Status}.", user.Id, user.Status);
            await TryAuditAsync(user, success: false, $"Compte au statut {user.Status}.", now, cancellationToken);
            throw new InvalidCredentialsException();
        }

        if (user.SchoolId is { } schoolId)
        {
            // `schools` n'est pas sous RLS (elle définit le tenant) : lisible normalement même avant
            // qu'un tenant existe. Un établissement Suspendu/Bloqué (Super Admin, ticket JGK-B01) ne
            // doit plus délivrer de token — même raisonnement que le statut du COMPTE ci-dessus.
            var school = await dbContext.Schools
                .AsNoTracking()
                .SingleOrDefaultAsync(s => s.Id == schoolId, cancellationToken);

            if (school is null || school.Status is not EntityStatus.Active)
            {
                logger.LogWarning(
                    "Échec de connexion : établissement {SchoolId} au statut {Status}.", schoolId, school?.Status);
                await TryAuditAsync(user, success: false, $"Établissement au statut {school?.Status}.", now, cancellationToken);
                throw new InvalidCredentialsException();
            }
        }

        if (!passwordHasher.Verify(user.PasswordHash, request.Password))
        {
            await authStore.RecordLoginAttemptAsync(
                user.Id, success: false, settings.MaxFailedAttempts, settings.LockoutMinutes, cancellationToken);

            logger.LogWarning("Échec de connexion : mot de passe invalide pour {UserId}.", user.Id);
            await TryAuditAsync(user, success: false, "Mot de passe invalide.", now, cancellationToken);
            throw new InvalidCredentialsException();
        }

        await authStore.RecordLoginAttemptAsync(
            user.Id, success: true, settings.MaxFailedAttempts, settings.LockoutMinutes, cancellationToken);

        await TryAuditAsync(user, success: true, failureReason: null, now, cancellationToken);

        return await IssueTokensAsync(authStore, tokenGenerator, settings, user, now, cancellationToken);
    }

    /// <summary>
    /// N'écrit rien si l'utilisateur n'a aucune école (Super Admin) : audit_logs est une table
    /// TENANT, une entrée sans SchoolId déterminable n'y a pas sa place (voir la remarque de classe).
    /// </summary>
    private async Task TryAuditAsync(
        AuthUser user, bool success, string? failureReason, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        if (user.SchoolId is not { } schoolId)
        {
            return;
        }

        await auditLogStore.AppendAsync(
            schoolId, user.Id, "Auth", "Login", success, failureReason, currentUser.IpAddress, occurredAt, cancellationToken);
    }

    /// <summary>
    /// Émet le couple access + refresh token. Partagé avec le refresh pour que la rotation produise
    /// exactement le même format de tokens que la connexion initiale.
    /// </summary>
    internal static async Task<AuthTokensResult> IssueTokensAsync(
        IAuthStore authStore,
        IJwtTokenGenerator tokenGenerator,
        AuthSettings settings,
        AuthUser user,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var accessToken = tokenGenerator.Generate(user.Id, user.FullName, user.SchoolId, user.Role);

        var refreshToken = RefreshTokenFactory.Create();
        await authStore.StoreRefreshTokenAsync(
            user.Id,
            RefreshTokenFactory.Hash(refreshToken),
            now.AddDays(settings.RefreshTokenDays),
            cancellationToken);

        return new AuthTokensResult(accessToken.Value, refreshToken, accessToken.ExpiresInSeconds);
    }
}