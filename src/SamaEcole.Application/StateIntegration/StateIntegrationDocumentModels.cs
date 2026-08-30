using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.StateIntegration;

/// <summary>
/// Tout ce qu'imprime le certificat de mutation, déjà résolu — le générateur PDF ne fait que mettre en
/// page (même contrat que <c>ReportCardDto</c>). Volume 1 §23.5, ticket JGK-M06.
/// </summary>
public record StudentMutationCertificateModel(
    // ---- Établissement émetteur ----------------------------------------------------------------
    string SchoolName,
    string? NationalSchoolCode,
    string? MinistryAuthorizationNumber,
    string? InspectionAcademie,
    string? InspectionEducationFormation,
    string? Address,
    string? Phone,
    string? Email,
    string? City,

    // ---- Pièce -----------------------------------------------------------------------------------
    string CertificateNumber,
    DateOnly IssuedOn,

    // Contenu encodé dans le QR : une URL de VÉRIFICATION, jamais les données de l'élève. Un QR qui
    // porterait l'état civil resterait lisible par quiconque photographie le papier, et le rendrait
    // exploitable hors de tout contrôle d'accès.
    string VerificationUrl,

    // ---- Élève -----------------------------------------------------------------------------------
    string StudentFullName,
    string Matricule,

    // IEN : imprimé s'il existe, avec la mention « (provisoire) » le cas échéant. C'est la donnée que
    // l'école d'accueil recopiera — lui laisser croire qu'un numéro fabriqué est officiel lui ferait
    // transmettre un faux au ministère à son tour.
    string? IenNumber,
    bool IsIenProvisional,

    DateOnly BirthDate,
    string BirthPlace,
    string Gender,
    string ClassroomName,
    string SchoolYearLabel,

    // ---- Mutation --------------------------------------------------------------------------------
    StudentMutationReason Reason,
    string? ReasonDetails,
    string? DestinationSchoolName,
    string? DestinationCity,

    // Situation financière AU JOUR DE LA DÉLIVRANCE (instantané figé — voir
    // StudentMutationCertificate.WasFinanciallyClear). Ne bloque jamais la délivrance.
    bool WasFinanciallyClear,

    byte[]? DirectorSignature = null,
    byte[]? OfficialStamp = null)
{
    /// <summary>Libellé français du motif, pour l'impression.</summary>
    public string ReasonLabel => Reason switch
    {
        StudentMutationReason.Demenagement => "Déménagement",
        StudentMutationReason.ChangementEtablissement => "Changement d'établissement",
        StudentMutationReason.RaisonFamiliale => "Raison familiale",
        StudentMutationReason.RaisonMedicale => "Raison médicale",
        _ => ReasonDetails is { Length: > 0 } details ? details : "Autre"
    };
}

/// <summary>
/// Livret de compétences (Volume 1 §23.6, ticket JGK-M07) — le document APC qui suit l'élève : pour
/// chaque domaine et chaque compétence de la grille de son niveau, le niveau d'acquisition atteint,
/// trimestre par trimestre.
///
/// Il ne remplace pas le bulletin : le bulletin note une période, le livret retrace un parcours. C'est
/// aussi la pièce que réclame l'école d'accueil lors d'une mutation, aux côtés du certificat.
///
/// La grille vient de <c>EvaluationStructureDto</c> — la MÊME configuration d'école que le bulletin
/// APC, jamais une seconde définition des compétences qui divergerait au premier changement.
/// </summary>
public record SkillsBookletModel(
    string SchoolName,
    string? InspectionAcademie,
    string? InspectionEducationFormation,
    string HeadingPrefix,
    string? HeadingName,

    string StudentFullName,
    string Matricule,
    string? IenNumber,
    DateOnly BirthDate,
    string BirthPlace,
    string ClassroomName,
    string Level,
    string SchoolYearLabel,

    // Les trimestres de l'année, dans l'ordre — les colonnes du livret. Leur NOMBRE est variable
    // (2 semestres ou 3 trimestres selon l'école) : rien n'est codé en dur, le tableau s'adapte.
    IReadOnlyList<string> TermLabels,

    IReadOnlyList<SkillsBookletDomain> Domains,

    // Appréciation globale de fin d'année, saisie par le conseil. Null tant que rien n'est écrit :
    // le cadre s'imprime vide, jamais rempli d'une phrase générée.
    string? OverallAppreciation,

    byte[]? DirectorSignature = null,
    byte[]? OfficialStamp = null);

/// <summary>Un domaine de la grille APC et ses compétences.</summary>
public record SkillsBookletDomain(string Name, IReadOnlyList<SkillsBookletCompetency> Competencies);

/// <summary>
/// Une compétence et son niveau d'acquisition par trimestre. <c>Levels</c> a exactement autant
/// d'entrées que <c>SkillsBookletModel.TermLabels</c> — une entrée null signifie « non évaluée sur
/// cette période », et s'imprime en case vide. Un « non acquis » à la place serait un jugement que
/// personne n'a porté.
/// </summary>
public record SkillsBookletCompetency(string Label, IReadOnlyList<SkillAcquisitionLevel?> Levels);

/// <summary>
/// Échelle d'acquisition de l'approche par compétences, dans l'ordre croissant. Dérivée du
/// POURCENTAGE de réussite (note / barème), seuils portés par <c>SkillAcquisition</c> — jamais de
/// seuils recopiés à la main dans le générateur PDF.
/// </summary>
public enum SkillAcquisitionLevel
{
    NonAcquis,
    EnCoursAcquisition,
    Acquis,
    Expert
}

/// <summary>
/// Traduit un pourcentage de réussite en niveau d'acquisition. Une SEULE définition des seuils, ici —
/// le livret, l'écran et tout export futur la partagent.
///
/// Seuils : &lt; 40 % non acquis, &lt; 60 % en cours, &lt; 85 % acquis, au-delà expert. Ils viennent des
/// grilles APC en usage au primaire sénégalais ; une école qui voudrait les siens aura besoin d'un
/// réglage d'établissement — rien ne porte cette information aujourd'hui, et les coder en dur ailleurs
/// qu'ici rendrait ce futur réglage impossible à câbler.
/// </summary>
public static class SkillAcquisition
{
    public static SkillAcquisitionLevel? FromScore(decimal? score, decimal maxScore)
    {
        // Pas de note, ou un barème nul (grille mal configurée) : rien à conclure. Surtout pas
        // « non acquis », qui affirmerait un échec que personne n'a constaté.
        if (score is not { } value || maxScore <= 0)
        {
            return null;
        }

        var percentage = value * 100m / maxScore;

        return percentage switch
        {
            < 40m => SkillAcquisitionLevel.NonAcquis,
            < 60m => SkillAcquisitionLevel.EnCoursAcquisition,
            < 85m => SkillAcquisitionLevel.Acquis,
            _ => SkillAcquisitionLevel.Expert
        };
    }

    /// <summary>Abréviation imprimée dans les cases étroites du livret (« NA », « ECA », « A », « E »).</summary>
    public static string Abbreviate(SkillAcquisitionLevel? level) => level switch
    {
        SkillAcquisitionLevel.NonAcquis => "NA",
        SkillAcquisitionLevel.EnCoursAcquisition => "ECA",
        SkillAcquisitionLevel.Acquis => "A",
        SkillAcquisitionLevel.Expert => "E",
        _ => ""
    };

    /// <summary>Libellé complet, pour la légende du document.</summary>
    public static string Describe(SkillAcquisitionLevel level) => level switch
    {
        SkillAcquisitionLevel.NonAcquis => "Non acquis",
        SkillAcquisitionLevel.EnCoursAcquisition => "En cours d'acquisition",
        SkillAcquisitionLevel.Acquis => "Acquis",
        _ => "Expert"
    };
}
