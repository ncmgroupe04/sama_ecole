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

    public string? BirthCertificateNumber { get; set; }
    public bool BirthCertificatePresent { get; set; }

    /// <summary>Nul = non encore contrôlé, distinct de <c>false</c> (Volume 1 §22.2).</summary>
    public bool? CivilStatusConforming { get; set; }

    public string? CivilStatusNotes { get; set; }

    public ExamDossierStatus Status { get; set; } = ExamDossierStatus.Incomplet;

    public DateOnly? TransmittedOn { get; set; }

    public ExamResult? Result { get; set; }
}
