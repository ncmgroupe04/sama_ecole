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
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Le client a coupé la connexion (onglet fermé, navigation, ou gestionnaire de
            // téléchargement qui intercepte le fetch d'un PDF). Plus personne pour lire une réponse :
            // tenter d'en écrire une relèverait, et journaliser une stack trace en Error noierait les
            // vraies erreurs sous le bruit des annulations. On sort en silence (trace Debug seulement).
            logger.LogDebug("Requête annulée par le client : {Method} {Path}", context.Request.Method, context.Request.Path);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var traceId = context.TraceIdentifier;

        // La réponse a déjà commencé à partir (en-têtes + une partie du corps envoyés) : impossible
        // d'y écrire une erreur normalisée — `Response.StatusCode = …` lèverait « response has already
        // started ». C'est le cas typique d'un `FileResult` volumineux dont le rendu casse en cours
        // d'écriture. On journalise et on coupe net la connexion : le client verra un transfert
        // incomplet (à retenter) plutôt qu'un corps tronqué présenté comme complet.
        if (context.Response.HasStarted)
        {
            logger.LogError(
                exception,
                "Exception APRÈS le début de la réponse ({Method} {Path}, {Written} octet(s) déjà écrits). " +
                "Connexion coupée — aucune erreur normalisée ne peut plus être renvoyée. TraceId: {TraceId}",
                context.Request.Method, context.Request.Path, context.Response.Headers.ContentLength, traceId);
            context.Abort();
            return;
        }

        var (statusCode, code, message, details) = exception switch
        {
            ValidationException validationEx => (
                HttpStatusCode.UnprocessableEntity,
                "VALIDATION_ERROR",
                "Une ou plusieurs erreurs de validation se sont produites.",
                (object?)validationEx.Errors),

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
                businessRuleEx.Code ?? "BUSINESS_RULE_VIOLATION",
                businessRuleEx.Message,
                null),

            // Échec d'authentification (ticket JGK-A04). Message volontairement générique : il ne doit
            // jamais permettre de distinguer un e-mail inconnu d'un mot de passe faux.
            InvalidCredentialsException credentialsEx => (
                HttpStatusCode.Unauthorized,
                "INVALID_CREDENTIALS",
                credentialsEx.Message,
                null),

            // Blocage progressif anti-force-brute (docs/Volume_7_Security.md §2). Contrairement à
            // InvalidCredentialsException ci-dessus, révéler ce statut n'ouvre pas d'énumération : le
            // verrou ne se déclenche qu'après plusieurs échecs déjà commis sur CE compte.
            AccountLockedException lockedEx => (
                (HttpStatusCode)StatusCodes.Status429TooManyRequests,
                "ACCOUNT_LOCKED",
                lockedEx.Message,
                (object?)new { retryAfterSeconds = lockedEx.RetryAfterSeconds }),

            // Refus de portée dont le message a été rédigé POUR l'écran (classe non assignée, dossier
            // hors périmètre, créneau d'un collègue) : on le renvoie tel quel, avec l'action
            // corrective — docs/Volume_4_API_Design.md §22. Doit précéder le cas UnauthorizedAccessException
            // ci-dessous, dont il hérite.
            ForbiddenException forbiddenEx => (
                HttpStatusCode.Forbidden,
                "FORBIDDEN",
                forbiddenEx.Message,
                null),

            // Garde interne « ne devrait jamais arriver » (« Tenant is required. », « Utilisateur
            // courant inconnu. »…) : message aplati, il ne doit pas fuir de vocabulaire technique.
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

        if (exception is AccountLockedException accountLockedEx)
        {
            context.Response.Headers.RetryAfter = accountLockedEx.RetryAfterSeconds.ToString();
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var payload = new { code, message, details, traceId };
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
