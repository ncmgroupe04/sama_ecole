using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Implémentation QuestPDF de <see cref="IPaymentReceiptPdfGenerator"/> (ticket JGK-F02). Comme pour le
/// reçu d'inscription, la licence Community est posée une fois via le constructeur statique, et un logo
/// illisible ne doit JAMAIS empêcher l'émission d'un reçu officiel : on régénère alors sans le logo.
///
/// Le générateur ne renvoie JAMAIS un tableau vide : il retente sans logo (exception OU document de
/// 0 octet), et si le repli échoue encore il LÈVE — le middleware d'exception en fait un 500 normalisé
/// journalisé, jamais un 200 à corps vide qui bloquerait l'aperçu client sans laisser de trace.
/// </summary>
public class PaymentReceiptPdfGenerator(ILogger<PaymentReceiptPdfGenerator> logger) : IPaymentReceiptPdfGenerator
{
    static PaymentReceiptPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(PaymentReceiptDto receipt, byte[]? logo)
    {
        byte[]? pdf = null;

        if (logo is not null)
        {
            try
            {
                pdf = new PaymentReceiptDocument(receipt, logo).GeneratePdf();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Erreur lors de la génération du reçu de paiement {ReceiptNumber} avec logo. Nouvelle tentative sans logo.", receipt.ReceiptNumber);
            }

            if (pdf is null || pdf.Length == 0)
            {
                logger.LogWarning("Reçu de paiement {ReceiptNumber} : rendu avec logo vide ou en échec. Nouvelle tentative sans logo.", receipt.ReceiptNumber);
                pdf = null;
            }
        }

        if (pdf is null || pdf.Length == 0)
        {
            try
            {
                pdf = new PaymentReceiptDocument(receipt, null).GeneratePdf();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erreur fatale lors de la génération du reçu de paiement {ReceiptNumber} (sans logo). Données : SchoolName={SchoolName}, Matricule={Matricule}",
                    receipt.ReceiptNumber, receipt.SchoolName, receipt.Matricule);
                throw;
            }
        }

        if (pdf is null || pdf.Length == 0)
        {
            throw new InvalidOperationException($"Le reçu de paiement généré est vide (reçu {receipt.ReceiptNumber}).");
        }

        return pdf;
    }
}
