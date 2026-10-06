namespace SamaEcole.Domain.Enums;

/// <summary>
/// Profil commercial et fonctionnel d'un établissement, tel que choisi à l'Onboarding SaaS et porté par
/// <see cref="Entities.TenantSubscription"/>. Persisté en TEXTE (jamais un entier qui se briserait
/// silencieusement si l'ordre des membres changeait).
///
/// À NE PAS CONFONDRE avec <see cref="ProfileEtablissement"/> (réglage d'Onboarding historique, porté par
/// <c>SchoolSettings</c> et seul consommé aujourd'hui par la sidebar) : ce dernier reste la référence tant
/// que les écrans n'ont pas été rebranchés sur la souscription. Correspondance utilisée par la migration de
/// reprise : Simplifie → ComptabiliteRapports, ElementairePrimaire → Elementaire, General →
/// EnseignementGeneral, FrancoArabe → FrancoArabe, DaaraInternat → InternatDaara.
/// </summary>
public enum ProfileType
{
    /// <summary>Élémentaire / Primaire : socle académique sans Séries ni Secondaire.</summary>
    Elementaire,

    /// <summary>Franco-Arabe : socle académique + bilinguisme, matières arabes/islamiques.</summary>
    FrancoArabe,

    /// <summary>Internat / Daara Moderne : socle académique + Internat + suivi coranique.</summary>
    InternatDaara,

    /// <summary>Enseignement Général (multi-cycles) : socle académique complet. Profil par défaut des écoles existantes sans profil.</summary>
    EnseignementGeneral,

    /// <summary>Comptabilité &amp; Rapports : Pédagogie masquée, Caisse et rapports financiers seuls.</summary>
    ComptabiliteRapports
}

/// <summary>
/// Tranche d'effectif choisie par le Directeur (ou fixée par le Super Admin). Les plafonds associés à
/// chaque tranche sont dans <see cref="Entities.StudentQuotaDefaults"/>. Persisté en TEXTE.
/// </summary>
public enum StudentQuotaTier
{
    /// <summary>Jusqu'à 150 élèves (tolérance 160).</summary>
    Tier1_150,

    /// <summary>Jusqu'à 400 élèves (tolérance 420).</summary>
    Tier2_400,

    /// <summary>Jusqu'à 800 élèves (tolérance 830).</summary>
    Tier3_800,

    /// <summary>Plafond sur mesure fixé par le Super Admin — illimité (<see cref="int.MaxValue"/>) tant qu'il n'a rien fixé.</summary>
    Tier4_Custom
}

/// <summary>
/// Cycle de vie d'une <see cref="Entities.TenantSubscription"/>. Persisté en TEXTE. Axe DISTINCT de
/// <see cref="SubscriptionStatus"/> (état de paiement de l'abonnement de facturation) : celui-ci décrit
/// l'état de la configuration commerciale (profil + tranche), pas l'encaissement.
/// </summary>
public enum TenantSubscriptionStatus
{
    /// <summary>
    /// Nouvelle école : le Directeur n'a pas encore choisi son profil ni sa tranche. L'accès est borné à
    /// l'écran /onboarding/select-profile et aucun élève ne peut être créé tant que l'état dure.
    /// </summary>
    PendingOnboarding,

    /// <summary>Choix fait, en attente de validation par le Super Admin.</summary>
    PendingApproval,

    Active,
    Suspended,
    Expired
}
