namespace SamaEcole.Application.Uniforms;

/// <summary>
/// Les libellés qu'une remise ou un retour écrit au journal de stock (<c>stock_movements</c>). Le journal est la pièce
/// opposable d'un contrôle : son motif doit dire QUOI est sorti et POUR QUI, sans jamais dépendre de la longueur d'un
/// nom — <c>Reason</c> est borné à 200 caractères et <c>CounterpartyLabel</c> à 150. Un libellé trop long est tronqué par
/// une ellipse ; il n'est jamais cause d'échec de la remise.
/// </summary>
public static class UniformStockLabels
{
    /// <summary>Borne de <c>stock_movements.Reason</c>.</summary>
    public const int MaxReasonLength = 200;

    /// <summary>Borne de <c>stock_movements.CounterpartyLabel</c>.</summary>
    public const int MaxCounterpartyLength = 150;

    public static string Delivery(string itemName, string sizeLabel, string? studentLabel) =>
        Fit(string.IsNullOrWhiteSpace(studentLabel)
            ? $"Remise de tenue — {itemName} {sizeLabel}"
            : $"Remise de tenue — {itemName} {sizeLabel} — {studentLabel}", MaxReasonLength);

    public static string Return(string itemName, string sizeLabel, string reason) =>
        Fit($"Retour de tenue — {itemName} {sizeLabel} — {reason}", MaxReasonLength);

    /// <summary>L'élève, côté « destinataire » du mouvement ; <c>null</c> si on ne le connaît pas.</summary>
    public static string? Counterparty(string? studentLabel) =>
        string.IsNullOrWhiteSpace(studentLabel) ? null : Fit(studentLabel.Trim(), MaxCounterpartyLength);

    private static string Fit(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
