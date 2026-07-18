using System.Net;
using System.Text.Json;
using SamaEcole.Application.Common.Exceptions;

namespace SamaEcole.Web.Middleware;

/// <summary>
/// Traduit toute exception applicative dans le format d'erreur normalisé de
/// docs/Volume_4_API_Design.md §0.4. AGENTS.md règle #9 : jamais d'exception brute renvoyée au client.
/// </summary>
public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var traceId = context.TraceIdentifier;

        var (statusCode, code, message, details) = exception switch
        {
            ValidationException validationEx => (
                HttpStatusCode.UnprocessableEntity,
                "VALIDATION_ERROR",
                "Une ou plusieurs erreurs de validation se sont produites.",
                validationEx.Errors),

            ConcurrencyConflictException concurrencyEx => (
                HttpStatusCode.Conflict,
                "CONCURRENCY_CONFLICT",
                concurrencyEx.Message,
                null),

            // Échec d'authentification (ticket JGK-A04). Message volontairement générique : il ne doit
            // jamais permettre de distinguer un e-mail inconnu d'un mot de passe faux.
            InvalidCredentialsException credentialsEx => (
                HttpStatusCode.Unauthorized,
                "INVALID_CREDENTIALS",
                credentialsEx.Message,
                null),

            UnauthorizedAccessException => (
                HttpStatusCode.Forbidden,
                "FORBIDDEN",
                "Accès refusé.",
                null),

            KeyNotFoundException => (
                HttpStatusCode.NotFound,
                "NOT_FOUND",
                exception.Message,
                null),

            // Ticket JGK-I05 : l'agrégateur de paiement (PayDunya…) a refusé ou est injoignable. 502 —
            // le serveur, agissant comme passerelle, a reçu une réponse invalide de l'amont.
            PaymentProviderException paymentEx => (
                HttpStatusCode.BadGateway,
                "PAYMENT_PROVIDER_ERROR",
                paymentEx.Message,
                null),

            // Ticket JGK-I06, docs/Volume_7_Security.md §12bis : signature de webhook absente/invalide.
            InvalidWebhookSignatureException webhookEx => (
                HttpStatusCode.Unauthorized,
                "INVALID_WEBHOOK_SIGNATURE",
                webhookEx.Message,
                null),

            _ => (
                HttpStatusCode.InternalServerError,
                "INTERNAL_ERROR",
                "Une erreur inattendue s'est produite.",
                null)
        };

        if (statusCode == HttpStatusCode.InternalServerError)
        {
            logger.LogError(exception, "Erreur non gérée. TraceId: {TraceId}", traceId);
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var payload = new { code, message, details, traceId };
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
