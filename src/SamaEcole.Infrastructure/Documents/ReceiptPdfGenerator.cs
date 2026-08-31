using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Implémentation QuestPDF de <see cref="IReceiptPdfGenerator"/> (ticket JGK-E02).
///
/// La licence Community de QuestPDF (gratuite, adaptée à ce produit) DOIT être posée une fois avant
/// toute génération, sinon la bibliothèque lève une exception. Le constructeur statique la fixe.
///
/// Le rendu passe par <see cref="PdfRenderGuard"/> : un logo illisible n'empêche jamais l'émission
/// d'un reçu OFFICIEL (nouvelle passe sans logo), et un rendu vide LÈVE une exception journalisée —
/// jamais un tableau de 0 octet qui partirait en 200 muet.
/// </summary>
public class ReceiptPdfGenerator(ILogger<ReceiptPdfGenerator> logger) : IReceiptPdfGenerator
{
    static ReceiptPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(EnrollmentReceiptDto receipt, byte[]? logo) =>
        PdfRenderGuard.Render(
            logger,
            $"reçu d'inscription {receipt.ReceiptNumber} (matricule {receipt.Matricule}, école {receipt.SchoolName})",
            () => new EnrollmentReceiptDocument(receipt, logo).GeneratePdf(),
            logo is not null ? () => new EnrollmentReceiptDocument(receipt, null).GeneratePdf() : null);
}
