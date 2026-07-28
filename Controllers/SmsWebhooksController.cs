using SamaEcole.Application.Notifications.Commands.ProcessSmsDeliveryReceipt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// POST /webhooks/sms/{provider} — accusés de réception (DLR) de l'agrégateur SMS. Entièrement
/// PUBLIC, exactement comme <see cref="PaymentWebhooksController"/> : c'est l'agrégateur qui appelle
/// depuis l'extérieur, sans JWT. [AllowAnonymous] est donc voulu, pas un oubli — la sécurité réelle
/// est la signature HMAC vérifiée dans le Handler, avant tout accès à la base.
///
/// Contrôleur mince (AGENTS.md règle #8) : il ne lit que le corps BRUT et l'en-tête de signature,
/// et les transmet tels quels. Le corps ne doit surtout pas être désérialisé puis re-sérialisé au
/// passage — la signature porte sur les octets exacts reçus, et un simple réagencement des clés
/// JSON suffirait à la faire échouer.
/// </summary>
[ApiController]
[Route("api/v1/webhooks/sms")]
[AllowAnonymous]
public class SmsWebhooksController(ISender mediator) : ControllerBase
{
    /// <summary>En-tête portant la signature. Configurable côté agrégateur, constant côté serveur.</summary>
    private const string SignatureHeader = "X-Signature";

    [HttpPost("{provider}")]
    [ProducesResponseType<SmsDeliveryReceiptResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Receive(string provider, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);

        var signature = Request.Headers.TryGetValue(SignatureHeader, out var values)
            ? values.ToString()
            : null;

        var result = await mediator.Send(
            new ProcessSmsDeliveryReceiptCommand(provider, rawBody, signature), cancellationToken);

        // 200 même quand aucun accusé n'a trouvé sa correspondance : un rejeu ou un identifiant
        // inconnu n'est pas une erreur de l'appelant, et un statut d'échec ferait réémettre
        // l'agrégateur en boucle.
        return Ok(result);
    }
}
