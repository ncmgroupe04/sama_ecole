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
/// Le générateur ne renvoie JAMAIS un tableau vide : il retente sans logo en cas de premier échec, et
/// consigne l'erreur pour investigation sans bloquer l'émission du reçu.
/// </summary>
public class PaymentReceiptPdfGenerator(ILogger<PaymentReceiptPdfGenerator> logger) : IPaymentReceiptPdfGenerator
{
    static PaymentReceiptPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(PaymentReceiptDto receipt, byte[]? logo)
    {
        try
        {
            var pdf = new PaymentReceiptDocument(receipt, logo).GeneratePdf();
            if (pdf is null || pdf.Length == 0)
            {
                logger.LogWarning("QuestPDF a renvoyé un PDF vide pour le reçu de paiement {ReceiptNumber}. Nouvelle tentative sans logo.", receipt.ReceiptNumber);
                pdf = new PaymentReceiptDocument(receipt, null).GeneratePdf();
            }
            return pdf;
        }
        catch (Exception ex) when (logo is not null)
        {
            logger.LogWarning(ex, "Erreur lors de la génération du reçu de paiement {ReceiptNumber} avec logo. Nouvelle tentative sans logo.", receipt.ReceiptNumber);
            return new PaymentReceiptDocument(receipt, null).GeneratePdf();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur fatale lors de la génération du reçu de paiement {ReceiptNumber} (sans logo). Données : SchoolName={SchoolName}, Matricule={Matricule}",
                receipt.ReceiptNumber, receipt.SchoolName, receipt.Matricule);
            throw;
        }
    }
}
