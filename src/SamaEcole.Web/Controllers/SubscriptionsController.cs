using SamaEcole.Application.Subscriptions.Commands.InitiateSubscriptionPayment;
using SamaEcole.Application.Subscriptions.Queries.GetSubscriptionPayments;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Ticket JGK-I05 — initiation de paiement d'abonnement (docs/Volume_4_API_Design.md §2bis). RÉSERVÉ AU
/// DIRECTEUR (seul rôle qui régularise l'abonnement de son établissement). Route explicitement
/// allowlistée par SubscriptionAwaitingPaymentMiddleware (préfixe /api/v1/subscriptions/, ticket
/// JGK-I04) : c'est la seule voie de sortie du mode restreint, elle ne doit jamais être elle-même bloquée.
///
/// Contrôleur mince (AGENTS.md règle #8) : calcul du montant, appel à l'agrégateur et persistance
/// vivent dans le Handler.
/// </summary>
[ApiController]
[Route("api/v1/subscriptions")]
[Authorize(Roles = nameof(Role.Directeur))]
public class SubscriptionsController(ISender mediator) : ControllerBase
{
    public record InitiatePaymentRequest(SubscriptionPaymentMethod Method, BillingPeriod BillingPeriod);

    [HttpPost("{schoolId:guid}/payments")]
    [ProducesResponseType<InitiateSubscriptionPaymentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> InitiatePayment(
        Guid schoolId, [FromBody] InitiatePaymentRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new InitiateSubscriptionPaymentCommand
            {
                SchoolId = schoolId,
                Method = request.Method,
                BillingPeriod = request.BillingPeriod
            },
            cancellationToken);

        return Ok(result);
    }

    /// <summary>Ticket JGK-I07 — historique des paiements de l'établissement, pour l'écran "Facturation &amp; Historique".</summary>
    [HttpGet("{schoolId:guid}/payments")]
    [ProducesResponseType<PaginatedSubscriptionPayments>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListPayments(
        Guid schoolId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(
            new GetSubscriptionPaymentsQuery { SchoolId = schoolId, Page = page, PageSize = pageSize },
            cancellationToken);

        return Ok(result);
    }
}
