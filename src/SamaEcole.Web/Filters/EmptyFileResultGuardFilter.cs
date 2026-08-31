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
/// faire silencieusement — voir <c>PdfRenderGuard</c>), ASP.NET répond alors
/// <c>200 OK</c> + <c>Content-Length: 0</c>. Côté navigateur, ni le visualiseur natif ni
/// <c>wwwroot/js/pdf-preview.js</c> n'ont rien à afficher : l'utilisateur voit
/// « Le document généré par le serveur est vide (0 octet) » sur un cul-de-sac, alors que la
/// vraie panne est côté serveur et n'est même pas journalisée.
///
/// Ce filtre unifie tout : une réponse fichier de 0 octet devient un <c>500</c> normalisé
/// (AGENTS.md règle #9, docs/Volume_4_API_Design.md §0.4), journalisé en <c>Error</c> AVEC la route en
/// cause ET une exception synthétique (<see cref="InvalidOperationException"/>) pour disposer d'une
/// pile d'appels exploitable en supervision. Couvre toutes les actions actuelles ET futures, sans
/// garde à recopier.
///
/// MÉCANISME : on REMPLACE <see cref="ResultExecutingContext.Result"/> par un <see cref="ObjectResult"/>
/// 500 — on ne LÈVE pas. À ce stade du pipeline MVC le résultat est déjà sélectionné ; lever ici
/// risquerait un « response already started ». Le remplacement garantit un vrai 500 (jamais un 200
/// muet), et le log porté par une exception synthétique donne la même traçabilité qu'un throw.
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
            var route = $"{request.Method} {request.Path.Value}";
            var action = context.ActionDescriptor.DisplayName ?? "action inconnue";

            // Exception synthétique : jamais levée (voir le commentaire de classe), mais passée au
            // logger pour capturer une pile d'appels et rendre l'incident cherchable en supervision.
            var diagnostic = new InvalidOperationException(
                $"Génération PDF vide : {action} a produit un FileResult de 0 octet pour {route} " +
                $"(type {contentType ?? "inconnu"}). Aucun 200 muet n'est renvoyé — le client reçoit un 500 normalisé.");

            logger.LogError(
                diagnostic,
                "Réponse fichier vide (0 octet) interceptée pour {Route} → {Action} (type {ContentType}).",
                route, action, contentType ?? "inconnu");

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
    /// rembobinable (<c>CanSeek == false</c>) ne peut pas être mesuré sans le consumer : on le laisse
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
