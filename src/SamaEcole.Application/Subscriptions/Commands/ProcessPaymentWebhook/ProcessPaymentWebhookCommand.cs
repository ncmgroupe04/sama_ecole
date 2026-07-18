using MediatR;

namespace SamaEcole.Application.Subscriptions.Commands.ProcessPaymentWebhook;

/// <summary>
/// Ticket JGK-I06 — POST /webhooks/payments/{provider}, entièrement anonyme (docs/Volume_4_API_Design.md
/// §2bis). <paramref name="RawBody"/> est transmis TEL QUEL (pas de désérialisation côté contrôleur) :
/// c'est IPaymentService.ParseWebhook, propre à l'agrégateur, qui sait comment le lire et vérifier sa
/// signature — un contrôleur qui désérialiserait lui-même imposerait une forme de corps unique à tous
/// les agrégateurs futurs.
/// </summary>
public record ProcessPaymentWebhookCommand(
    string Provider, string RawBody, string? ContentType, string? RemoteIpAddress) : IRequest<Unit>;
