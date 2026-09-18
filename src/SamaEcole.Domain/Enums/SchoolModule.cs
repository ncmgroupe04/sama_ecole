namespace SamaEcole.Domain.Enums;

/// <summary>
/// Module métier activable/désactivable PAR ÉCOLE, au choix du Directeur (Paramètres › Modules) —
/// indépendant de la formule d'abonnement. À ne pas confondre avec <see cref="Feature"/>/PlanFeatures,
/// qui gouverne ce qu'une formule PAYANTE débloque : ici, une école Premium peut très bien désactiver
/// la Pédagogie si elle n'en a pas l'usage (ex. un établissement uniquement administratif/comptable).
///
/// L'état de chaque module vit sur <see cref="Entities.SchoolSettings"/> (IsPedagogyEnabled,
/// IsFinanceEnabled, IsInternatEnabled, IsCoranModuleEnabled), lu par ModuleAuthorizationHandler
/// (SamaEcole.Web.Authorization, [RequireModule]) et par la sidebar (sidebarNav() dans auth.js).
/// </summary>
public enum SchoolModule
{
    /// <summary>Classes/Matières restent hors de ce module (Paie et Frais par classe en dépendent) —
    /// couvre Notes/Bulletins, Examens officiels, Intégration étatique, et toute la Surveillance
    /// Générale (appel, billets, discipline, convocations, pointage enseignants).</summary>
    Pedagogy,

    /// <summary>Caisse, Frais scolaires, Paie, Fiscalité, Trésorerie, Rapports financiers.</summary>
    Finance,

    /// <summary>
    /// Réservé — aucun écran ni route ne dépend encore de ce module (produit non construit). Le
    /// réglage existe déjà côté Directeur pour ne pas rouvrir une migration le jour où il le sera.
    /// </summary>
    Internat,

    /// <summary>Réservé — filière Coranique/Franco-Arabe, même remarque que <see cref="Internat"/>.</summary>
    Coran
}
