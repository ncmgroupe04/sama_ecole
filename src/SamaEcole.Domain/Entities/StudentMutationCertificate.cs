using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Certificat de mutation (« certificat de transfert ») délivré à un élève qui quitte l'établissement
/// pour un autre — la pièce que l'école d'accueil exige avant toute réinscription, et sans laquelle
/// l'IEF refuse le transfert du dossier (Volume 1 §23.5).
///
/// POURQUOI UNE ENTITÉ, ET NON UN SIMPLE PDF GÉNÉRÉ À LA VOLÉE comme l'attestation d'inscription : le
/// certificat porte un QR code de vérification. Un QR qui n'est adossé à rien n'est qu'un ornement —
/// il faut une ligne en base pour que l'école d'accueil, en le scannant, obtienne une réponse. C'est
/// aussi la seule façon de répondre à « ce certificat a-t-il été révoqué ? ». Un document régénéré à
/// chaque appel ne peut répondre ni à l'une ni à l'autre question.
///
/// APPEND-ONLY DE FAIT : un certificat délivré n'est jamais modifié — une erreur se corrige en le
/// RÉVOQUANT (<see cref="RevokedAt"/>) et en en délivrant un nouveau. Réécrire un certificat déjà
/// remis au tuteur produirait deux pièces contradictoires portant le même numéro, dont la version
/// papier ferait foi contre nous.
/// </summary>
public class StudentMutationCertificate : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid StudentId { get; set; }
    public Student Student { get; set; } = null!;

    /// <summary>
    /// Année scolaire au titre de laquelle la mutation est prononcée. Figée à la délivrance : un
    /// certificat émis en juin 2027 reste rattaché à 2026-2027 même consulté deux ans plus tard.
    /// </summary>
    public Guid SchoolYearId { get; set; }
    public SchoolYear SchoolYear { get; set; } = null!;

    /// <summary>
    /// Numéro officiel séquentiel par établissement (« MUT-2026-0007 »). Généré DANS la transaction de
    /// délivrance, jamais à l'ouverture du formulaire — AGENTS.md règle #3, même contrat que
    /// <see cref="Student.Matricule"/> et que le numéro de reçu.
    /// </summary>
    public required string CertificateNumber { get; set; }

    /// <summary>
    /// Secret de vérification encodé dans le QR code — 32 caractères d'aléa cryptographique, PAS
    /// l'identifiant de la ligne. Exposer <c>Id</c> dans un QR public rendrait toute la table
    /// énumérable : un tiers essaierait des GUID jusqu'à découvrir les mutations d'autres élèves.
    /// L'aléa rend l'énumération sans objet, et le point de vérification ne répond que par
    /// « valide / révoqué / inconnu » — jamais par les données de l'élève.
    /// </summary>
    public required string VerificationCode { get; set; }

    /// <summary>
    /// Établissement de destination, en TEXTE LIBRE : l'école d'accueil est presque toujours hors de la
    /// plateforme, et l'y référencer par une clé étrangère rendrait le certificat impossible à délivrer
    /// dans le cas le plus courant. Null quand le tuteur n'a pas encore choisi — le certificat vaut
    /// alors décharge sans destination nommée, ce qui est admis.
    /// </summary>
    public string? DestinationSchoolName { get; set; }

    /// <summary>Ville / localité de destination, saisie libre pour la même raison.</summary>
    public string? DestinationCity { get; set; }

    public StudentMutationReason Reason { get; set; } = StudentMutationReason.Autre;

    /// <summary>Précision libre, obligatoire côté validateur quand <see cref="Reason"/> vaut <c>Autre</c>.</summary>
    public string? ReasonDetails { get; set; }

    /// <summary>
    /// Classe quittée, FIGÉE à la délivrance — comme <see cref="ExamDossier.ClassroomId"/>. Un
    /// changement de classe ultérieur (ou la suppression de la classe) ne doit jamais réécrire un
    /// certificat déjà remis.
    /// </summary>
    public required string ClassroomNameSnapshot { get; set; }

    /// <summary>Date de délivrance imprimée sur la pièce.</summary>
    public DateOnly IssuedOn { get; set; }

    /// <summary>
    /// Vrai quand l'élève était à jour de ses frais au moment de la délivrance. INSTANTANÉ, pas un
    /// calcul refait à la lecture : le certificat atteste de la situation au jour de son émission, et
    /// recalculer le solde deux mois plus tard changerait le sens d'une pièce déjà signée.
    ///
    /// N'EMPÊCHE PAS la délivrance quand il est faux : refuser un certificat de mutation à un élève
    /// débiteur revient à le retenir de force dans l'établissement — ce que la réglementation interdit.
    /// La mention s'imprime, le règlement se poursuit par les voies de recouvrement ordinaires.
    /// </summary>
    public bool WasFinanciallyClear { get; set; }

    /// <summary>
    /// Révocation (erreur de saisie, mutation annulée). Non nul = le point de vérification répond
    /// « révoqué » à tout scan du QR. Aucune suppression physique — AGENTS.md règle #6.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Motif de révocation, exigé par le Handler dès qu'une révocation est demandée.</summary>
    public string? RevocationReason { get; set; }
}
