namespace SamaEcole.Domain.Enums;

public enum EntityStatus
{
    Active,
    Suspended,
    Blocked
}

public enum EnrollmentType
{
    NewEnrollment,
    ReEnrollment
}

public enum EnrollmentStatus
{
    Pending,
    Confirmed,
    Cancelled
}

/// <summary>
/// Moyens de paiement d'un élève acceptés en V1 (DDS §6, Volume 1 §7.3) : espèces, chèque, virement,
/// Mobile Money (Wave / Orange Money — saisi MANUELLEMENT en V1, sans intégration agrégateur ; celle-ci
/// ne concerne que les abonnements, règle #11). Distinct de <c>SubscriptionPaymentMethod</c>.
/// </summary>
public enum PaymentMethod
{
    Cash,
    Cheque,
    Transfer,
    MobileMoney
}

/// <summary>
/// Statut d'un paiement d'élève (DDS §6). <c>Paid</c> : ce versement solde l'inscription ; <c>Partial</c> :
/// un solde reste dû après lui ; <c>Cancelled</c> : versement annulé/remboursé (réservé à une évolution —
/// aucune route de F02 ne l'émet). Le statut GLOBAL d'une inscription se déduit de son solde, pas d'ici.
/// </summary>
public enum PaymentStatus
{
    Paid,
    Partial,
    Cancelled
}

public enum PaymentCategory
{
    Enrollment,
    Tuition,
    Exam,
    Other
}

public enum SubscriptionPlan
{
    Primaire,
    Standard,
    Premium
}

public enum SubscriptionStatus
{
    Active,
    Suspended,
    ReadOnly
}

/// <summary>
/// Type d'évaluation d'une note (ticket JGK-G01, Volume 1 §8.1) : le bulletin distingue une colonne
/// Devoir d'une colonne Composition par matière, avant la moyenne pondérée — jamais une note unique.
/// </summary>
public enum EvaluationType
{
    Devoir,
    Composition
}

public enum MatriculeKind
{
    Student,
    Teacher,

    /// <summary>
    /// Numéro de reçu d'inscription (ticket JGK-E02, ex. « REC-2025-0002 »). Réutilise le compteur
    /// séquentiel par (école, type, année) des matricules : même garantie de numérotation officielle
    /// unique et sans trou, incrémentée dans la transaction d'inscription.
    /// </summary>
    Receipt
}
