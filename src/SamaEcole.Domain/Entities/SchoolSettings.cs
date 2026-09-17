using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Paramètres d'un établissement (ticket JGK-B02, openapi.yaml §SchoolSettings).
///
/// Table tenant à part entière (SchoolId + RLS + Global Query Filter), et non des colonnes de
/// `schools` : `schools` échappe volontairement à la RLS puisqu'elle DÉFINIT le tenant. Y loger les
/// paramètres les priverait de toute protection en base, alors qu'ils pilotent des règles métier —
/// à commencer par le format des matricules.
/// </summary>
public class SchoolSettings : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    /// <summary>Barème de notation : 10 ou 20 (docs/Volume_1_Cahier_des_Charges.md).</summary>
    public int GradingScale { get; set; } = SchoolSettingsDefaults.GradingScale;

    /// <summary>Ex. « ELEV-{YEAR}-{SEQ:4} » — voir MatriculeFormat.</summary>
    public string StudentMatriculeFormat { get; set; } = SchoolSettingsDefaults.StudentMatriculeFormat;

    public string TeacherMatriculeFormat { get; set; } = SchoolSettingsDefaults.TeacherMatriculeFormat;

    /// <summary>Déconnexion automatique par inactivité (ticket JGK-A06, gelé — la valeur est déjà stockée).</summary>
    public int AutoLogoutMinutes { get; set; } = SchoolSettingsDefaults.AutoLogoutMinutes;

    public string DateFormat { get; set; } = SchoolSettingsDefaults.DateFormat;

    /// <summary>
    /// Nombre de mensualités facturées sur une année scolaire (ticket JGK-E01). Le calcul du montant
    /// dû à l'inscription multiplie chaque mensualité par ce nombre ; un frais ponctuel n'est jamais
    /// multiplié. Les écoles sénégalaises raisonnent en « tranches » (souvent 9), d'où un réglage
    /// explicite plutôt qu'une déduction des dates de l'année — celles-ci peuvent couvrir des mois
    /// non facturés.
    /// </summary>
    public int TuitionMonthsPerYear { get; set; } = SchoolSettingsDefaults.TuitionMonthsPerYear;

    /// <summary>
    /// Ticket JGK-G02 — au choix du Directeur de CHAQUE école (colonne, pas un rôle codé en dur) :
    /// délègue au Secrétariat la gestion du barème, des matières/coefficients et des mentions du
    /// bulletin. Lu par CanManageGradingScaleHandler (SamaEcole.Web.Authorization), jamais par une
    /// simple comparaison de rôle. Faux par défaut : la délégation est un choix explicite du
    /// Directeur, pas un acquis silencieux à l'activation du module.
    /// </summary>
    public bool AllowSecretaryToManageGrading { get; set; } = SchoolSettingsDefaults.AllowSecretaryToManageGrading;

    /// <summary>
    /// Au choix du Directeur de CHAQUE école : délègue à la Finance le droit d'ajuster un montant de
    /// barème déjà défini (PUT /finance/fees/{id}), normalement réservé au Directeur (AGENTS.md règle
    /// #4). Lu par CanModifyFeesHandler (SamaEcole.Web.Authorization). Faux par défaut : la Finance
    /// encaisse, elle ne fixe pas les montants tant que le Directeur n'a pas explicitement délégué.
    /// </summary>
    public bool AllowFinanceToModifyFees { get; set; } = SchoolSettingsDefaults.AllowFinanceToModifyFees;

    /// <summary>
    /// Au choix du Directeur de CHAQUE école : délègue à la Finance le droit de supprimer (soft
    /// delete) une catégorie de frais ou une ligne de barème. Séparé de <see cref="AllowFinanceToModifyFees"/>
    /// : supprimer une catégorie entière est un geste plus lourd de conséquences (elle disparaît de
    /// toute l'interface) qu'ajuster un montant, donc un commutateur distinct. Lu par
    /// CanDeleteFeesHandler (SamaEcole.Web.Authorization). Faux par défaut.
    /// </summary>
    public bool AllowFinanceToDeleteFees { get; set; } = SchoolSettingsDefaults.AllowFinanceToDeleteFees;

    /// <summary>URL de l'image de la signature du directeur, injectée sur les reçus et bulletins.</summary>
    public string? DirectorSignatureUrl { get; set; }

    /// <summary>URL de l'image de la signature du secrétariat, injectée sur les certificats et bulletins.</summary>
    public string? SecretarySignatureUrl { get; set; }

    /// <summary>URL de l'image de la signature du caissier/service financier, injectée sur les reçus.</summary>
    public string? CashierSignatureUrl { get; set; }

    /// <summary>URL de l'image du cachet officiel de l'établissement, injecté sur les reçus et bulletins.</summary>
    public string? OfficialStampUrl { get; set; }

    /// <summary>URL de l'image de la signature du Surveillant Général, injectée sur les documents de Vie Scolaire (billets d'entrée/sortie, fiches de discipline).</summary>
    public string? SurveillantSignatureUrl { get; set; }

    /// <summary>
    /// Type d'établissement : Prive (défaut) ou Public. Pilote l'affichage du module Finance dans la
    /// navigation (sidebar). Rertrocompat : les écoles existantes (colonne absente) obtiennent Prive
    /// via la valeur par défaut de la migration — aucun accès financier ne leur est retiré.
    /// </summary>
    public TypeEtablissement TypeEtablissement { get; set; } = SchoolSettingsDefaults.TypeEtablissement;

    /// <summary>
    /// Solde de SMS restant, en SEGMENTS et non en messages (voir SmsMessage.SegmentCount) : un
    /// message long en consomme plusieurs, et un solde compté en messages divergerait de la facture
    /// de l'agrégateur.
    ///
    /// Décrémenté ATOMIQUEMENT en base par SmsDispatcher (UPDATE … WHERE solde ≥ coût), jamais par un
    /// lire-modifier-écrire côté application : deux alertes simultanées sur la même école feraient
    /// sinon disparaître un débit, et l'école enverrait des SMS qu'elle n'a pas payés.
    /// </summary>
    public int SmsCreditBalance { get; set; } = SchoolSettingsDefaults.SmsCreditBalance;

    /// <summary>Alerter le parent par SMS à la saisie d'un retard ou d'une absence.</summary>
    public bool SmsOnAttendanceAlert { get; set; } = SchoolSettingsDefaults.SmsAlertsEnabled;

    /// <summary>Relancer par SMS les impayés de scolarité.</summary>
    public bool SmsOnDuesReminder { get; set; } = SchoolSettingsDefaults.SmsAlertsEnabled;

    /// <summary>Confirmer par SMS chaque encaissement, avec le lien vers le reçu.</summary>
    public bool SmsOnPaymentReceipt { get; set; } = SchoolSettingsDefaults.SmsAlertsEnabled;

    /// <summary>
    /// Nombre de jours de retard au-delà duquel DebtorAgingHostedService inclut un débiteur dans un
    /// lot de relance brouillon (Étape 5 — recouvrement). N'a d'effet que si <see cref="SmsOnDuesReminder"/>
    /// est actif : ce réglage cadre le calcul, il ne l'active pas — même logique de double
    /// interrupteur que pour l'application d'un barème (portée, puis activation).
    /// </summary>
    public int DebtorReminderThresholdDays { get; set; } = SchoolSettingsDefaults.DebtorReminderThresholdDays;

    /// <summary>
    /// Modules activés/désactivés par le Directeur, indépendamment de la formule d'abonnement — un
    /// second axe, distinct de <see cref="Domain.Enums.Feature"/>/PlanFeatures (formule payante). Lus
    /// par ModuleAuthorizationHandler (SamaEcole.Web.Authorization, [RequireModule]) et par la sidebar
    /// (sidebarNav() dans auth.js). Voir <see cref="Domain.Enums.SchoolModule"/>.
    ///
    /// Pédagogie et Finance forment le socle métier livré : activés par défaut, comme le reste du
    /// produit avant ce réglage. Internat et Coran n'ont encore aucun écran ni route derrière eux
    /// (réglage anticipé) : désactivés par défaut, sans effet aujourd'hui quelle que soit leur valeur.
    /// </summary>
    public bool IsPedagogyEnabled { get; set; } = SchoolSettingsDefaults.IsPedagogyEnabled;

    public bool IsFinanceEnabled { get; set; } = SchoolSettingsDefaults.IsFinanceEnabled;

    public bool IsInternatEnabled { get; set; } = SchoolSettingsDefaults.IsInternatEnabled;

    public bool IsCoranModuleEnabled { get; set; } = SchoolSettingsDefaults.IsCoranModuleEnabled;
}

/// <summary>
/// Valeurs par défaut d'un établissement neuf (critère du ticket JGK-B02 : « les valeurs par défaut
/// sont appliquées à la création »). Elles sont AUSSI la source de vérité de la fonction
/// provision_school_director : si l'une change ici, changer la migration correspondante.
/// </summary>
public static class SchoolSettingsDefaults
{
    public const int GradingScale = 20;
    public const string StudentMatriculeFormat = "ELEV-{YEAR}-{SEQ:4}";
    public const string TeacherMatriculeFormat = "ENS-{YEAR}-{SEQ:3}";
    public const int AutoLogoutMinutes = 10;
    public const string DateFormat = "dd/MM/yyyy";

    /// <summary>9 tranches — la convention la plus répandue au Sénégal (rentrée d'octobre, fin en juin).</summary>
    public const int TuitionMonthsPerYear = 9;

    /// <summary>Délégation au Secrétariat désactivée tant que le Directeur ne l'a pas explicitement activée.</summary>
    public const bool AllowSecretaryToManageGrading = false;

    /// <summary>Délégation à la Finance désactivée tant que le Directeur ne l'a pas explicitement activée.</summary>
    public const bool AllowFinanceToModifyFees = false;

    /// <summary>Délégation à la Finance désactivée tant que le Directeur ne l'a pas explicitement activée.</summary>
    public const bool AllowFinanceToDeleteFees = false;

    public static readonly int[] AllowedGradingScales = [10, 20];
    public static readonly string[] AllowedDateFormats = ["dd/MM/yyyy", "dd MMMM yyyy"];

    /// <summary>Bornes du nombre de mensualités : au moins 1 mois, au plus l'année civile complète.</summary>
    public const int MinTuitionMonths = 1;
    public const int MaxTuitionMonths = 12;

    /// <summary>Type d'établissement par défaut : Privé, pour garantir la rétrocompatibilité des écoles existantes.</summary>
    public const TypeEtablissement TypeEtablissement = Enums.TypeEtablissement.Prive;

    /// <summary>
    /// Aucun crédit à l'ouverture : les SMS s'achètent. Un solde initial offert serait une décision
    /// commerciale, pas une valeur par défaut technique — le Super Admin crédite explicitement.
    /// </summary>
    public const int SmsCreditBalance = 0;

    /// <summary>
    /// Alertes SMS désactivées tant que le Directeur ne les a pas explicitement activées — même
    /// principe que les délégations ci-dessus. Écrire aux parents de toute une école ne doit jamais
    /// être la conséquence silencieuse d'une montée de version.
    /// </summary>
    public const bool SmsAlertsEnabled = false;

    /// <summary>Une semaine de retard avant qu'un débiteur n'entre dans un lot de relance brouillon.</summary>
    public const int DebtorReminderThresholdDays = 7;

    /// <summary>Bornes du seuil de retard : au moins 1 jour, au plus une année scolaire complète.</summary>
    public const int MinDebtorReminderThresholdDays = 1;
    public const int MaxDebtorReminderThresholdDays = 365;

    /// <summary>
    /// Modules activés à la création d'une école : Pédagogie et Finance forment le socle métier
    /// existant, elles restent actives tant que le Directeur ne les désactive pas explicitement.
    /// </summary>
    public const bool IsPedagogyEnabled = true;

    public const bool IsFinanceEnabled = true;

    /// <summary>
    /// Internat et filière Coranique/Franco-Arabe : aucun écran ni route ne dépend encore de ces deux
    /// réglages (Internat/Coran n'existent pas dans le produit). Désactivés par défaut — les activer
    /// aujourd'hui n'ouvrirait rien, c'est un réglage anticipé pour le jour où ces modules existeront.
    /// </summary>
    public const bool IsInternatEnabled = false;

    public const bool IsCoranModuleEnabled = false;
}
