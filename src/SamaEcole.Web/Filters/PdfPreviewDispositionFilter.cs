using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;

namespace SamaEcole.Web.Filters;

/// <summary>
/// Filtre MVC global : quand une requête porte l'en-tête <c>X-Pdf-Preview: 1</c> (posé par
/// <c>wwwroot/js/pdf-preview.js</c> pour alimenter la modale d'aperçu), la réponse fichier PDF est
/// renvoyée en <c>application/octet-stream</c> + <c>Content-Disposition: inline</c>, sans nom de
/// fichier.
///
/// POURQUOI
/// --------
/// Un gestionnaire de téléchargement (Internet Download Manager et ses équivalents, extensions
/// « grab », mode « toujours télécharger les PDF » du navigateur) surveille les réponses HTTP et
/// intercepte tout ce qui ressemble à un PDF téléchargeable : il COUPE alors le <c>fetch()</c> de la
/// page (côté serveur : <c>OperationCanceledException</c> / requêtes annulées) et propose un
/// enregistrement de fichier à la place. L'aperçu intégré ne reçoit jamais ses octets.
///
/// En présentant les mêmes octets comme un flux binaire anonyme et non comme un « fichier PDF », ces
/// outils cessent d'intercepter : ce n'est plus un téléchargement à leurs yeux. La page, elle,
/// reconstruit un <c>Blob { type: 'application/pdf' }</c> en local (pdf-preview.js) — le rendu, le
/// bouton « Ouvrir dans un nouvel onglet » et l'impression ne changent pas.
///
/// N'affecte QUE les requêtes marquées : un téléchargement normal (bouton « Télécharger », export)
/// n'envoie pas l'en-tête et garde son <c>application/pdf</c> + <c>attachment; filename=…</c>.
/// </summary>
public sealed class PdfPreviewDispositionFilter : IAsyncResultFilter
{
    /// <summary>En-tête posé par pdf-preview.js sur le fetch qui alimente la modale d'aperçu.</summary>
    public const string PreviewHeader = "X-Pdf-Preview";

    private const string OpaqueContentType = "application/octet-stream";

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.HttpContext.Request.Headers.TryGetValue(PreviewHeader, out var flag)
            && flag == "1"
            && TryRewriteAsOpaque(context.Result, out var rewritten))
        {
            context.Result = rewritten;
            // Pas de `attachment; filename=…` : rien que le gestionnaire de téléchargement puisse « attraper ».
            context.HttpContext.Response.Headers[HeaderNames.ContentDisposition] = "inline";
        }

        await next();
    }

    /// <summary>
    /// Si <paramref name="result"/> est une réponse fichier de type PDF, en produit une copie servie
    /// en <c>application/octet-stream</c> sans nom de téléchargement. <c>FileResult.ContentType</c>
    /// étant en lecture seule, il faut reconstruire le résultat.
    /// </summary>
    private static bool TryRewriteAsOpaque(IActionResult result, out IActionResult rewritten)
    {
        switch (result)
        {
            case FileContentResult f when IsPdf(f.ContentType):
                rewritten = new FileContentResult(f.FileContents, OpaqueContentType)
                {
                    EnableRangeProcessing = f.EnableRangeProcessing,
                    LastModified = f.LastModified,
                    EntityTag = f.EntityTag
                };
                return true;

            case FileStreamResult s when IsPdf(s.ContentType):
                rewritten = new FileStreamResult(s.FileStream, OpaqueContentType)
                {
                    EnableRangeProcessing = s.EnableRangeProcessing,
                    LastModified = s.LastModified,
                    EntityTag = s.EntityTag
                };
                return true;

            default:
                rewritten = result;
                return false;
        }
    }

    private static bool IsPdf(string? contentType) =>
        contentType?.Contains("pdf", StringComparison.OrdinalIgnoreCase) ?? false;
}
