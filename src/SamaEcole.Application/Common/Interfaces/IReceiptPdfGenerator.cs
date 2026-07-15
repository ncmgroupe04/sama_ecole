using SamaEcole.Application.Enrollments;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Rend un reçu d'inscription en PDF (ticket JGK-E02). L'implémentation (SamaEcole.Infrastructure,
/// QuestPDF) reproduit fidèlement docs/design-references/receipt-reference.png — la mise en page est
/// une préoccupation d'infrastructure, la donnée (<see cref="EnrollmentReceiptDto"/>) vient de
/// l'Application. Le document est déterministe : mêmes données figées ⇒ même reçu, réimpression comprise.
///
/// <paramref name="logo"/> porte les octets déjà récupérés et validés du logo de l'établissement
/// (via <see cref="ISchoolLogoProvider"/>), ou <c>null</c> si l'école n'en a pas / s'il est
/// injoignable : dans ce cas l'en-tête retombe sur l'emplacement réservé de la maquette. La
/// récupération réseau reste hors du générateur, qui demeure une fonction pure et synchrone de ses
/// entrées.
/// </summary>
public interface IReceiptPdfGenerator
{
    byte[] Generate(EnrollmentReceiptDto receipt, byte[]? logo);
}
