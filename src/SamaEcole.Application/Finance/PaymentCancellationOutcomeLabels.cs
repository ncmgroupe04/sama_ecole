using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Finance;

/// <summary>Libellés français de la destination de l'argent d'un versement annulé, pour les états imprimés.</summary>
public static class PaymentCancellationOutcomeLabels
{
    public static string Label(PaymentCancellationOutcome? outcome) => outcome switch
    {
        PaymentCancellationOutcome.ReusedForEnrollment => "argent réutilisé pour l'inscription de l'enfant",
        PaymentCancellationOutcome.RefundedToParent => "argent remboursé au parent",
        _ => "simple correction, l'argent reste à l'école"
    };

    /// <summary>« Annulé le 13/10/2026 — motif (destination) », la mention portée sur le versement d'origine.</summary>
    public static string CancelledNote(DateTimeOffset? cancelledAt, string? reason, PaymentCancellationOutcome? outcome) =>
        $"Annulé le {cancelledAt:dd/MM/yyyy} — {(string.IsNullOrWhiteSpace(reason) ? "sans motif" : reason)} ({Label(outcome)})";
}
