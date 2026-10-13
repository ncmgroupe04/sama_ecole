using SamaEcole.Application.Schools;
using SamaEcole.Application.Schools.Commands.ApplyEstablishmentProfile;
using SamaEcole.Application.Subscriptions;
using SamaEcole.Application.Subscriptions.Pricing;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Registration;

public enum OfferProblemKind
{
    /// <summary>Effectif ou tranche au-delà de la dernière tranche standard : « Grand Groupe », sur devis, jamais en libre-service.</summary>
    RequiresQuote,

    /// <summary>La tranche cochée est plus petite que celle que l'effectif déclaré impose.</summary>
    TierTooSmall
}

/// <param name="MinimumTier">Tranche minimale pour l'effectif déclaré (renseignée pour <see cref="OfferProblemKind.TierTooSmall"/>).</param>
public sealed record OfferProblem(OfferProblemKind Kind, StudentQuotaTier? MinimumTier);

/// <summary>
/// Règles de l'OFFRE déclarée à l'inscription — profil, cycles cochés, internat, tranche d'effectif — sans accès
/// base de données (testables à plat). Servent trois fois : à la soumission (validation), à l'approbation (même
/// contrôle, la demande a pu attendre) et au provisionnement (ce que la souscription et les réglages reçoivent).
/// </summary>
public static class RegistrationOffer
{
    /// <summary>
    /// Profils académiques (notes, bulletins) : le Directeur doit dire quels cycles il gère. Un Daara de pure
    /// mémorisation et un profil Comptabilité n'ont pas de cycles académiques à déclarer.
    /// </summary>
    public static bool RequiresCycles(ProfileType profile) =>
        EstablishmentProfilePresets.For(ProfileTypeMapping.ToEstablishmentProfile(profile)).IsPedagogyEnabled;

    /// <summary>Cycles qu'un profil peut cocher : un profil Élémentaire ne gère ni Collège ni Lycée.</summary>
    public static IReadOnlyList<CycleType> AllowedCycles(ProfileType profile) =>
        profile == ProfileType.Elementaire ? ManagedCycleSet.Elementary : ManagedCycleSet.All;

    /// <summary>
    /// Profil de cycles de la GRILLE HISTORIQUE (<see cref="SchoolCycleProfile"/>), toujours renseigné sur la demande :
    /// un cycle → ce cycle (le préscolaire se range avec le Primaire, la grille n'a pas de Maternelle), deux →
    /// Bicycle, trois ou quatre → Complexe, aucun (Daara, Comptabilité) → Primaire. Il ne pilote que le plan de
    /// droits de fonctionnalités (<see cref="SubscriptionPricingGrid.PlanFor"/>), jamais le montant : celui-ci vient
    /// de la tranche d'effectif.
    /// </summary>
    public static SchoolCycleProfile CycleProfileFor(IReadOnlyCollection<CycleType> cycles) => cycles.Count switch
    {
        0 => SchoolCycleProfile.Primaire,
        1 => cycles.Single() switch
        {
            CycleType.College => SchoolCycleProfile.College,
            CycleType.Lycee => SchoolCycleProfile.Lycee,
            _ => SchoolCycleProfile.Primaire
        },
        2 => SchoolCycleProfile.Bicycle,
        _ => SchoolCycleProfile.Complexe
    };

    /// <summary>
    /// Modules activés : le preset du profil, auquel l'Internat s'ajoute si l'école héberge des élèves. L'internat
    /// n'ENLÈVE jamais un module : les Halqas et le suivi coranique vivent derrière le module Internat (décision
    /// G-D1), un profil Franco-Arabe ou Daara le garde donc même sans pensionnaires — seule la facturation
    /// (majoration Internat) dépend de <paramref name="hasBoarding"/>.
    /// </summary>
    public static ModulePreset ModulesFor(ProfileType profile, bool hasBoarding)
    {
        var preset = EstablishmentProfilePresets.For(ProfileTypeMapping.ToEstablishmentProfile(profile));

        return preset with { IsInternatEnabled = preset.IsInternatEnabled || hasBoarding };
    }

    /// <summary>Cycles gérés à stocker dans les réglages : ceux cochés, sinon tous (la colonne n'accepte jamais « aucun »).</summary>
    public static string ManagedCyclesFor(IReadOnlyCollection<CycleType>? chosen) =>
        chosen is { Count: > 0 } ? ManagedCycleSet.Serialize(chosen) : ManagedCycleSet.AllStored;

    /// <summary>Forme stockée sur la demande ; <c>null</c> quand aucun cycle n'a été coché.</summary>
    public static string? SerializeCycles(IReadOnlyCollection<CycleType>? cycles) =>
        cycles is { Count: > 0 } ? ManagedCycleSet.Serialize(cycles) : null;

    public static IReadOnlyList<CycleType> ParseCycles(string? stored) =>
        string.IsNullOrWhiteSpace(stored) ? [] : ManagedCycleSet.FromStored(stored);

    /// <summary>
    /// Anti-fraude : l'effectif déclaré doit tenir dans la tranche choisie, et au-delà de la dernière tranche
    /// standard il n'y a plus de libre-service. <c>null</c> = offre cohérente. Surpayer (tranche plus grande que
    /// nécessaire) reste permis.
    /// </summary>
    public static OfferProblem? Check(int declaredStudentCount, StudentQuotaTier chosenTier)
    {
        var lastStandardMax = StudentQuotaDefaults.For(QuotaTierPricing.PricedTiers().Last()).Max;

        if (QuotaTierResolver.RequiresQuote(chosenTier) || declaredStudentCount > lastStandardMax)
        {
            return new OfferProblem(OfferProblemKind.RequiresQuote, null);
        }

        return QuotaTierResolver.IsSufficientFor(chosenTier, declaredStudentCount)
            ? null
            : new OfferProblem(OfferProblemKind.TierTooSmall, QuotaTierResolver.MinimumTierFor(declaredStudentCount));
    }

    /// <summary>Phrase destinée au demandeur (ou au Super Admin) pour un <see cref="OfferProblem"/>.</summary>
    public static string Describe(OfferProblem problem, int declaredStudentCount)
    {
        if (problem.Kind == OfferProblemKind.RequiresQuote)
        {
            return $"Au-delà de {StudentQuotaDefaults.For(QuotaTierPricing.PricedTiers().Last()).Max} élèves, l'offre « Grand Groupe » " +
                   "est établie sur devis : contactez-nous pour finaliser votre inscription.";
        }

        var (max, _) = StudentQuotaDefaults.For(problem.MinimumTier!.Value);

        return $"Votre effectif déclaré ({declaredStudentCount} élèves) dépasse la capacité de la tranche choisie : " +
               $"choisissez au moins la tranche « jusqu'à {max} élèves ».";
    }
}
