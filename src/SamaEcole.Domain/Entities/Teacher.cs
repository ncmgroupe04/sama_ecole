using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Enseignant d'un établissement (ticket JGK-D03). Même règle de matricule que
/// <see cref="Student"/> : généré par le Handler, dans la transaction d'enregistrement,
/// jamais à l'ouverture du formulaire (AGENTS.md règle #3).
///
/// Dossier RH distinct du compte de connexion `User` (rôle <c>Enseignant</c>) : aucun lien n'existe
/// aujourd'hui entre les deux, un enseignant peut avoir une fiche sans jamais se connecter à la
/// plateforme (docs/Volume_7_Security.md « Enseignants » : gérée par Directeur/Secrétariat).
/// </summary>
public class Teacher : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public required string Matricule { get; set; }
    public required string FullName { get; set; }
    public required string Email { get; set; }
    public string? Phone { get; set; }

    /// <summary>Obligatoire à la création (formulaire Enseignant) — voir <see cref="Student.BirthDate"/>.</summary>
    public required DateOnly BirthDate { get; set; }

    /// <summary>
    /// « M » / « F », même convention que <see cref="Student.Gender"/> — mais NULLABLE là où celui de
    /// l'élève est obligatoire, et cette différence est délibérée : des milliers de fiches enseignant
    /// existent déjà sans ce champ, et le rendre obligatoire empêcherait de les rouvrir pour les
    /// modifier.
    ///
    /// Ajouté pour le rapport annuel STATEDUC (Volume 1 §23.3), dont TOUS les tableaux de personnel
    /// sont ventilés Hommes/Femmes. Sans cette colonne, l'agrégat n'avait que deux issues : compter
    /// tout l'effectif dans une seule case (faux, et invisible au relecteur), ou déduire le genre du
    /// prénom (faux pour une part importante des prénoms sénégalais, et faux en silence). Un champ
    /// nullable permet la troisième issue, la seule honnête : une colonne « non renseigné » que
    /// l'école voit et peut corriger.
    /// </summary>
    public string? Gender { get; set; }

    public string? BirthPlace { get; set; }

    /// <summary>Adresse de résidence (facultative) — même contrat que <see cref="Student.Address"/>.</summary>
    public string? Address { get; set; }

    /// <summary>URL de la photo d'identité — même contrat que <see cref="Student.PhotoUrl"/>.</summary>
    public string? PhotoUrl { get; set; }

    /// <summary>Photo téléversée (feature B) — même contrat que <see cref="Student.PhotoData"/>.</summary>
    public byte[]? PhotoData { get; set; }

    public EntityStatus Status { get; set; } = EntityStatus.Active;

    // ------------------------------------------------- Qualifications (rapport annuel STATEDUC §23.3)

    /// <summary>
    /// Diplôme académique (BFEM… Doctorat). <c>NonRenseigne</c> par défaut — l'immense majorité des
    /// fiches existantes n'a jamais eu ce champ, et les compter d'office « sans diplôme » fausserait
    /// le premier rapport STATEDUC produit par l'école.
    /// </summary>
    public AcademicQualification AcademicQualification { get; set; } = AcademicQualification.NonRenseigne;

    /// <summary>
    /// Diplôme professionnel (CEAP, CAP, CAEM, CAES). C'est CE champ, et non le diplôme académique,
    /// qui détermine le « taux d'enseignants qualifiés » du rapport STATEDUC.
    /// </summary>
    public ProfessionalQualification ProfessionalQualification { get; set; } = ProfessionalQualification.NonRenseigne;

    /// <summary>Statut administratif (Fonctionnaire, Contractuel…) — colonne obligatoire du STATEDUC.</summary>
    public TeacherCivilServiceStatus CivilServiceStatus { get; set; } = TeacherCivilServiceStatus.NonRenseigne;

    /// <summary>
    /// Matricule de solde de la Fonction publique, pour les seuls personnels payés par l'État. Null
    /// pour un vacataire de l'école — et cette absence est une information, pas un oubli : elle
    /// distingue un contractuel de l'État d'un contractuel de l'établissement.
    /// </summary>
    public string? CivilServiceMatricule { get; set; }

    /// <summary>
    /// Date de première prise de service dans l'enseignement (toutes écoles confondues), et non dans
    /// CET établissement : le STATEDUC compte l'ancienneté dans le métier. Null si non renseignée —
    /// l'ancienneté s'imprime alors vide, jamais déduite de la date de création de la fiche.
    /// </summary>
    public DateOnly? FirstAppointmentDate { get; set; }

    /// <summary>
    /// Compte de connexion (rôle Enseignant) rattaché à cette fiche RH (ticket JGK-D06). Nullable : une
    /// fiche peut exister sans compte (enseignant qui n'utilise pas la plateforme). C'est ce lien qui
    /// permet de savoir QUEL enseignant est connecté pour borner la saisie de l'appel à ses classes et
    /// matières assignées — sans lui, le JWT ne porte que l'identifiant du compte, pas la fiche.
    /// </summary>
    public Guid? UserId { get; set; }
}
