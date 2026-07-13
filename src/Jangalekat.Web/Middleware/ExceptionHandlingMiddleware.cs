using System.Net;
using System.Text.Json;
using Jangalekat.Application.Common.Exceptions;

namespace Jangalekat.Web.Middleware;

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
