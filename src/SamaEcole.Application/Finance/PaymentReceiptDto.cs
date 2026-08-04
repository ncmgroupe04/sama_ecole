namespace SamaEcole.Application.Finance;

/// <summary>
/// Charge utile du reçu de PAIEMENT (ticket JGK-F02). Suit la même référence de design que le reçu
/// d'inscription (docs/design-references/README.md §1, Volume 1 §7.3bis) : en-tête établissement, titre
/// numéroté, bloc d'informations, tableau des montants, mention obligatoire, signatures.
///
/// Les montants sont FIGÉS à l'instant du versement (via <c>Payment.BalanceAfter</c>) : réimprimer ce
/// reçu affiche toujours le solde de ce jour-là, jamais l'état courant de l'inscription. La mention
/// obligatoire n'est pas transportée ici — c'est une constante posée côté PDF (AGENTS.md règle #12).
/// </summary>
public record PaymentReceiptDto(
    string ReceiptNumber,
    string SchoolName,
    string? SchoolAddress,
    string? SchoolEmail,
    string? SchoolPhone,
    string? SchoolCity,
    string? SchoolNinea,
    string? SchoolRegistreCommerce,
    string? SchoolLogoUrl,
    string Matricule,
    string StudentFullName,
    string ClassroomName,
    string SchoolYearLabel,
    string Method,
    decimal Amount,
    decimal TotalDue,
    decimal AlreadyPaid,
    decimal RemainingBalance,
    DateTimeOffset PaidAt,

    // Classe PASSERELLE / ACCÉLÉRÉE (option) : même mention que sur le reçu d'inscription, pour que le
    // reçu de caisse d'un élève de « CI-CP » dise la même chose que la pièce d'inscription qu'il complète.
    bool IsAcceleratedClass = false);
