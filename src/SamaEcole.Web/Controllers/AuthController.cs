using SamaEcole.Application.Auth;
using SamaEcole.Application.Auth.Commands.Login;
using SamaEcole.Application.Auth.Commands.Logout;
using SamaEcole.Application.Auth.Commands.Refresh;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Web.Auth;
using SamaEcole.Web.Contracts;
using SamaEcole.Web.RateLimiting;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Ticket JGK-A04 — /auth/login, /auth/refresh, /auth/logout (openapi.yaml).
/// Contrôleur mince : aucune logique métier ici (AGENTS.md règle #8).
///
/// Le refresh token ne quitte le serveur que dans un cookie HttpOnly
/// (docs/Volume_4_API_Design.md §1.1) — voir <see cref="RefreshTokenCookie"/> pour le pourquoi.
/// Le client JavaScript ne manipule donc que l'access token.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public class AuthController(
    ISender mediator,
    AuthSettings settings,
    TimeProvider timeProvider) : ControllerBase
{
    /// <summary>login et refresh sont anonymes : c'est justement leur rôle de délivrer un token.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(SensitiveEndpointRateLimiting.LoginPolicyName)]
    [ProducesResponseType<AuthTokensResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginCommand command, CancellationToken cancellationToken)
    {
        var tokens = await mediator.Send(command, cancellationToken);
        return Ok(IssueRefreshCookie(tokens));
    }

    /// <summary>
    /// Aucun corps de requête : le refresh token est lu dans le cookie, jamais dans une donnée que
    /// le client pourrait fabriquer ou qu'une XSS pourrait lire.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<AuthTokensResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        // Cookie absent → même 401 générique qu'un jeton invalide : rien ne doit permettre de
        // distinguer les deux cas.
        var refreshToken = RefreshTokenCookie.Read(Request);

        if (string.IsNullOrEmpty(refreshToken))
        {
            throw new InvalidCredentialsException();
        }

        AuthTokensResult tokens;

        try
        {
            tokens = await mediator.Send(
                new RefreshTokenCommand { RefreshToken = refreshToken }, cancellationToken);
        }
        catch (InvalidCredentialsException)
        {
            // Le jeton est mort (expiré, révoqué, rejoué) : purger le cookie, sinon le navigateur
            // continuerait à le présenter à chaque tentative jusqu'à sa date d'expiration.
            RefreshTokenCookie.Delete(Response);
            throw;
        }

        return Ok(IssueRefreshCookie(tokens));
    }

    /// <summary>Authentifié : l'utilisateur à déconnecter est lu dans le JWT, jamais dans la requête.</summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await mediator.Send(new LogoutCommand(), cancellationToken);

        RefreshTokenCookie.Delete(Response);
        return NoContent();
    }

    /// <summary>
    /// Pose le refresh token en cookie et ne renvoie au client que ce qu'il a le droit de voir.
    /// Le cookie expire en même temps que le jeton qu'il porte (AuthSettings.RefreshTokenDays).
    /// </summary>
    private AuthTokensResponse IssueRefreshCookie(AuthTokensResult tokens)
    {
        RefreshTokenCookie.Set(
            Response,
            tokens.RefreshToken,
            timeProvider.GetUtcNow().AddDays(settings.RefreshTokenDays));

        return new AuthTokensResponse(tokens.AccessToken, tokens.ExpiresIn);
    }
}
