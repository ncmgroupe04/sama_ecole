using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Filet UNIQUE de tous les générateurs PDF QuestPDF. Garantit qu'un générateur ne renvoie JAMAIS
/// <c>null</c> ni un tableau de 0 octet : soit un PDF valide, soit une exception explicite et
/// journalisée. Le middleware d'exception en fait alors un 500 normalisé — jamais un 200 à corps vide
/// qui bloquerait l'aperçu client sur « Le document généré par le serveur est vide (0 octet) » sans
/// laisser la moindre trace serveur.
///
/// POURQUOI CE HELPER
/// ------------------
/// Trois générateurs (reçu d'inscription, reçu de paiement, export élèves) codaient déjà cette garde,
/// chacun à sa façon. Une quinzaine d'autres se contentaient d'un <c>catch (Exception) when (logo is
/// not null)</c> qui retente sans logo — mais la relance n'était pas gardée et rien ne vérifiait
/// <c>Length == 0</c>. Une dizaine ne gardaient rien du tout. Résultat : selon le document cliqué, un
/// échec de rendu pouvait produire un 500 propre, un 500 opaque, ou (via un chemin QuestPDF) un
/// tableau vide silencieux. Ce helper impose le MÊME comportement partout.
///
/// DEUX PASSES
/// ----------
///  1. <paramref name="render"/> — rendu complet (logo d'établissement, cachet, signature…).
///  2. <paramref name="renderPlain"/> (optionnel) — même document dépouillé des ornements chargés
///     dynamiquement : un logo illisible ne doit JAMAIS empêcher l'émission d'une pièce officielle.
///     Les documents sans ornement passent <c>null</c>.
///
/// Un rendu qui LÈVE **ou** qui renvoie 0 octet est traité de façon identique : on tente la passe
/// dépouillée si elle existe, sinon on lève <see cref="InvalidOperationException"/> en citant le
/// document et ses identifiants (log <c>Error</c>, exploitable en production).
/// </summary>
public static class PdfRenderGuard
{
    /// <param name="logger">
    /// Journal du générateur appelant (contexte : nom de la classe génératrice). <c>null</c> accepté
    /// (tests unitaires qui instancient le générateur sans conteneur) : le helper retombe alors sur
    /// <see cref="NullLogger"/>.
    /// </param>
    /// <param name="documentLabel">
    /// Libellé humain du document AVEC ses identifiants, p. ex.
    /// <c>$"reçu de paiement {receipt.ReceiptNumber} (matricule {receipt.Matricule})"</c>.
    /// Il apparaît tel quel dans les logs et dans le message d'erreur : le mettre parlant.
    /// </param>
    /// <param name="render">Rendu complet. Peut lever ou (pathologiquement) renvoyer un tableau vide.</param>
    /// <param name="renderPlain">Rendu dépouillé du logo/cachet/signature, ou <c>null</c> si sans objet.</param>
    public static byte[] Render(
        ILogger? logger,
        string documentLabel,
        Func<byte[]?> render,
        Func<byte[]?>? renderPlain = null)
    {
        logger ??= NullLogger.Instance;

        byte[]? pdf = null;
        Exception? failure = null;

        try
        {
            pdf = render();
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        if ((pdf is null || pdf.Length == 0) && renderPlain is not null)
        {
            if (failure is not null)
            {
                logger.LogWarning(
                    failure,
                    "PDF « {DocumentLabel} » : échec du rendu complet. Nouvelle tentative sans logo/cachet/signature.",
                    documentLabel);
            }
            else
            {
                logger.LogWarning(
                    "PDF « {DocumentLabel} » : le rendu complet a produit 0 octet. Nouvelle tentative sans logo/cachet/signature.",
                    documentLabel);
            }

            failure = null;
            try
            {
                pdf = renderPlain();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        }

        if (pdf is { Length: > 0 })
        {
            return pdf;
        }

        var message = $"La génération du PDF « {documentLabel} » a produit 0 octet — aucun document ne sera renvoyé.";
        logger.LogError(failure, "{Message}", message);

        throw failure is not null
            ? new InvalidOperationException(message, failure)
            : new InvalidOperationException(message);
    }
}
