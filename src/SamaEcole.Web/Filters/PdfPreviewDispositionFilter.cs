using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;

namespace SamaEcole.Web.Filters;

/// <summary>
/// Filtre MVC global : quand une requête porte l'en-tête <c>X-Pdf-Preview: 1</c> (posé par
/// <c>wwwroot/js/pdf-preview.js</c> pour l'aperçu intégré), la réponse fichier PDF est renvoyée en
/// <c>text/plain</c> + <c>Content-Disposition: inline</c>, sans nom de fichier.
///
/// POURQUOI, ET POURQUOI text/plain PLUTÔT QU'application/octet-stream
/// -----------------------------------------------------------------
/// Un gestionnaire de téléchargement — Internet Download Manager (IDM) au premier chef, avec son
/// « intégration avancée au navigateur » — surveille les réponses HTTP et DÉTOURNE tout ce qui
/// ressemble à un fichier téléchargeable : il happe les octets vers sa propre file et laisse au
/// <c>fetch()</c> de la page une réponse VIDE (<c>204</c>, ou <c>200</c> sans corps). L'aperçu ne
/// reçoit jamais ses octets → toast « Le document généré par le serveur est vide (0 octet) ».
///
/// <c>application/pdf</c> ET <c>application/octet-stream</c> déclenchent tous deux ce détournement
/// (octet-stream = « binaire inconnu » = téléchargement, pour IDM). En revanche <c>text/plain</c>
/// n'est jamais vu comme un fichier : IDM laisse passer. La page relit les octets
/// (<c>response.arrayBuffer()</c>), vérifie la signature <c>%PDF-</c> et reconstruit un
/// <c>Blob { type: 'application/pdf' }</c> 100 % local — rendu, nouvel onglet et impression inchangés.
///
/// N'affecte QUE les requêtes marquées : un téléchargement normal (bouton « Télécharger », export)
/// n'envoie pas l'en-tête et garde son <c>application/pdf</c> + <c>Content-Disposition: attachment; filename=…</c>.
/// </summary>
public sealed class PdfPreviewDispositionFilter : IAsyncResultFilter
{
    /// <summary>En-tête posé par pdf-preview.js sur le fetch qui alimente l'aperçu.</summary>
    public const string PreviewHeader = "X-Pdf-Preview";

    /// <summary>Type de contenu qu'aucun gestionnaire de téléchargement ne considère comme un fichier.</summary>
    private const string InertContentType = "text/plain";

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.HttpContext.Request.Headers.TryGetValue(PreviewHeader, out var flag)
            && flag == "1"
            && TryRewriteAsInert(context.Result, out var rewritten))
        {
            context.Result = rewritten;
            // Pas de `attachment; filename=….pdf` : on retire le dernier signal « fichier » que le
            // contrôleur avait pu poser (report-cards/generate, billets… posent un inline; filename=…).
            context.HttpContext.Response.Headers[HeaderNames.ContentDisposition] = "inline";
        }

        await next();
    }

    /// <summary>
    /// Si <paramref name="result"/> est une réponse fichier de type PDF, en produit une copie servie
    /// en <c>text/plain</c> sans nom de téléchargement. <c>FileResult.ContentType</c> étant en lecture
    /// seule, il faut reconstruire le résultat.
    /// </summary>
    private static bool TryRewriteAsInert(IActionResult result, out IActionResult rewritten)
    {
        switch (result)
        {
            case FileContentResult f when IsPdf(f.ContentType):
                rewritten = new FileContentResult(f.FileContents, InertContentType)
                {
                    EnableRangeProcessing = f.EnableRangeProcessing,
                    LastModified = f.LastModified,
                    EntityTag = f.EntityTag
                };
                return true;

            case FileStreamResult s when IsPdf(s.ContentType):
                rewritten = new FileStreamResult(s.FileStream, InertContentType)
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
