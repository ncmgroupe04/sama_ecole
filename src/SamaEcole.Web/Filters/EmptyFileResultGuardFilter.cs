using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace SamaEcole.Web.Filters;

/// <summary>
/// Filet de sécurité GLOBAL sur toutes les réponses fichier (PDF, xlsx, zip…) des contrôleurs.
///
/// POURQUOI CE FILTRE EXISTE
/// ------------------------
/// Une petite quinzaine d'actions font <c>return File(result.Content, "application/pdf")</c> sans
/// garde préalable : le jour où le générateur renvoie un tableau d'octets VIDE (QuestPDF sait le
/// faire silencieusement — voir <c>PaymentReceiptPdfGenerator</c>), ASP.NET répond alors
/// <c>200 OK</c> + <c>Content-Length: 0</c>. Côté navigateur, ni le visualiseur natif ni le moteur
/// PDF.js (<c>wwwroot/js/pdf-preview.js</c>) n'ont rien à afficher : l'utilisateur voit
/// « Impossible d'afficher l'aperçu — document vide (0 octet) » sur un cul-de-sac, alors que la
/// vraie panne est côté serveur et n'est même pas journalisée.
///
/// Les actions qui gardaient déjà le cas le faisaient chacune à sa façon (<c>NotFound(new { message })</c>) :
/// mauvais statut (404 = « n'existe pas », pas « génération ratée ») et hors format d'erreur normalisé
/// (AGENTS.md règle #9, docs/Volume_4_API_Design.md §0.4). Ce filtre unifie tout : une réponse fichier
/// de 0 octet devient un <c>500</c> normalisé, journalisé en <c>Error</c> avec la route en cause, et
/// le client (<c>api.js</c> / <c>pdf-preview.js</c>) affiche un message exploitable + « Réessayer »
/// au lieu d'une impasse muette. Couvre toutes les actions actuelles ET futures, sans garde à recopier.
/// </summary>
public sealed class EmptyFileResultGuardFilter(ILogger<EmptyFileResultGuardFilter> logger) : IAsyncResultFilter
{
    /// <summary>Code d'erreur normalisé consommé par le client — distinct de INTERNAL_ERROR pour rester diagnosticable.</summary>
    public const string ErrorCode = "DOCUMENT_GENERATION_FAILED";

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (IsEmptyFileResult(context.Result, out var contentType))
        {
            var request = context.HttpContext.Request;
            logger.LogError(
                "Réponse fichier vide (0 octet) interceptée pour {Method} {Path} (type {ContentType}). " +
                "Le générateur a produit un contenu vide — renvoi d'une erreur normalisée au lieu d'un 200 muet.",
                request.Method, request.Path.Value, contentType ?? "inconnu");

            // Le flux du résultat écarté ne sera jamais exécuté (donc jamais disposé par MVC) : on s'en
            // charge. Sans conséquence pour un MemoryStream, correct si un jour c'est un vrai FileStream.
            if (context.Result is FileStreamResult { FileStream: { } discarded })
            {
                await discarded.DisposeAsync();
            }

            context.Result = new ObjectResult(new
            {
                code = ErrorCode,
                message = "Le document n'a pas pu être généré (contenu vide). Réessayez ; si le problème persiste, signalez-le.",
                details = (object?)null,
                traceId = context.HttpContext.TraceIdentifier
            })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }

        await next();
    }

    /// <summary>
    /// Vrai uniquement pour une réponse fichier dont le corps est certain d'être vide. Un flux non
    /// rembobinable (<c>CanSeek == false</c>) ne peut pas être mesuré sans le consommer : on le laisse
    /// passer plutôt que de risquer de vider une réponse légitime — aucun endpoint fichier du projet
    /// n'est dans ce cas (tous partent d'un <c>byte[]</c> ou d'un <c>MemoryStream</c>).
    /// </summary>
    private static bool IsEmptyFileResult(IActionResult result, out string? contentType)
    {
        switch (result)
        {
            case FileContentResult fileContent:
                contentType = fileContent.ContentType;
                return fileContent.FileContents is null || fileContent.FileContents.Length == 0;

            case FileStreamResult fileStream:
                contentType = fileStream.ContentType;
                return fileStream.FileStream is null
                    || (fileStream.FileStream.CanSeek && fileStream.FileStream.Length == 0);

            default:
                contentType = null;
                return false;
        }
    }
}
