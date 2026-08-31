using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Implémentation QuestPDF de <see cref="IPaymentReceiptPdfGenerator"/> (ticket JGK-F02). Licence
/// Community posée une fois dans le constructeur statique.
///
/// Le rendu passe par <see cref="PdfRenderGuard"/> : un logo illisible n'empêche jamais l'émission
/// d'un reçu OFFICIEL (nouvelle passe sans logo), et un rendu vide LÈVE une exception journalisée —
/// jamais un tableau de 0 octet qui partirait en 200 muet et bloquerait l'aperçu client sur
/// « document vide (0 octet) » sans laisser de trace.
/// </summary>
public class PaymentReceiptPdfGenerator(ILogger<PaymentReceiptPdfGenerator> logger) : IPaymentReceiptPdfGenerator
{
    static PaymentReceiptPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(PaymentReceiptDto receipt, byte[]? logo) =>
        PdfRenderGuard.Render(
            logger,
            $"reçu de paiement {receipt.ReceiptNumber} (matricule {receipt.Matricule}, école {receipt.SchoolName})",
            () => new PaymentReceiptDocument(receipt, logo).GeneratePdf(),
            logo is not null ? () => new PaymentReceiptDocument(receipt, null).GeneratePdf() : null);
}
