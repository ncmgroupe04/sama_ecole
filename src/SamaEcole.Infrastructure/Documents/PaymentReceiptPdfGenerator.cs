using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Implémentation QuestPDF de <see cref="IPaymentReceiptPdfGenerator"/> (ticket JGK-F02). Comme pour le
/// reçu d'inscription, la licence Community est posée une fois via le constructeur statique, et un logo
/// illisible ne doit JAMAIS empêcher l'émission d'un reçu officiel : on régénère alors sans le logo.
/// </summary>
public class PaymentReceiptPdfGenerator : IPaymentReceiptPdfGenerator
{
    static PaymentReceiptPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(PaymentReceiptDto receipt, byte[]? logo)
    {
        try
        {
            return new PaymentReceiptDocument(receipt, logo).GeneratePdf();
        }
        catch (Exception) when (logo is not null)
        {
            return new PaymentReceiptDocument(receipt, null).GeneratePdf();
        }
    }
}
