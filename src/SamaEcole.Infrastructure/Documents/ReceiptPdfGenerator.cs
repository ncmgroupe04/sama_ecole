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
/// toute génération, sinon la bibliothèque lève une exception. Le constructeur statique la fixe : elle
/// est ainsi garantie aussi bien en production qu'en test, sans dépendre de l'ordre d'initialisation.
///
/// Le générateur ne renvoie JAMAIS un tableau vide : il retente sans logo en cas de premier échec, et
/// consigne l'erreur pour investigation sans bloquer l'émission du reçu.
/// </summary>
public class ReceiptPdfGenerator(ILogger<ReceiptPdfGenerator> logger) : IReceiptPdfGenerator
{
    static ReceiptPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(EnrollmentReceiptDto receipt, byte[]? logo)
    {
        try
        {
            var pdf = new EnrollmentReceiptDocument(receipt, logo).GeneratePdf();
            if (pdf is null || pdf.Length == 0)
            {
                logger.LogWarning("QuestPDF a renvoyé un PDF vide pour le reçu d'inscription {ReceiptNumber}. Nouvelle tentative sans logo.", receipt.ReceiptNumber);
                pdf = new EnrollmentReceiptDocument(receipt, null).GeneratePdf();
            }
            return pdf;
        }
        catch (Exception ex) when (logo is not null)
        {
            // Dernier filet : le logo a passé les contrôles du fournisseur mais reste illisible pour le
            // moteur de rendu. Un reçu OFFICIEL doit toujours s'émettre — on le régénère sans le logo
            // plutôt que de propager l'échec. Le cas courant (logo injoignable) est déjà journalisé et
            // écarté en amont par ISchoolLogoProvider ; on n'arrive ici que pour un contenu pathologique.
            logger.LogWarning(ex, "Erreur lors de la génération du reçu d'inscription {ReceiptNumber} avec logo. Nouvelle tentative sans logo.", receipt.ReceiptNumber);
            return new EnrollmentReceiptDocument(receipt, null).GeneratePdf();
        }
        catch (Exception ex)
        {
            // Même sans logo, la composition a échoué. On consigne l'erreur et on laisse propager :
            // le contrôleur renverra un 500 avec un message d'erreur au lieu d'un blob vide.
            logger.LogError(ex, "Erreur fatale lors de la génération du reçu d'inscription {ReceiptNumber} (sans logo). Données : SchoolName={SchoolName}, Matricule={Matricule}",
                receipt.ReceiptNumber, receipt.SchoolName, receipt.Matricule);
            throw;
        }
    }
}
