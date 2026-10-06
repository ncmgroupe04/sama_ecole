using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Subscriptions;

/// <summary>
/// Souscription commerciale de l'établissement courant (profil, tranche, plafonds, modules).
/// Les énumérations sortent en TEXTE, comme partout ailleurs dans l'API.
/// </summary>
public sealed record TenantSubscriptionDto(
    Guid Id,
    Guid SchoolId,
    string ProfileType,
    string StudentQuotaTier,
    int MaxStudentLimit,
    int SoftQuotaLimit,
    string Status,
    bool IsPedagogyEnabled,
    bool IsFinanceEnabled,
    bool IsInternatEnabled,
    bool IsCoranModuleEnabled);

/// <summary>
/// Corps de POST /onboarding/select-profile : le Directeur choisit son profil et sa tranche d'effectif.
/// Jamais de SchoolId ici — l'école vient du JWT (AGENTS.md règle #10) — et jamais de plafond : seule la
/// tranche est choisie, les plafonds se déduisent de <see cref="StudentQuotaDefaults"/>. La tranche sur
/// mesure (<see cref="StudentQuotaTier.Tier4_Custom"/>) est réservée au Super Admin.
/// </summary>
public sealed record SelectProfileRequest(ProfileType Profile, StudentQuotaTier Tier);

/// <summary>Position de l'école par rapport à son quota d'élèves.</summary>
public enum StudentQuotaState
{
    /// <summary>Effectif ≤ plafond nominal.</summary>
    WithinQuota,

    /// <summary>Plafond nominal dépassé mais tolérance (Soft cap) non atteinte : on avertit, on n'interdit pas.</summary>
    InTolerance,

    /// <summary>Tolérance dépassée : toute nouvelle création d'élève est refusée.</summary>
    Exceeded
}

/// <summary>Pourquoi la création d'un élève est refusée — pour que l'appelant choisisse le bon message.</summary>
public enum StudentAdmissionDenial
{
    None,

    /// <summary>Pas de souscription, ou souscription non <c>Active</c> (Onboarding à faire, en attente, suspendue, expirée).</summary>
    SubscriptionNotActive,

    /// <summary>La création ferait dépasser la tolérance (Soft cap).</summary>
    QuotaExceeded
}

/// <param name="CurrentStudentCount">Élèves vivants de l'école (hors supprimés logiquement).</param>
/// <param name="State">Position de l'effectif actuel — indépendante du statut de la souscription.</param>
/// <param name="CanAddStudent">Vrai si UN élève de plus peut être créé maintenant.</param>
public sealed record StudentQuotaStatus(
    int CurrentStudentCount,
    int MaxStudentLimit,
    int SoftQuotaLimit,
    StudentQuotaState State,
    bool CanAddStudent,
    StudentAdmissionDenial Denial);
