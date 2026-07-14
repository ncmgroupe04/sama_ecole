using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Auth.Commands.Login;

/// <summary>
/// Ticket JGK-A04 — authentification par e-mail + mot de passe.
///
/// Toutes les branches d'échec lèvent la MÊME InvalidCredentialsException (401) : distinguer
/// « e-mail inconnu » de « mot de passe faux » ou de « compte verrouillé » permettrait d'énumérer
/// les comptes de la plateforme. Le motif réel est journalisé, jamais renvoyé.
/// </summary>
public class LoginCommandHandler(
    IAuthStore authStore,
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
            throw new InvalidCredentialsException();
        }

        if (user.Status is not EntityStatus.Active)
        {
            // Un compte suspendu ou bloqué ne doit plus obtenir de token (ticket JGK-A05).
            logger.LogWarning("Échec de connexion : compte {UserId} au statut {Status}.", user.Id, user.Status);
            throw new InvalidCredentialsException();
        }

        if (!passwordHasher.Verify(user.PasswordHash, request.Password))
        {
            await authStore.RecordLoginAttemptAsync(
                user.Id, success: false, settings.MaxFailedAttempts, settings.LockoutMinutes, cancellationToken);

            logger.LogWarning("Échec de connexion : mot de passe invalide pour {UserId}.", user.Id);
            throw new InvalidCredentialsException();
        }

        await authStore.RecordLoginAttemptAsync(
            user.Id, success: true, settings.MaxFailedAttempts, settings.LockoutMinutes, cancellationToken);

        return await IssueTokensAsync(authStore, tokenGenerator, settings, user, now, cancellationToken);
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
        var accessToken = tokenGenerator.Generate(user.Id, user.SchoolId, user.Role);

        var refreshToken = RefreshTokenFactory.Create();
        await authStore.StoreRefreshTokenAsync(
            user.Id,
            RefreshTokenFactory.Hash(refreshToken),
            now.AddDays(settings.RefreshTokenDays),
            cancellationToken);

        return new AuthTokensResult(accessToken.Value, refreshToken, accessToken.ExpiresInSeconds);
    }
}