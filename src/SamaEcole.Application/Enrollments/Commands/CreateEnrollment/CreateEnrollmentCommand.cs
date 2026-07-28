using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Enrollments.Commands.CreateEnrollment;

/// <summary>
/// POST /api/v1/enrollments — ticket JGK-E01. Un seul acte transactionnel qui, selon le
/// <see cref="Type"/> :
///
///   * NewEnrollment — CRÉE l'élève (et génère son matricule, JGK-D01/B02) puis l'inscrit ;
///   * ReEnrollment — inscrit un élève DÉJÀ connu (<see cref="StudentId"/> requis), sans nouveau matricule.
///
/// Dans les deux cas, le service calcule le montant dû à partir du barème de la classe (JGK-F01) et
/// initialise le compte financier (lignes de frais figées). Ce qui ne figure PAS ici, à dessein :
///
///   * Le SchoolId — lu du JWT, jamais du client (AGENTS.md règle #10).
///   * L'année scolaire — l'inscription porte l'année ACTIVE, résolue serveur (mission JGK-E01).
///   * Le TotalDue — calculé serveur ; l'accepter du client laisserait fixer un montant arbitraire
///     (règle #4 : le montant d'une inscription n'est pas une donnée d'entrée).
/// </summary>
public record CreateEnrollmentCommand : IRequest<EnrollmentReceiptDto>
{
    public required EnrollmentType Type { get; init; }

    public required Guid ClassroomId { get; init; }

    /// <summary>L'élève redouble cette classe (feature F) — coché sur le bulletin. Faux par défaut.</summary>
    public bool IsRepeating { get; init; }

    // --- Réinscription : élève existant ---
    public Guid? StudentId { get; init; }

    // --- Nouvelle inscription : état civil de l'élève à créer ---
    public string? FullName { get; init; }
    public DateOnly? BirthDate { get; init; }

    /// <summary>Lieu de naissance de l'élève à créer — obligatoire pour une NOUVELLE inscription (feature E).</summary>
    public string? BirthPlace { get; init; }

    public string? Gender { get; init; }
    public string? GuardianName { get; init; }
    public string? GuardianPhone { get; init; }

    // --- Encaissement du jour (ventilé) ---

    /// <summary>
    /// Frais réglés au guichet AU MOMENT de l'inscription, catégorie par catégorie. Liste vide = dossier
    /// ouvert sans versement : l'inscription est créée, aucun paiement ne l'est, et le reçu s'imprime
    /// avec un total encaissé de 0.
    ///
    /// Le client désigne QUOI est réglé, jamais COMBIEN : les montants sont repris du barème que le
    /// serveur vient lui-même de figer (règle #4 — un montant n'est pas une donnée d'entrée).
    /// </summary>
    public IReadOnlyList<CollectedFeeInput> CollectedFees { get; init; } = [];

    /// <summary>Mode de règlement du versement du jour. Ignoré si <see cref="CollectedFees"/> est vide.</summary>
    public PaymentMethod PaymentMethod { get; init; } = PaymentMethod.Cash;
}

/// <summary>
/// Une catégorie de frais réglée à l'inscription. <paramref name="Months"/> ne vaut que pour une
/// mensualité (« le tuteur règle 2 mois d'avance ») : sur un frais ponctuel il est ignoré, la ligne
/// étant réglée en entier ou pas du tout. Le serveur borne ce nombre au nombre de mois facturés.
/// </summary>
public record CollectedFeeInput(Guid FeeCategoryId, int Months = 1);
