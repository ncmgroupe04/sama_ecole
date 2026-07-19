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

/// <summary>
/// Cycle de vie d'une inscription (ticket cycle de vie des inscriptions). <c>Cancelled</c> : erreur de
/// saisie annulée avant tout encaissement — libère le créneau (voir l'index partiel
/// EnrollmentConfiguration.UX_enrollments_single_active_per_year, qui exclut ce seul statut).
/// <c>DroppedOut</c>/<c>Transferred</c> : abandon ou transfert en cours d'année — l'élève quitte la
/// classe pour l'avenir (exclu des appels de présence actifs), mais l'inscription reste l'historique
/// figé des notes et paiements déjà effectués, elle n'est PAS soft-deletée et continue d'occuper le
/// créneau (SchoolYearId, StudentId) : on ne réinscrit pas un élève transféré ou en abandon la même
/// année, contrairement à une inscription annulée par erreur.
/// </summary>
public enum EnrollmentStatus
{
    Pending,
    Confirmed,
    Cancelled,
    DroppedOut,
    Transferred
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
    /// <summary>
    /// État initial d'un abonnement créé à l'approbation d'une demande self-service (ticket JGK-I03,
    /// docs/Volume_1_Cahier_des_Charges.md §11.5, docs/Volume_3_DDS.md §5.6) : aucune date d'expiration
    /// tant que le premier paiement n'est pas confirmé (JGK-I06). L'accès reste restreint au strict
    /// paiement tant que l'abonnement est dans cet état (mode restreint, ticket JGK-I04).
    /// </summary>
    AwaitingPayment,
    Active,
    Suspended,
    ReadOnly
}

/// <summary>
/// Cycle de vie d'une demande d'inscription self-service (ticket JGK-I01, docs/Volume_3_DDS.md §5.7).
/// Une demande ne devient JAMAIS une école par simple changement de statut : l'approbation (JGK-I03)
/// CRÉE une nouvelle ligne School/User/Subscription dans une transaction dédiée, la demande restant
/// un historique immuable de la candidature.
/// </summary>
public enum RegistrationRequestStatus
{
    Pending,
    Approved,
    Rejected
}

/// <summary>
/// Moyen de paiement d'un ABONNEMENT (ticket JGK-I05, docs/Volume_1_Cahier_des_Charges.md §11.6) —
/// distinct de <see cref="PaymentMethod"/> (encaissement de scolarité, JGK-F02) : deux domaines métier
/// différents, même si le vocabulaire se recoupe.
/// </summary>
public enum SubscriptionPaymentMethod
{
    MobileMoney,
    BankTransfer,
    Card
}

/// <summary>
/// Périodicité choisie pour un paiement d'abonnement (ticket JGK-I05, Volume 1 §11.6 : « mensuel ou
/// annuel »). Détermine à la fois le montant facturé (docs/Volume_3_DDS.md n'a pas de table de tarifs —
/// voir ISubscriptionPricingProvider) et, une fois le paiement confirmé, la nouvelle date d'expiration
/// de l'abonnement (JGK-I06, pas encore livré) — d'où sa présence sur SubscriptionPayment bien qu'absente
/// du schéma Volume_3_DDS §5.8 : sans elle, le futur traitement du webhook ne pourrait pas savoir de
/// combien prolonger l'abonnement.
/// </summary>
public enum BillingPeriod
{
    Monthly,
    Yearly
}

/// <summary>
/// Statut d'un paiement d'abonnement (ticket JGK-I05/I06, docs/Volume_3_DDS.md §5.8). Ne passe à
/// <see cref="Confirmed"/> QUE via le traitement d'un webhook signé (JGK-I06, AGENTS.md règle #11) —
/// aucune route accessible au Directeur ne positionne ce statut.
/// </summary>
public enum SubscriptionPaymentStatus
{
    Initiated,
    Confirmed,
    Failed
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

/// <summary>
/// Statut d'un élève à un appel (ticket JGK-D06). <c>Late</c> s'accompagne d'un nombre de minutes de
/// retard strictement positif (StudentAttendance.LateMinutes) ; les autres statuts portent toujours
/// zéro minute. Un retard n'est PAS une absence : c'est une présence tardive, comptée séparément.
/// </summary>
public enum AttendanceStatus
{
    Present,
    JustifiedAbsence,
    UnjustifiedAbsence,
    Late
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
