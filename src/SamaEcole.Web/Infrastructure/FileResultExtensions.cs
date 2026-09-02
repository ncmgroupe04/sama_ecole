using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace SamaEcole.Web.Infrastructure;

/// <summary>
/// Réponses fichier partagées entre contrôleurs.
///
/// Un PDF est TOUJOURS servi <c>inline</c> (Content-Disposition), jamais <c>attachment</c> : il
/// s'ouvre d'abord dans la modale d'aperçu partagée (<c>_PdfPreviewModal</c> / <c>pdf-preview.js</c>),
/// d'où l'utilisateur imprime ou télécharge à son rythme. L'overload à trois arguments
/// <c>File(bytes, contentType, fileName)</c> force <c>attachment</c> — on ne l'utilise donc jamais
/// pour un PDF.
///
/// <see cref="ContentDispositionHeaderValue.SetHttpFileName"/> produit un couple
/// <c>filename</c> / <c>filename*</c> conforme (RFC 6266 + RFC 5987) : indispensable dès que le nom
/// porte un nom d'élève accentué ou une espace (certificat de mutation, livret de compétences…).
/// </summary>
public static class FileResultExtensions
{
    /// <summary>PDF rendu <c>inline</c> (aperçu avant impression/téléchargement), nom de fichier normalisé.</summary>
    public static FileContentResult InlinePdf(this ControllerBase controller, byte[] content, string fileName)
    {
        var contentDisposition = new ContentDispositionHeaderValue("inline");
        contentDisposition.SetHttpFileName(fileName);
        controller.Response.Headers.ContentDisposition = contentDisposition.ToString();

        return controller.File(content, "application/pdf");
    }
}
