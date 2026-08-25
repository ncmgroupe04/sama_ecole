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

            // Doublon refusé par la base : la ligne existe déjà. Doit précéder le cas
            // ConcurrencyConflictException ci-dessous — dont il hérite — sinon il ne serait jamais
            // atteint, et l'utilisateur relirait « modifié par une autre personne » alors que
            // personne n'a rien modifié. Code distinct pour que le client puisse les différencier.
            DuplicateRecordException duplicateEx => (
                HttpStatusCode.Conflict,
                "DUPLICATE_RECORD",
                duplicateEx.Message,
                null),

            ConcurrencyConflictException concurrencyEx => (
                HttpStatusCode.Conflict,
                "CONCURRENCY_CONFLICT",
                concurrencyEx.Message,
                null),

            // Règle métier qui bloque une opération à cause de l'état actuel de la ressource (ex.
            // suppression d'une classe encore liée à des élèves, annulation d'une inscription déjà
            // encaissée) — pas une écriture concurrente, mais un conflit avec l'état existant.
            BusinessRuleException businessRuleEx => (
                HttpStatusCode.Conflict,
                "BUSINESS_RULE_VIOLATION",
                businessRuleEx.Message,
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

            OperationCanceledException => (
                (HttpStatusCode)499, // Client Closed Request
                "CLIENT_CLOSED_REQUEST",
                "La requête a été annulée par le client.",
                null),

            NotFoundException => (
                HttpStatusCode.NotFound,
                "NOT_FOUND",
                exception.Message,
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

        // Le conflit d'écriture est le seul cas où le message envoyé au client a été volontairement
        // DÉPOUILLÉ de sa cause technique (table, contrainte). Sans cette trace, plus personne côté
        // serveur ne saurait quelle contrainte a réellement cédé, et le catalogue de messages
        // deviendrait impossible à compléter — d'où ce journal, corrélé par le même TraceId que la
        // réponse renvoyée à l'utilisateur.
        if (exception is ConcurrencyConflictException conflict)
        {
            logger.LogWarning(
                "Conflit d'écriture ({Code}) sur {TechnicalDetail}. TraceId: {TraceId}",
                code, conflict.TechnicalDetail, traceId);
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var payload = new { code, message, details, traceId };
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
