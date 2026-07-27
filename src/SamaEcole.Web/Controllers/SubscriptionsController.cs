using SamaEcole.Application.Subscriptions.Commands.InitiateSubscriptionPayment;
using SamaEcole.Application.Subscriptions.Queries.GetSubscriptionPayments;
using SamaEcole.Application.Subscriptions.Queries.ValidatePromoCode;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.Authorization;
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
public class SubscriptionsController(ISender mediator, IAuthorizationService authorizationService) : ControllerBase
{
    public record InitiatePaymentRequest(
        SubscriptionPaymentMethod Method, BillingPeriod BillingPeriod, string? PromoCode = null);

    public record ValidatePromoCodeRequest(string Code, BillingPeriod BillingPeriod);

    [HttpPost("{schoolId:guid}/payments")]
    [ProducesResponseType<InitiateSubscriptionPaymentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> InitiatePayment(
        Guid schoolId, [FromBody] InitiatePaymentRequest request, CancellationToken cancellationToken)
    {
        await EnsureSchoolResourceAccessAsync(schoolId);

        var result = await mediator.Send(
            new InitiateSubscriptionPaymentCommand
            {
                SchoolId = schoolId,
                Method = request.Method,
                BillingPeriod = request.BillingPeriod,
                PromoCode = request.PromoCode
            },
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Aperçu en direct d'un code promo — module Tarification &amp; Promotions. Aucun SchoolId en
    /// route : le tenant vient de la session (ITenantProvider), comme partout ailleurs — un Directeur
    /// ne peut prévisualiser que SON PROPRE abonnement. Déjà couvert par l'allowlist
    /// SubscriptionAwaitingPaymentMiddleware (préfixe /api/v1/subscriptions/) : accessible même en
    /// mode restreint, comme l'initiation de paiement elle-même.
    /// </summary>
    [HttpPost("validate-promo")]
    [ProducesResponseType<ValidatePromoCodeResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ValidatePromoCode(
        [FromBody] ValidatePromoCodeRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new ValidatePromoCodeQuery { Code = request.Code, BillingPeriod = request.BillingPeriod },
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
        await EnsureSchoolResourceAccessAsync(schoolId);

        var result = await mediator.Send(
            new GetSubscriptionPaymentsQuery { SchoolId = schoolId, Page = page, PageSize = pageSize },
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Policy resource-based réutilisable (audit BOLA/IDOR, voir SchoolResourceAuthorizationHandler) :
    /// remplace la comparaison manuelle « request.SchoolId != tenantProvider.CurrentSchoolId » qui vivait
    /// auparavant dans chaque Handler MediatR de ce module. Lève la même exception qu'avant
    /// (ExceptionHandlingMiddleware la traduit en 403 au format normalisé, AGENTS.md règle #9) : le
    /// comportement observable par le client est inchangé.
    /// </summary>
    private async Task EnsureSchoolResourceAccessAsync(Guid schoolId)
    {
        var authorization = await authorizationService.AuthorizeAsync(
            User, schoolId, SchoolResourcePolicies.CanAccessSchoolResource);

        if (!authorization.Succeeded)
        {
            throw new UnauthorizedAccessException("L'établissement de l'URL ne correspond pas à votre session.");
        }
    }
}
