using SamaEcole.Application.Auth.Commands.Login;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Auth.Commands.Refresh;

/// <summary>
/// Ticket JGK-A04 — échange d'un refresh token valide contre un nouveau couple de tokens.
///
/// ROTATION : le token présenté est révoqué et remplacé. Un refresh token ne sert donc qu'une fois.
///
/// DÉTECTION DE REJEU : si un token DÉJÀ révoqué est présenté, c'est soit un rejeu, soit un vol —
/// le client légitime, lui, a reçu le token suivant. On révoque alors TOUTE la famille de tokens de
/// l'utilisateur : l'attaquant comme la victime sont déconnectés, plutôt que de laisser l'attaquant
/// prolonger sa session en silence.
/// </summary>
public class RefreshTokenCommandHandler(
    IAuthStore authStore,
    IJwtTokenGenerator tokenGenerator,
    AuthSettings settings,
    TimeProvider timeProvider,
    ILogger<RefreshTokenCommandHandler> logger)
    : IRequestHandler<RefreshTokenCommand, AuthTokensResult>
{
    public async Task<AuthTokensResult> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var hash = RefreshTokenFactory.Hash(request.RefreshToken);
        var stored = await authStore.FindRefreshTokenAsync(hash, cancellationToken);

        if (stored is null)
        {
            logger.LogWarning("Refresh refusé : token inconnu.");
            throw new InvalidCredentialsException();
        }

        if (stored.RevokedAt is not null)
        {
            logger.LogWarning(
                "Refresh refusé : token DÉJÀ révoqué pour {UserId} — rejeu ou vol probable, révocation de tous ses tokens.",
                stored.UserId);

            await authStore.RevokeAllRefreshTokensAsync(stored.UserId, cancellationToken);
            throw new InvalidCredentialsException();
        }

        if (now >= stored.ExpiresAt)
        {
            logger.LogWarning("Refresh refusé : token expiré pour {UserId}.", stored.UserId);
            throw new InvalidCredentialsException();
        }

        // Le compte a pu être suspendu/bloqué depuis l'émission du token : on relit son état courant
        // au lieu de faire confiance à ce que contenait l'ancien JWT.
        var user = await authStore.FindUserByIdAsync(stored.UserId, cancellationToken);

        if (user is null || user.Status is not EntityStatus.Active)
        {
            logger.LogWarning("Refresh refusé : compte {UserId} absent ou inactif.", stored.UserId);
            await authStore.RevokeAllRefreshTokensAsync(stored.UserId, cancellationToken);
            throw new InvalidCredentialsException();
        }

        await authStore.RevokeRefreshTokenAsync(stored.Id, cancellationToken);

        return await LoginCommandHandler.IssueTokensAsync(
            authStore, tokenGenerator, settings, user, now, cancellationToken);
    }
}