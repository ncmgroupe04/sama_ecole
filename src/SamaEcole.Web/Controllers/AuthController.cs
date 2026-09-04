using SamaEcole.Application.Auth;
using SamaEcole.Application.Auth.Commands.ChangePassword;
using SamaEcole.Application.Auth.Commands.ForgotPassword;
using SamaEcole.Application.Auth.Commands.Login;
using SamaEcole.Application.Auth.Commands.Logout;
using SamaEcole.Application.Auth.Commands.Refresh;
using SamaEcole.Application.Auth.Commands.ResetPassword;
using SamaEcole.Application.Auth.Commands.SwitchSchool;
using SamaEcole.Application.Auth.Queries.GetMySchools;
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

    /// <summary>
    /// Demande un lien de réinitialisation (docs/Volume_4_API_Design.md §1). Anonyme par nature : celui
    /// qui a oublié son mot de passe ne peut pas s'authentifier.
    ///
    /// Renvoie TOUJOURS 202, que l'adresse existe ou non — le Handler ne lève jamais d'exception sur ce
    /// point. Répondre 404 sur une adresse inconnue ferait de cette route un oracle d'énumération de
    /// comptes, exploitable pour dresser la liste des utilisateurs de la plateforme.
    ///
    /// 202 (Accepted) plutôt que 200 : la remise de l'e-mail est asynchrone et hors du contrôle de
    /// l'API — nous accusons réception de la demande, nous ne garantissons pas une livraison.
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(SensitiveEndpointRateLimiting.PasswordResetPolicyName)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordCommand command, CancellationToken cancellationToken)
    {
        await mediator.Send(command, cancellationToken);

        return Accepted();
    }

    /// <summary>
    /// Applique le nouveau mot de passe à partir du jeton reçu par e-mail. Anonyme : le jeton fait
    /// office d'authentification, c'est tout l'objet du parcours.
    ///
    /// Un jeton inconnu, expiré, déjà consommé ou révoqué donne le MÊME 422, avec le même message :
    /// distinguer ces cas indiquerait à un attaquant qu'il a deviné un condensat valide.
    /// </summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting(SensitiveEndpointRateLimiting.PasswordResetPolicyName)]
    [ProducesResponseType<ResetPasswordResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);

        // Toutes les sessions viennent d'être coupées, y compris celle du navigateur courant s'il en
        // avait une : le cookie doit partir avec elles, sinon il serait présenté à chaque refresh
        // jusqu'à sa date d'expiration, pour un jeton désormais révoqué.
        RefreshTokenCookie.Delete(Response);

        return Ok(result);
    }

    /// <summary>
    /// Change le mot de passe du compte AUTHENTIFIÉ courant, qui doit prouver connaître l'actuel —
    /// contrairement à /reset-password (jeton par e-mail) et à PATCH /users/{id}/password (un
    /// Directeur fixe le mot de passe d'AUTRUI). Ouvert à tout rôle : Super Admin compris.
    ///
    /// Toutes les sessions viennent d'être coupées (voir ChangePasswordCommandHandler), y compris
    /// celle du navigateur courant : le cookie de refresh part avec elles, comme sur /reset-password.
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType<ChangePasswordResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);

        RefreshTokenCookie.Delete(Response);

        return Ok(result);
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
    /// Établissements entre lesquels l'utilisateur peut basculer (groupe scolaire). Liste VIDE pour
    /// un compte mono-école — le sélecteur ne s'affiche alors pas.
    /// </summary>
    [HttpGet("my-schools")]
    [Authorize]
    [ProducesResponseType<IReadOnlyList<SwitchableSchoolDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMySchools(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetMySchoolsQuery(), cancellationToken));

    public record SwitchSchoolRequest(Guid SchoolId);

    /// <summary>
    /// Bascule vers un autre établissement du groupe : émet un NOUVEAU jeton portant l'école cible
    /// (voir SwitchSchoolCommandHandler — jamais deux établissements dans un même jeton).
    ///
    /// Placé sous /auth/, préfixe allowlisté par SubscriptionAwaitingPaymentMiddleware : un promoteur
    /// dont l'école courante est en attente de paiement doit pouvoir rejoindre celles qui sont à jour.
    ///
    /// Le refresh token n'est PAS renouvelé : il porte l'identité du compte, pas l'établissement
    /// actif. Le renouvellement ramènera l'école d'origine, ce qui est le comportement attendu — une
    /// bascule vaut pour la session en cours, pas indéfiniment.
    /// </summary>
    [HttpPost("switch-school")]
    [Authorize]
    [ProducesResponseType<SwitchSchoolResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SwitchSchool(
        [FromBody] SwitchSchoolRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new SwitchSchoolCommand(request.SchoolId), cancellationToken));

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
