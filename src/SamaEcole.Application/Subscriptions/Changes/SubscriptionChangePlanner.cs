using SamaEcole.Application.Registration;
using SamaEcole.Application.Schools;
using SamaEcole.Application.Schools.Commands.ApplyEstablishmentProfile;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Subscriptions.Changes;

/// <summary>
/// L'abonnement d'une école tel que le planificateur le voit : profil, tranche, cycles gérés, drapeau de facturation
/// « internat » et modules SOUSCRITS (les droits de <c>tenant_subscriptions</c>, pas les préférences de l'école).
/// </summary>
public sealed record SubscriptionState(
    ProfileType Profile,
    StudentQuotaTier Tier,
    IReadOnlyList<CycleType> Cycles,
    bool HasBoarding,
    ModulePreset Modules);

/// <summary>
/// Ce que le Directeur tente de modifier. Tout champ <c>null</c> est « inchangé » ; un champ qui reprend la valeur actuelle
/// n'est pas non plus un changement. Les modules ne servent qu'à ACTIVER un module non souscrit (couper un module
/// souscrit n'est pas une demande de changement : c'est une préférence).
/// </summary>
public sealed record SubscriptionChangeSet(
    ProfileType? Profile = null,
    StudentQuotaTier? Tier = null,
    IReadOnlyList<CycleType>? Cycles = null,
    bool? IsPedagogyEnabled = null,
    bool? IsFinanceEnabled = null,
    bool? IsInternatEnabled = null,
    bool? IsCoranModuleEnabled = null)
{
    public bool IsEmpty =>
        Profile is null && Tier is null && Cycles is null
        && IsPedagogyEnabled is null && IsFinanceEnabled is null && IsInternatEnabled is null && IsCoranModuleEnabled is null;
}

public enum PlanProblemKind
{
    /// <summary>La tranche sur mesure est fixée par l'équipe commerciale : elle ne se demande pas.</summary>
    TierNotRequestable,

    /// <summary>L'effectif actuel dépasse déjà le plafond de la tranche demandée.</summary>
    TierBelowHeadcount,

    /// <summary>Un cycle retiré contient encore des classes vivantes.</summary>
    CycleHasClassrooms,

    NoCycle,

    /// <summary>Le profil demandé ne gère pas ces cycles (Élémentaire : ni Collège ni Lycée).</summary>
    CyclesNotAllowedForProfile
}

public sealed record PlanProblem(PlanProblemKind Kind, string Message);

/// <summary>Une ligne lisible du changement : « Cycles : Primaire, Collège → Primaire, Collège, Lycée ».</summary>
public sealed record ChangeLine(string Field, string From, string To);

/// <param name="Target">État COMPLET après changement (c'est lui que la validation écrit).</param>
/// <param name="IsNoOp">L'état cible est l'état actuel : rien à faire, rien à payer.</param>
/// <param name="IsDowngrade">Réduction : tranche inférieure, cycle retiré, module souscrit qui disparaît.</param>
public sealed record ChangePlan(
    SubscriptionState Target,
    bool IsNoOp,
    bool IsDowngrade,
    IReadOnlyList<ChangeLine> Lines,
    IReadOnlyList<PlanProblem> Problems);

/// <summary>
/// Calcule l'état cible d'un changement d'abonnement, SANS accès base de données (testable à plat). Les mêmes règles
/// servent l'interception d'une modification directe (aperçu du tarif ajusté), la création de la demande et sa
/// validation : aucune ne peut dériver des autres.
///
/// Règles :
///   · profil : il réapplique le preset de modules du profil (comme à l'inscription), l'internat souscrit s'y ajoute ;
///     un profil Élémentaire restreint les cycles à Maternelle et Primaire ;
///   · tranche : plus haute = montée en gamme, plus basse = réduction ; jamais en dessous de l'effectif actuel ;
///   · cycles : en ajouter = montée en gamme, en retirer = réduction (et refusé tant qu'un cycle retiré a des classes) ;
///   · internat : l'activer pose le drapeau de facturation « l'école héberge des élèves » (majoration de la grille), le
///     couper le lève ; les autres modules s'ajoutent sans effet sur le tarif.
/// </summary>
public static class SubscriptionChangePlanner
{
    public static ChangePlan Plan(
        SubscriptionState current,
        SubscriptionChangeSet change,
        int currentStudentCount,
        IReadOnlyDictionary<CycleType, int> classroomsByCycle)
    {
        var problems = new List<PlanProblem>();

        var profile = change.Profile ?? current.Profile;
        var profileChanged = profile != current.Profile;
        var tier = change.Tier ?? current.Tier;
        var hasBoarding = current.HasBoarding;

        // --- Cycles : explicites, ou restreints par un profil Élémentaire
        IReadOnlyList<CycleType> cycles = Canonical(change.Cycles ?? current.Cycles);

        if (change.Cycles is null && profileChanged)
        {
            var allowed = RegistrationOffer.AllowedCycles(profile);
            var kept = cycles.Where(allowed.Contains).ToList();
            cycles = kept.Count > 0 ? Canonical(kept) : Canonical(allowed);
        }

        // --- Modules : preset du profil s'il change, puis activations explicites
        var modules = current.Modules;

        if (profileChanged)
        {
            modules = RegistrationOffer.ModulesFor(profile, hasBoarding);
        }

        modules = modules with
        {
            IsPedagogyEnabled = change.IsPedagogyEnabled ?? modules.IsPedagogyEnabled,
            IsFinanceEnabled = change.IsFinanceEnabled ?? modules.IsFinanceEnabled,
            IsInternatEnabled = change.IsInternatEnabled ?? modules.IsInternatEnabled,
            IsCoranModuleEnabled = change.IsCoranModuleEnabled ?? modules.IsCoranModuleEnabled
        };

        // Le drapeau de facturation suit l'activation EXPLICITE de l'internat — pas le preset d'un profil, qui l'inclut
        // sans pensionnaires (Halqas) et ne doit donc pas faire payer la majoration.
        if (change.IsInternatEnabled is { } internat && internat != current.Modules.IsInternatEnabled)
        {
            hasBoarding = internat;
        }

        var target = new SubscriptionState(profile, tier, cycles, hasBoarding, modules);

        // --- Problèmes
        if (change.Tier == StudentQuotaTier.Tier4_Custom)
        {
            problems.Add(new PlanProblem(PlanProblemKind.TierNotRequestable,
                "La tranche sur mesure est fixée par l'équipe commerciale : demandez l'offre « Grand Groupe » ou contactez-nous."));
        }
        else if (change.Tier is not null && StudentQuotaDefaults.For(tier).Max < currentStudentCount)
        {
            problems.Add(new PlanProblem(PlanProblemKind.TierBelowHeadcount,
                $"Votre effectif actuel ({currentStudentCount} élèves) dépasse cette tranche : choisissez une tranche qui le contient."));
        }

        if (cycles.Count == 0)
        {
            problems.Add(new PlanProblem(PlanProblemKind.NoCycle, "Cochez au moins un cycle géré par votre établissement."));
        }

        if (change.Cycles is not null && !cycles.All(RegistrationOffer.AllowedCycles(profile).Contains))
        {
            problems.Add(new PlanProblem(PlanProblemKind.CyclesNotAllowedForProfile,
                "Un établissement élémentaire ne gère ni le collège ni le lycée : choisissez le profil « Enseignement général » pour ces cycles."));
        }

        var removed = ManagedCycleSet.Removed(current.Cycles, cycles);
        var blocking = removed.Where(c => classroomsByCycle.GetValueOrDefault(c) > 0).ToList();

        if (blocking.Count > 0)
        {
            var parts = blocking.Select(c =>
                $"Le cycle {ManagedCycleSet.FrenchLabel(c)} compte {classroomsByCycle[c]} classe{(classroomsByCycle[c] > 1 ? "s" : "")}.");

            problems.Add(new PlanProblem(PlanProblemKind.CycleHasClassrooms,
                $"{string.Join(" ", parts)} Supprimez-les ou déplacez-les avant de retirer ce cycle."));
        }

        // --- Changement ? réduction ?
        var lines = Describe(current, target);
        var isNoOp = lines.Count == 0;

        var isDowngrade =
            QuotaTierResolver.Rank(tier) < QuotaTierResolver.Rank(current.Tier)
            || removed.Count > 0
            || (current.Modules.IsPedagogyEnabled && !modules.IsPedagogyEnabled)
            || (current.Modules.IsFinanceEnabled && !modules.IsFinanceEnabled)
            || (current.Modules.IsInternatEnabled && !modules.IsInternatEnabled)
            || (current.Modules.IsCoranModuleEnabled && !modules.IsCoranModuleEnabled);

        return new ChangePlan(target, isNoOp, isDowngrade, lines, problems);
    }

    private static IReadOnlyList<CycleType> Canonical(IEnumerable<CycleType> cycles)
    {
        var set = cycles.ToHashSet();
        return ManagedCycleSet.All.Where(set.Contains).ToList();
    }

    /// <summary>Les lignes lisibles qui séparent deux états (vide si identiques).</summary>
    public static List<ChangeLine> Describe(SubscriptionState from, SubscriptionState to)
    {
        var lines = new List<ChangeLine>();

        if (from.Profile != to.Profile)
        {
            lines.Add(new ChangeLine("Profile", ProfileLabel(from.Profile), ProfileLabel(to.Profile)));
        }

        if (from.Tier != to.Tier)
        {
            lines.Add(new ChangeLine("Tier", TierLabel(from.Tier), TierLabel(to.Tier)));
        }

        if (!from.Cycles.SequenceEqual(to.Cycles))
        {
            lines.Add(new ChangeLine("Cycles", CyclesLabel(from.Cycles), CyclesLabel(to.Cycles)));
        }

        AddModule(lines, "Pedagogy", "Pédagogie", from.Modules.IsPedagogyEnabled, to.Modules.IsPedagogyEnabled);
        AddModule(lines, "Finance", "Finance", from.Modules.IsFinanceEnabled, to.Modules.IsFinanceEnabled);
        AddModule(lines, "Internat", "Internat", from.Modules.IsInternatEnabled, to.Modules.IsInternatEnabled);
        AddModule(lines, "Coran", "Suivi coranique", from.Modules.IsCoranModuleEnabled, to.Modules.IsCoranModuleEnabled);

        // Le drapeau de facturation seul (cas rare : il ne bouge qu'avec le module Internat, déjà décrit ci-dessus).
        if (from.HasBoarding != to.HasBoarding && !lines.Any(l => l.Field == "Internat"))
        {
            lines.Add(new ChangeLine("Internat", from.HasBoarding ? "avec internat" : "sans internat", to.HasBoarding ? "avec internat" : "sans internat"));
        }

        return lines;
    }

    private static void AddModule(List<ChangeLine> lines, string field, string label, bool from, bool to)
    {
        if (from != to)
        {
            lines.Add(new ChangeLine(field, $"{label} : {(from ? "inclus" : "non inclus")}", $"{label} : {(to ? "inclus" : "non inclus")}"));
        }
    }

    public static string ProfileLabel(ProfileType profile) => profile switch
    {
        ProfileType.EnseignementGeneral => "Enseignement général",
        ProfileType.FrancoArabe => "Franco-arabe",
        ProfileType.Elementaire => "Élémentaire",
        ProfileType.InternatDaara => "Daara moderne (internat)",
        ProfileType.DaaraMemorisation => "Daara — Mémorisation",
        ProfileType.ComptabiliteRapports => "Comptabilité & rapports",
        _ => profile.ToString()
    };

    public static string TierLabel(StudentQuotaTier tier) => tier switch
    {
        StudentQuotaTier.Tier3_800 => "Grand Groupe (sur devis)",
        StudentQuotaTier.Tier4_Custom => "Sur mesure (sur devis)",
        _ => $"jusqu'à {StudentQuotaDefaults.For(tier).Max} élèves"
    };

    private static string CyclesLabel(IEnumerable<CycleType> cycles) =>
        string.Join(", ", cycles.Select(ManagedCycleSet.FrenchLabel));
}
