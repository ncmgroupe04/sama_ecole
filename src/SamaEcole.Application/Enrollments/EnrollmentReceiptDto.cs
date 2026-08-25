namespace SamaEcole.Application.Enrollments;

/// <summary>
/// Charge utile du reçu d'inscription (ticket JGK-E01), renvoyée aussi bien à la création qu'à la
/// relecture GET /enrollments/{id}/receipt. Elle porte tout ce que la référence de design impose
/// (docs/design-references/README.md §1) : en-tête établissement (coordonnées + mentions légales
/// NINEA/RCCM), identité de l'élève et de son tuteur, ventilation de l'encaissement et total.
///
/// Deux montants distincts, à ne jamais confondre :
///   * <paramref name="TotalCollected"/> — ce qui est RÉELLEMENT entré en caisse ce jour-là. C'est le
///     seul total imprimé en gras sur le reçu : une pièce comptable n'atteste que de l'encaissement.
///   * <paramref name="TotalDue"/> — le dû ANNUEL figé à l'inscription, rappelé en pied de tableau
///     avec le reste à payer pour que le tuteur sache où il en est.
///
/// La mention obligatoire (AGENTS.md règle #12) n'est pas transportée ici : c'est un texte constant,
/// figé côté vue/PDF pour qu'aucun appelant ne puisse l'altérer ou l'omettre.
/// </summary>
public record EnrollmentReceiptDto(
    Guid EnrollmentId,
    string ReceiptNumber,
    string SchoolName,
    string? SchoolAddress,
    string? SchoolPhone,
    string? SchoolEmail,
    string? SchoolNinea,
    string? SchoolRegistreCommerce,
    string? SchoolCity,
    string? SchoolLogoUrl,
    string Matricule,
    string StudentFullName,
    string ClassroomName,
    string ClassroomLevel,
    string SchoolYearLabel,
    string? GuardianName,
    string? GuardianPhone,
    string Type,
    string Status,
    DateTimeOffset EnrolledAt,
    IReadOnlyList<EnrollmentFeeLineDto> Lines,
    decimal TotalDue,

    // Ventilation de l'encaissement du jour — une ligne par frais réglé, vide si rien n'a été encaissé.
    IReadOnlyList<CollectedFeeLineDto> CollectedLines,
    decimal TotalCollected,

    // Mode de règlement du versement du jour ; null quand rien n'a été encaissé.
    string? PaymentMethod,

    // Classe PASSERELLE / ACCÉLÉRÉE (option) : la ligne « Classe d'affectation » du reçu porte alors la
    // mention du dispositif, l'année payée en couvrant deux niveaux. Faux pour une classe ordinaire —
    // le reçu est alors rigoureusement identique à ce qu'il a toujours été.
    bool IsAcceleratedClass = false)
{
    /// <summary>Reste dû sur l'année APRÈS le versement du jour. Jamais négatif : l'encaissement est borné au dû.</summary>
    public decimal RemainingBalance => TotalDue - TotalCollected;

    /// <summary>
    /// RÈGLE MÉTIER — engagement initial : ce que le tuteur doit régler à la comptabilité le jour de
    /// l'inscription. Pour chaque ligne, son <see cref="EnrollmentFeeLineDto.UnitAmount"/> : le frais
    /// ponctuel ENTIER (inscription, tenue) ou UNE SEULE mensualité pour une ligne récurrente.
    ///
    /// C'est le cœur de la refonte du 25/08/2026 : l'attestation n'affiche plus jamais
    /// <see cref="TotalDue"/> (le cumul annuel, « 271 000 »), qui effrayait les tuteurs sans les
    /// informer. Elle annonce ce qu'il y a à payer maintenant, puis le tarif mensuel qui suivra.
    ///
    /// HYPOTHÈSE ASSUMÉE : un mois d'avance. Une école qui en exigerait deux, ou qui ne réclamerait
    /// que les frais d'inscription à la signature, a besoin d'un réglage d'établissement — rien dans
    /// le modèle ne porte cette information aujourd'hui.
    /// </summary>
    public decimal InitialSettlementTotal => Lines.Sum(line => line.UnitAmount);

    /// <summary>
    /// Lignes de l'échéancier mensuel : les frais RÉCURRENTS seuls. Un frais ponctuel n'a pas
    /// d'échéance et le faire figurer dans un échéancier serait un faux.
    /// </summary>
    public IReadOnlyList<EnrollmentFeeLineDto> MonthlyLines =>
        Lines.Where(line => line.IsRecurring).ToList();

    /// <summary>
    /// Total à prévoir chaque mois — la somme des mensualités UNITAIRES. Jamais multiplié par le
    /// nombre de mois : c'est précisément la multiplication que la refonte supprime.
    /// </summary>
    public decimal MonthlyTotal => Lines.Where(line => line.IsRecurring).Sum(line => line.UnitAmount);
}

/// <summary>
/// Une ligne du tableau « Désignation des frais / Montant » du reçu. <paramref name="Months"/> vaut 1
/// pour un frais ponctuel et le nombre de mensualités pour une scolarité, ce qui permet à la vue
/// d'afficher « Mensualité (× 9) » sans recalculer quoi que ce soit.
/// </summary>
public record EnrollmentFeeLineDto(
    string Designation,
    bool IsRecurring,
    decimal UnitAmount,
    int Months,
    decimal LineTotal);

/// <summary>
/// Une ligne de la VENTILATION de l'encaissement : le frais réglé au guichet et son montant réel.
/// <paramref name="Months"/> porte le nombre de mois couverts pour une mensualité (« Mensualité
/// (× 1 mois) »), 1 pour un frais ponctuel.
/// </summary>
public record CollectedFeeLineDto(
    string Designation,
    bool IsRecurring,
    int Months,
    decimal Amount);

/// <summary>
/// Dérive la « ville » du bas de reçu (« Fait à … », référence de design §1.6) depuis l'adresse libre
/// de l'établissement. Le modèle ne stocke qu'une adresse (School.Address) ; par convention sénégalaise
/// elle se termine par la localité (« Rue 12, Médina, Dakar » → « Dakar »). Faute d'adresse, on renvoie
/// null et le reçu laisse la ligne à compléter — jamais une valeur inventée.
/// </summary>
public static class ReceiptCity
{
    public static string? FromAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        var lastSegment = address.Split(',')[^1].Trim();
        return lastSegment.Length == 0 ? null : lastSegment;
    }
}
