using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Établissement scolaire = unité d'isolation multi-tenant. Cette entité n'implémente PAS
/// ITenantEntity : elle définit le tenant, elle ne lui appartient pas.
/// Voir docs/Volume_3_DDS.md §Multi-tenant (cas particulier) et docs/ERD.md.
/// </summary>
public class School : AuditableEntity
{
    public required string Name { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? LogoUrl { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;

    /// <summary>
    /// Bascule « bac à sable → exploitation réelle ». <c>null</c> = mode TEST : le Directeur peut
    /// réinitialiser (purger) autant de fois qu'il veut pour refaire des essais. DATÉ = mode RÉEL :
    /// horodatage du passage EXPLICITE, la purge devient indisponible (les données enregistrées font
    /// partie de la comptabilité — invariant d'immuabilité, AGENTS.md règle #6).
    ///
    /// Jamais posé par un effet de bord (première clôture, première inscription…) : c'est une action
    /// délibérée du Directeur. <c>RevertToTestCommand</c> — disponible à tout moment, pour que le
    /// Directeur garde le contrôle de son environnement — remet ce champ à <c>null</c> pour rejouer la
    /// bascule. Ce n'est PAS ce champ, mais <see cref="HasEverGoneLive"/>, qui verrouille la purge :
    /// sans cette distinction, un retour en mode test rouvrirait la « Zone de danger » sur des données
    /// réelles.
    /// </summary>
    public DateTimeOffset? WentLiveAt { get; set; }

    /// <summary>
    /// Verrou PERMANENT : posé une seule fois, au premier passage en mode réel
    /// (<c>GoLiveCommandHandler</c>), et plus jamais effacé — y compris par
    /// <c>RevertToTestCommand</c>. C'est ce champ, et non <see cref="WentLiveAt"/> (qui redevient
    /// <c>null</c> après un retour en mode test), que <c>ResetSchoolDataCommandHandler</c> ET la
    /// fonction PostgreSQL <c>reset_school_data</c> interrogent pour interdire la purge : une fois
    /// qu'une école a enregistré des données réelles, elles restent inaltérables pour toujours, quel
    /// que soit le régime d'affichage ultérieur (AGENTS.md règle #6).
    /// </summary>
    public bool HasEverGoneLive { get; set; }

    /// <summary>Adresse e-mail de contact de l'établissement, imprimée dans l'en-tête du reçu.</summary>
    public string? Email { get; set; }

    /// <summary>
    /// Numéro d'Identification Nationale des Entreprises et Associations. Mention légale sénégalaise
    /// portée par l'en-tête du reçu : c'est ce qui distingue une pièce comptable opposable d'un simple
    /// justificatif interne. Null tant que le Directeur ne l'a pas saisi — la ligne s'imprime alors
    /// sans cette mention, jamais avec un numéro inventé.
    /// </summary>
    public string? Ninea { get; set; }

    /// <summary>Registre du Commerce et du Crédit Mobilier (RCCM) — même usage que <see cref="Ninea"/>.</summary>
    public string? RegistreCommerce { get; set; }

    /// <summary>
    /// Inspection d'Académie de rattachement — ligne « IA : … » de l'en-tête du bulletin
    /// (docs/design-references/bulletin-reference.png, ex. « Thies »). Null tant que le Directeur ne
    /// l'a pas renseignée dans Paramètres → Établissement : la ligne s'imprime alors vide, jamais
    /// une valeur inventée.
    /// </summary>
    public string? InspectionAcademie { get; set; }

    /// <summary>Inspection de l'Éducation et de la Formation — ligne « IEF : … » du bulletin (ex. « Mbour 1 »).</summary>
    public string? InspectionEducationFormation { get; set; }

    /// <summary>
    /// Nom porté par la troisième ligne de l'en-tête du bulletin (ex. « Popenguine »). SANS son préfixe
    /// de cycle : celui-ci (« ÉCOLE ÉLÉMENTAIRE DE » / « COLLÈGE DE » / « LYCÉE DE ») est ajouté à
    /// l'impression selon le cycle de la CLASSE de l'élève — un même établissement édite des bulletins
    /// de CM2 comme de Terminale. Un préfixe malgré tout saisi ici est retiré par
    /// <c>SchoolHeading.StripCyclePrefix</c>, sans quoi un bulletin de 6e afficherait
    /// « COLLÈGE DE : LYCÉE DE POPENGUINE ».
    ///
    /// Distinct de <see cref="Name"/> : la raison sociale complète (« Complexe Privé… », affichée sur le
    /// reçu) n'a pas sa place sur le bulletin. Tant que ce champ est vide, la ligne s'imprime réduite à
    /// son préfixe — AUCUN repli sur Name, même partiel (voir ReportCardDocument.ComposeHeader).
    /// </summary>
    public string? NomLycee { get; set; }

    // ------------------------------------------------------- Identification réglementaire (SIMEN)

    /// <summary>
    /// Code établissement NATIONAL attribué par le SIMEN — la clé sous laquelle le ministère connaît
    /// l'école dans Planète et STATEDUC. C'est ce code, jamais l'identifiant technique <c>Id</c>, qui
    /// figure en tête de tout fichier transmis à l'IEF ou à l'IA (Volume 1 §23.1).
    ///
    /// Null tant que le Directeur ne l'a pas saisi : l'export d'intégration étatique REFUSE alors de
    /// s'exécuter (erreur explicite, jamais un fichier au code vide qui serait rejeté en silence à
    /// l'autre bout). C'est aussi le préfixe de l'IEN provisoire — voir <c>NationalIenGenerator</c>.
    /// </summary>
    public string? NationalSchoolCode { get; set; }

    /// <summary>
    /// Numéro de l'arrêté ministériel d'ouverture / d'autorisation d'exercer. Mention légale exigée sur
    /// les pièces officielles des établissements privés et reportée sur le formulaire STATEDUC. Null
    /// s'imprime en ligne omise, jamais un numéro inventé — même convention que <see cref="Ninea"/>.
    /// </summary>
    public string? MinistryAuthorizationNumber { get; set; }

    /// <summary>
    /// Code de la circonscription scolaire (carte scolaire) dont dépend l'établissement. Sert à
    /// l'agrégation territoriale du ministère : deux écoles d'une même IEF peuvent relever de deux
    /// districts distincts, ce que <see cref="InspectionEducationFormation"/> ne sait pas exprimer.
    /// </summary>
    public string? SchoolDistrictCode { get; set; }

    /// <summary>
    /// Latitude WGS84 de l'établissement (carte scolaire). En <c>decimal</c>, PAS dans une chaîne
    /// « lat,lon » unique : une coordonnée stockée en texte ne peut être ni validée à la saisie, ni
    /// bornée, ni utilisée dans une requête géographique — et les fichiers de carte scolaire réels
    /// arrivent tantôt en « 14.6928, -17.4467 », tantôt en « 14°41'34"N ». Deux colonnes numériques
    /// tranchent la question à l'entrée plutôt qu'à chaque lecture.
    ///
    /// Les DEUX coordonnées sont renseignées ensemble ou pas du tout — une latitude sans longitude ne
    /// localise rien. <see cref="GpsCoordinates"/> ne rend la chaîne d'affichage que dans ce cas.
    /// </summary>
    public decimal? GpsLatitude { get; set; }

    /// <summary>Longitude WGS84 — voir <see cref="GpsLatitude"/>, dont elle est indissociable.</summary>
    public decimal? GpsLongitude { get; set; }

    /// <summary>
    /// Coordonnées GPS au format attendu par les fichiers de carte scolaire (« 14.692800, -17.446700 »),
    /// calculées — jamais stockées, pour qu'aucune dérive ne soit possible entre la chaîne et le couple
    /// de décimaux qui fait foi. Null si l'une des deux coordonnées manque.
    ///
    /// Culture INVARIANTE, imposée : en « fr-FR », le séparateur décimal serait la virgule et
    /// « 14,6928, -17,4467 » deviendrait illisible pour l'importeur du ministère comme pour un CSV.
    /// </summary>
    public string? GpsCoordinates => GpsLatitude is { } lat && GpsLongitude is { } lon
        ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{lat:F6}, {lon:F6}")
        : null;

    // ------------------------------------------------------------------ Annuaire public (B2C)

    /// <summary>
    /// Consentement du Directeur à figurer dans l'annuaire PUBLIC des établissements.
    ///
    /// FAUX par défaut, et ce défaut est une décision, pas une commodité : publier le nom, la ville et
    /// les coordonnées d'un établissement est une diffusion vers des tiers, qui n'a pas à découler
    /// implicitement d'une inscription à un logiciel de gestion. Seul le Directeur bascule ce drapeau,
    /// depuis Paramètres → Établissement.
    ///
    /// Une école retirée de l'annuaire (repassée à faux) disparaît immédiatement des réponses publiques :
    /// le filtre est appliqué à CHAQUE requête, jamais mis en cache côté serveur.
    /// </summary>
    public bool IsPubliclyListed { get; set; }

    /// <summary>
    /// Ville, en donnée STRUCTURÉE — contrairement à <see cref="Address"/> qui reste du texte libre
    /// destiné à l'impression. C'est le principal critère de recherche d'un parent dans l'annuaire, et
    /// filtrer par sous-chaîne sur une adresse libre donnerait des résultats faux (« Rue de Dakar » à
    /// Thiès). Alimentée à l'approbation depuis la demande d'inscription, qui la saisit déjà.
    /// </summary>
    public string? City { get; set; }

    /// <summary>Région administrative (Dakar, Thiès, Saint-Louis…) — même usage que <see cref="City"/>.</summary>
    public string? Region { get; set; }

    /// <summary>
    /// Présentation rédigée par le Directeur pour l'annuaire public. N'apparaît sur AUCUN document
    /// officiel (reçu, bulletin, attestation) : c'est un texte de vitrine, pas une mention légale.
    /// Null tant qu'il n'a rien saisi — la fiche s'affiche alors sans paragraphe de présentation,
    /// jamais avec un texte inventé à sa place.
    /// </summary>
    public string? PublicDescription { get; set; }
}
