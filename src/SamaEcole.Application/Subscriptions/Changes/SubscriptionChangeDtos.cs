using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Subscriptions.Changes;

/// <summary>Tarif d'une formule. <see cref="Known"/> faux : l'école ne suit pas la grille par tranche, le support le chiffre.</summary>
public sealed record SubscriptionPriceQuote(decimal? MonthlyXof, decimal? AnnualXof, bool RequiresQuote, bool Known);

/// <summary>
/// Ce que montre la modale quand une modification directe est interceptée (et ce que renvoie l'aperçu à la demande) :
/// ce qui changerait, le tarif ajusté, et le corps de la demande à envoyer pour la faire valider.
/// </summary>
/// <param name="HasChanges">Faux : l'état demandé est l'état actuel, rien à faire.</param>
/// <param name="IsDowngrade">Réduction : elle est traitée par le support, jamais appliquée seule.</param>
/// <param name="Problems">Ce qui empêche la demande (effectif trop élevé pour la tranche, cycle qui contient des classes).</param>
/// <param name="HasPendingRequest">Une demande est déjà en attente : une seconde ne peut pas partir.</param>
/// <param name="Request">Le changement, tel qu'il se renvoie à POST /tenant-subscription/change-requests.</param>
public sealed record SubscriptionChangePreview(
    bool HasChanges,
    bool IsDowngrade,
    string Message,
    IReadOnlyList<ChangeLine> Changes,
    IReadOnlyList<string> Problems,
    SubscriptionPriceQuote Current,
    SubscriptionPriceQuote Target,
    decimal? DifferenceMonthlyXof,
    decimal? DifferenceAnnualXof,
    bool HasPendingRequest,
    SubscriptionChangeSet Request)
{
    public bool CanRequest => HasChanges && Problems.Count == 0 && !HasPendingRequest;
}

/// <summary>Une demande de changement, telle que la voient le Directeur et le Super Admin.</summary>
public sealed record SubscriptionChangeRequestDto(
    Guid Id,
    Guid SchoolId,
    string? SchoolName,
    string Status,
    bool IsDowngrade,
    IReadOnlyList<ChangeLine> Changes,
    decimal? CurrentMonthlyXof,
    decimal? CurrentAnnualXof,
    decimal? TargetMonthlyXof,
    decimal? TargetAnnualXof,
    decimal? DifferenceMonthlyXof,
    decimal? DifferenceAnnualXof,
    string? DirectorNote,
    string? ReviewNote,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReviewedAt)
{
    /// <summary>
    /// Renseigné par la réponse à une DÉCISION du Super Admin : l'e-mail au Directeur est-il parti ? <c>null</c> partout ailleurs.
    /// Faux n'annule rien — la décision est enregistrée — mais dit au Super Admin de prévenir le Directeur lui-même.
    /// </summary>
    public bool? DirectorNotified { get; init; }

    public static SubscriptionChangeRequestDto From(SubscriptionChangeRequest r, string? schoolName = null) => new(
        r.Id,
        r.SchoolId,
        schoolName,
        r.Status.ToString(),
        r.IsDowngrade,
        SubscriptionChangePlanner.Describe(ToState(r, current: true), ToState(r, current: false)),
        r.CurrentMonthlyXof,
        r.CurrentAnnualXof,
        r.TargetMonthlyXof,
        r.TargetAnnualXof,
        Difference(r.TargetMonthlyXof, r.CurrentMonthlyXof),
        Difference(r.TargetAnnualXof, r.CurrentAnnualXof),
        r.DirectorNote,
        r.ReviewNote,
        r.CreatedAt,
        r.ReviewedAt);

    public static decimal? Difference(decimal? target, decimal? current) =>
        target is { } t && current is { } c ? t - c : null;

    private static SubscriptionState ToState(SubscriptionChangeRequest r, bool current) => current
        ? new SubscriptionState(
            r.CurrentProfile, r.CurrentTier, Schools.ManagedCycleSet.FromStored(r.CurrentCycles), r.CurrentHasBoarding,
            new(r.CurrentIsPedagogyEnabled, r.CurrentIsFinanceEnabled, r.CurrentIsInternatEnabled, r.CurrentIsCoranModuleEnabled))
        : new SubscriptionState(
            r.TargetProfile, r.TargetTier, Schools.ManagedCycleSet.FromStored(r.TargetCycles), r.TargetHasBoarding,
            new(r.TargetIsPedagogyEnabled, r.TargetIsFinanceEnabled, r.TargetIsInternatEnabled, r.TargetIsCoranModuleEnabled));
}

/// <summary>Position de l'école par rapport à son quota, pour le bandeau du tableau de bord du Directeur.</summary>
/// <param name="State">WithinQuota | InTolerance | Exceeded.</param>
/// <param name="SuggestedTier">Tranche qui contiendrait l'effectif actuel ; <c>null</c> tant que l'école est dans son quota.</param>
/// <param name="OverQuotaSince">Date du premier constat de dépassement ; <c>null</c> tant que l'école est dans son quota.</param>
public sealed record QuotaOverviewDto(
    int CurrentStudentCount,
    int MaxStudentLimit,
    int SoftQuotaLimit,
    string State,
    string Tier,
    string TierLabel,
    string? SuggestedTier,
    string? SuggestedTierLabel,
    DateTimeOffset? OverQuotaSince,
    Guid? PendingChangeRequestId);

/// <summary>École en dépassement de quota, pour la relance par le Super Admin.</summary>
public sealed record OverQuotaSchoolDto(
    Guid SchoolId,
    string SchoolName,
    string Tier,
    int MaxStudentLimit,
    int SoftQuotaLimit,
    long StudentCount,
    string State,
    DateTimeOffset? OverQuotaSince);
