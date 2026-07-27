namespace SamaEcole.Domain.Enums;

/// <summary>
/// Fonctionnalité soumise à la formule d'abonnement de l'école (contrôle d'accès par formule).
///
/// N'y figurent QUE des fonctionnalités nouvelles ou explicitement premium : le socle métier
/// (élèves, classes, notes, inscriptions, encaissement, bulletins, documents) reste accessible à
/// TOUTES les formules. Verrouiller après coup une fonctionnalité déjà livrée retirerait un accès
/// à des écoles qui l'utilisent aujourd'hui — ce n'est pas une décision technique, elle appartient
/// au commerce ; ajouter un membre ici suffira le jour où elle sera prise.
/// </summary>
public enum Feature
{
    /// <summary>Notifications SMS aux parents (retards, absences, impayés, reçus).</summary>
    SmsNotifications,

    /// <summary>Rapports financiers consolidés (par cycle/classe/mode de paiement) et exports comptables.</summary>
    AdvancedFinancialReports,

    /// <summary>Groupe scolaire : plusieurs établissements pilotés depuis un même compte.</summary>
    MultiSchool
}

/// <summary>
/// SEULE source de vérité de la matrice formule → fonctionnalités, partagée par l'API
/// (FeatureAuthorizationHandler) et l'interface (GetSchoolFeaturesQuery, qui alimente les badges
/// « Premium » côté Alpine). Sans elle, le serveur et le client finiraient par diverger et
/// l'utilisateur verrait un bouton actif que l'API refuse.
/// </summary>
public static class PlanFeatures
{
    private static readonly Feature[] StandardFeatures = [Feature.AdvancedFinancialReports];

    private static readonly Feature[] PremiumFeatures =
        [Feature.AdvancedFinancialReports, Feature.SmsNotifications, Feature.MultiSchool];

    public static IReadOnlyList<Feature> For(SubscriptionPlan plan) => plan switch
    {
        SubscriptionPlan.Premium => PremiumFeatures,
        SubscriptionPlan.Standard => StandardFeatures,

        // Primaire — formule d'entrée : le socle métier uniquement (voir la remarque de Feature).
        _ => []
    };

    public static bool Includes(SubscriptionPlan plan, Feature feature) => For(plan).Contains(feature);

    /// <summary>Formule la plus basse qui inclut <paramref name="feature"/> — pour le message « Passez à … ».</summary>
    public static SubscriptionPlan MinimumPlanFor(Feature feature) =>
        Includes(SubscriptionPlan.Standard, feature) ? SubscriptionPlan.Standard : SubscriptionPlan.Premium;
}
