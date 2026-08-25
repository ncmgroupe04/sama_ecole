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

    // Ventilation du versement (Payment.Breakdowns) : une ligne par poste imputé. VIDE quand la caisse
    // n'a rien ventilé — le reçu retombe alors sur sa ligne unique « Versement reçu », comme avant.
    IReadOnlyList<PaymentReceiptLineDto> Lines,

    // Période couverte par le versement entier (« Septembre 2026 »), null si non renseignée. Sert de
    // repli au libellé de chaque ligne, et de sous-titre au tableau.
    string? ReferencePeriod,

    // Classe PASSERELLE / ACCÉLÉRÉE (option) : même mention que sur le reçu d'inscription, pour que le
    // reçu de caisse d'un élève de « CI-CP » dise la même chose que la pièce d'inscription qu'il complète.
    bool IsAcceleratedClass = false)
{
    /// <summary>
    /// Libellé de la colonne « Période / Note » pour une ligne : son libellé propre (« Unique »,
    /// « 2 jeux ») s'il est renseigné, sinon la période du versement entier (« Septembre 2026 »),
    /// sinon <c>null</c> — la cellule reste alors VIDE. Jamais un tiret ni une valeur devinée, même
    /// convention que les coordonnées d'établissement absentes de l'en-tête.
    ///
    /// Résolu ici, dans Application, et non dans le générateur PDF : c'est une règle d'affichage
    /// métier, testable sans instancier QuestPDF (AGENTS.md règle #8).
    /// </summary>
    public string? ResolveLineLabel(PaymentReceiptLineDto line) =>
        !string.IsNullOrWhiteSpace(line.Label) ? line.Label
        : !string.IsNullOrWhiteSpace(ReferencePeriod) ? ReferencePeriod
        : null;

    /// <summary>
    /// Somme des lignes ventilées. Sert de garde-fou au reçu : si elle diffère de
    /// <see cref="Amount"/>, la ventilation est incomplète et le document imprime sa ligne unique
    /// plutôt qu'un tableau qui ne balance pas — un reçu dont le détail ne fait pas le total est
    /// comptablement invalide.
    /// </summary>
    public decimal LinesTotal => Lines.Sum(l => l.Amount);

    /// <summary>Vrai quand la ventilation est exploitable : au moins une ligne, et un total exact.</summary>
    public bool HasBalancedLines => Lines.Count > 0 && LinesTotal == Amount;
}

/// <summary>
/// Une ligne de la ventilation imprimée sur le reçu de caisse : le poste imputé
/// (<paramref name="Designation"/>, le nom de la catégorie de frais), son libellé facultatif de période
/// ou de note, et le montant réellement affecté à ce poste.
/// </summary>
public record PaymentReceiptLineDto(
    string Designation,
    string? Label,
    decimal Amount);
