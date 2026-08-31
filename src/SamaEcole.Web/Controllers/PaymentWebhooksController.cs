using SamaEcole.Application.Subscriptions.Commands.ProcessPaymentWebhook;
using SamaEcole.Web.RateLimiting;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Ticket JGK-I06 — POST /webhooks/payments/{provider} (docs/Volume_4_API_Design.md §2bis). Entièrement
/// PUBLIC : c'est l'agrégateur de paiement (PayDunya…) qui appelle cette route depuis l'extérieur, sans
/// JWT — [AllowAnonymous] est donc voulu, pas un oubli. La sécurité réelle vit dans le Handler (signature
/// + vérification IPN serveur à serveur, docs/Volume_7_Security.md §12bis), jamais dans une couche
/// d'authentification classique qu'un agrégateur ne pourrait de toute façon pas satisfaire.
///
/// Contrôleur mince (AGENTS.md règle #8) : ne lit que le corps BRUT et le transmet tel quel — c'est
/// IPaymentService, propre à chaque agrégateur, qui sait le décoder et vérifier sa signature.
/// </summary>
[ApiController]
[Route("api/v1/webhooks/payments")]
[AllowAnonymous]
[EnableRateLimiting(SensitiveEndpointRateLimiting.WebhookInboundPolicyName)]
public class PaymentWebhooksController(ISender mediator) : ControllerBase
{
    [HttpPost("{provider}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Receive(string provider, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);

        await mediator.Send(
            new ProcessPaymentWebhookCommand(
                provider, rawBody, Request.ContentType, HttpContext.Connection.RemoteIpAddress?.ToString()),
            cancellationToken);

        // 200 générique, sans corps métier : c'est le contrat attendu par la plupart des agrégateurs
        // (un accusé de réception simple), pas une réponse à interpréter côté PayDunya.
        return Ok();
    }
}
