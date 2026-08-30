using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Dossier de candidature d'un élève à une <see cref="ExamSession"/> (Volume 1 §22, Volume 3 DDS §5.10).
/// Un élève n'a qu'un seul dossier par session (contrainte UNIQUE posée par la migration).
/// </summary>
public class ExamDossier : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid ExamSessionId { get; set; }
    public ExamSession ExamSession { get; set; } = null!;

    public Guid StudentId { get; set; }
    public Student Student { get; set; } = null!;

    /// <summary>
    /// Classe au moment de l'ouverture du dossier, FIGÉE : un transfert de classe ultérieur de
    /// l'élève ne réécrit jamais un dossier déjà ouvert (Volume 1 §22.1). Aucun Handler de mise à
    /// jour ne doit exposer ce champ en écriture après création.
    /// </summary>
    public Guid ClassroomId { get; set; }
    public Classroom Classroom { get; set; } = null!;

    /// <summary>
    /// Numéro de table. Nul tant que <c>AssignExamCenterCommand</c> n'a pas été exécuté — généré
    /// dans CETTE transaction, jamais à l'ouverture du dossier (AGENTS.md règle #3, même contrat
    /// que <see cref="Student.Matricule"/>).
    /// </summary>
    public string? CandidateNumber { get; set; }

    /// <summary>Hérite de <see cref="ExamSession.CenterName"/> si non renseigné.</summary>
    public string? ExamCenterName { get; set; }

    /// <summary>
    /// CODE officiel du centre d'examen attribué par l'IA (Volume 1 §23.4), distinct de
    /// <see cref="ExamCenterName"/> qui n'est qu'un libellé humain. C'est le code — jamais le nom — que
    /// le ministère utilise pour rapprocher les candidats d'un centre : deux centres peuvent porter des
    /// noms voisins (« Lycée de Mbour », « Lycée de Mbour 2 ») et un nom mal orthographié fait rejeter
    /// tout le lot. Null tant que l'IA ne l'a pas communiqué.
    /// </summary>
    public string? ExamCenterCode { get; set; }

    /// <summary>
    /// Numéro de TABLE (place physique) du candidat dans la salle d'examen, communiqué par le centre
    /// quelques jours avant les épreuves. À ne pas confondre avec <see cref="CandidateNumber"/>, qui
    /// est le numéro d'INSCRIPTION : le premier situe l'élève dans une salle, le second l'identifie
    /// dans le fichier national — un candidat garde son numéro d'inscription et change de table entre
    /// deux épreuves.
    ///
    /// Écrit uniquement par la commande d'affectation de centre, jamais généré par nous : ce numéro
    /// appartient au centre d'examen.
    /// </summary>
    public string? TableNumber { get; set; }

    public string? BirthCertificateNumber { get; set; }
    public bool BirthCertificatePresent { get; set; }

    /// <summary>Nul = non encore contrôlé, distinct de <c>false</c> (Volume 1 §22.2).</summary>
    public bool? CivilStatusConforming { get; set; }

    /// <summary>
    /// État détaillé de la pièce d'état civil (Volume 1 §23.4). COMPLÈTE
    /// <see cref="BirthCertificatePresent"/> et <see cref="CivilStatusConforming"/> sans les remplacer :
    /// ces deux champs restent écrits et lus par les Handlers et l'audit de dossier existants, et les
    /// réécrire aurait cassé <c>GetExamDossierAuditQuery</c>.
    ///
    /// Ce que le couple booléen ne savait pas dire, et qui est le cas le plus fréquent au Sénégal :
    /// « fourni, non conforme, jugement supplétif en cours ». Sans cet état, un dossier en
    /// régularisation était indiscernable d'un dossier définitivement non conforme — et l'IEF refusait
    /// les deux.
    /// </summary>
    public CivilRegistryDocumentStatus CivilRegistryDocumentStatus { get; set; } = CivilRegistryDocumentStatus.NonFourni;

    public string? CivilStatusNotes { get; set; }

    public ExamDossierStatus Status { get; set; } = ExamDossierStatus.Incomplet;

    public DateOnly? TransmittedOn { get; set; }

    public ExamResult? Result { get; set; }
}
