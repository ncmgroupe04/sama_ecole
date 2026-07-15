using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Implémentation QuestPDF de <see cref="IReceiptPdfGenerator"/> (ticket JGK-E02).
///
/// La licence Community de QuestPDF (gratuite, adaptée à ce produit) DOIT être posée une fois avant
/// toute génération, sinon la bibliothèque lève une exception. Le constructeur statique la fixe : elle
/// est ainsi garantie aussi bien en production qu'en test, sans dépendre de l'ordre d'initialisation.
/// </summary>
public class ReceiptPdfGenerator : IReceiptPdfGenerator
{
    static ReceiptPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(EnrollmentReceiptDto receipt) =>
        new EnrollmentReceiptDocument(receipt).GeneratePdf();
}
