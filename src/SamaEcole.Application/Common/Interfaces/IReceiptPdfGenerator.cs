using SamaEcole.Application.Enrollments;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Rend un reçu d'inscription en PDF (ticket JGK-E02). L'implémentation (SamaEcole.Infrastructure,
/// QuestPDF) reproduit fidèlement docs/design-references/receipt-reference.png — la mise en page est
/// une préoccupation d'infrastructure, la donnée (<see cref="EnrollmentReceiptDto"/>) vient de
/// l'Application. Le document est déterministe : mêmes données figées ⇒ même reçu, réimpression comprise.
/// </summary>
public interface IReceiptPdfGenerator
{
    byte[] Generate(EnrollmentReceiptDto receipt);
}
