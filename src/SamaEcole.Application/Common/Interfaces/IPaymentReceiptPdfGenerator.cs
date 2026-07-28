using SamaEcole.Application.Finance;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Rend un reçu de PAIEMENT en PDF (ticket JGK-F02). Pendant de <see cref="IReceiptPdfGenerator"/> (reçu
/// d'inscription) : même moteur QuestPDF, même référence de design, même mention obligatoire constante.
/// <paramref name="logo"/> porte les octets déjà validés du logo (via <see cref="ISchoolLogoProvider"/>)
/// ou <c>null</c> — la récupération réseau reste hors du générateur, qui demeure pur et synchrone.
/// </summary>
public interface IPaymentReceiptPdfGenerator
{
    byte[] Generate(PaymentReceiptDto receipt, byte[]? logo);
}
