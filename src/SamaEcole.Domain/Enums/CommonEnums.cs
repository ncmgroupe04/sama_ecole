namespace SamaEcole.Domain.Enums;

public enum EntityStatus
{
    Active,
    Suspended,
    Blocked
}

/// <summary>
/// Type d'établissement : détermine si le module Finance (Caisse, recouvrement, frais mensuel,
/// tableau de bord financier) est actif ou masqué. Les écoles publiques sénégalaises n'ont pas de
/// recouvrement mensuel — seuls les frais ponctuels (APE, inscription) et les documents pédagogiques
/// (certificats, bulletins) les concernent. Valeur par défaut : <see cref="Prive"/> — les écoles
/// existantes conservent leur accès financier sans aucune migration manuelle.
/// </summary>
public enum TypeEtablissement
{
    /// <summary>École privée : accès complet Finance, Caisse, recouvrement mensuel, dashboard financier.</summary>
    Prive,

    /// <summary>École publique : module financier masqué dans la navigation. Documents pédagogiques mis en avant.</summary>
    Public
}


/// <summary>
/// Cycle d'enseignement d'une classe. Détermine notamment le barème de notation appliqué au bulletin :
/// <c>Maternelle</c> et <c>Primaire</c> sont notés sur /10, <c>College</c> et <c>Lycee</c> sur /20
/// (système sénégalais). DÉRIVÉ du champ <see cref="SamaEcole.Domain.Entities.Classroom.Level"/> par
/// <c>ClassroomCycle.CycleFor</c> — jamais saisi séparément, sous peine de voir les deux se contredire
/// (c'est exactement ce qui a laissé toutes les classes sur College jusqu'au 2026-07-21).
/// </summary>
public enum CycleType
{
    Primaire,
    College,
    Lycee,

    /// <summary>
    /// Préscolaire (Crèche, Maternelle). AJOUTÉ EN FIN d'énumération à dessein : les membres existants
    /// gardent ainsi leur valeur entière, et <c>default(CycleType)</c> reste <see cref="Primaire"/> —
    /// dont dépend le réglage de sentinelle de ClassroomConfiguration. La colonne étant persistée en
    /// string, l'ordre n'a aucune incidence en base.
    /// </summary>
    Maternelle
}

public static class CycleTypeExtensions
{
    /// <summary>
    /// Cycles à notation SIMPLIFIÉE : barème /10, moyenne simple sans coefficients, ni mentions ni
    /// appréciations, et tableau de bulletin épuré. Une seule source de vérité pour cette distinction,
    /// partagée par Application (GradingScaleGuard, GetGradeSummary) et Infrastructure
    /// (ReportCardDocument) — sans quoi chacun réinventerait le test et finirait par diverger.
    /// </summary>
    public static bool UsesSimplifiedGrading(this CycleType cycle)
        => cycle is CycleType.Primaire or CycleType.Maternelle;
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
    Canteen,
    Other,
    Transport,
    Uniformes,
    ActivitesPeriscolaires,
    FournituresVente
}

public enum CashierSessionStatus
{
    Open,
    Closed,
    Verified
}

public enum SubscriptionPlan
{
    Primaire,
    Standard,
    Premium
}

/// <summary>
/// Type de réduction porté par un code promo (module Tarification &amp; Promotions, espace Super
/// Admin). FreeTrialMonths et FullDiscount ne font transiter aucun argent : ils court-circuitent
/// l'agrégateur de paiement plutôt que de générer un paiement à 0 FCFA « confirmé » sans webhook
/// (AGENTS.md règle #11).
/// </summary>
public enum PromoDiscountType
{
    Percentage,
    FixedAmount,
    FreeTrialMonths,
    FullDiscount
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
/// Type d'évaluation d'une note (ticket JGK-G01, Volume 1 §8.1) : deux devoirs (Devoir1, Devoir2) et
/// une Composition par matière. La moyenne des devoirs (Devoir1/Devoir2) est ensuite moyennée avec la
/// Composition pour la moyenne de matière (GradeCalculator) — jamais une note unique.
/// </summary>
public enum EvaluationType
{
    Devoir1,
    Devoir2,
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

/// <summary>
/// Distinction du conseil de classe (docs/design-references/bulletin-reference.png : ligne Blâme /
/// Avertissement / Tableau d'honneur / Encouragements / Félicitations). Au plus UNE par
/// (élève, trimestre) — un conseil ne prononce pas deux distinctions contradictoires à la fois.
/// </summary>
public enum DisciplinaryMention
{
    Blame,
    Avertissement,
    TableauHonneur,
    Encouragements,
    Felicitations
}

/// <summary>
/// Décision du conseil de classe à l'issue d'un trimestre (docs/design-references/bulletin-reference.png :
/// bloc « Décision du Conseil »). PAS de membre <c>None</c> : « aucune décision encore prise » est
/// l'absence de valeur sur <see cref="Entities.ReportCardRemark.CouncilDecision"/> (nullable), jamais une
/// valeur d'énumération supplémentaire — même parti pris que <see cref="DisciplinaryMention"/>, qui
/// évite qu'un état "rien coché" soit représentable de deux façons différentes (null ET None).
/// </summary>
public enum CouncilDecision
{
    Admitted,
    AllowedToRepeat,
    Excluded
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
    Receipt,

    /// <summary>
    /// Numéro de certificat de mutation (ticket JGK-M06, ex. « MUT-2026-0007 »). Même compteur, même
    /// garantie : une pièce officielle remise à une famille et opposable à l'école d'accueil ne peut
    /// ni porter un numéro en double, ni laisser un trou inexpliqué dans la série.
    /// </summary>
    MutationCertificate,

    /// <summary>
    /// Séquence de l'IEN PROVISOIRE (ticket JGK-M01). Passe par le même compteur pour hériter de sa
    /// sérialisation sous concurrence — deux inscriptions simultanées ne peuvent pas recevoir le même
    /// numéro. Voir <c>IIenGeneratorService</c> et sa mise en garde : ce n'est pas un IEN officiel.
    /// </summary>
    ProvisionalIen
}

/// <summary>
/// Type de sanction disciplinaire.
/// </summary>
public enum DisciplineType
{
    Avertissement,
    Blame,
    Retenue,
    Exclusion
}

public enum ContractType
{
    Permanent,
    Vacataire
}

/// <summary>
/// Moyen de règlement du salaire d'un membre du personnel (ticket JGK-K02). Distinct de
/// <see cref="PaymentMethod"/> — celui-ci décrit un encaissement élève, jamais un versement RH ;
/// les deux domaines ne doivent jamais partager une énumération, même si Wave/Orange Money s'y
/// retrouvent conceptuellement des deux côtés.
/// </summary>
public enum PayoutMethod
{
    Cash,
    BankTransfer,
    Wave,
    OrangeMoney
}

/// <summary>
/// Nature d'un changement journalisé dans <see cref="Entities.EmployeeContractHistory"/> (Volume 1
/// §14.1). <c>Amended</c> : changement de rémunération sur un contrat resté actif (salaire de base,
/// taux horaire, prime de transport). <c>Closed</c> : clôture définitive — les montants ne changent
/// pas, seule <see cref="Entities.EmployeeContract.EndDate"/> est posée.
/// </summary>
public enum EmployeeContractChangeType
{
    Amended,
    Closed
}

/// <summary>
/// Type d'une salle physique (module Infrastructures, <see cref="Entities.Room"/>).
/// </summary>
public enum RoomType
{
    SalleDeClasse,
    Laboratoire,
    Bureau,
    Autre
}
