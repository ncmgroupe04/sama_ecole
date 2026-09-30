using MediatR;

namespace SamaEcole.Application.Exams.Queries.GetCfeeCandidates;

/// <summary>
/// GET /api/v1/exams/cfee-candidates — Ticket Onboarding #6 (profil Élémentaire). Cohorte des élèves
/// actuellement en CM2 pour l'année scolaire ACTIVE, avec leur dossier CFEE s'il en existe déjà un.
///
/// Réservée à Directeur/Secrétariat (ManageRoles, ExamsController) — même portée que les onglets
/// Sessions/Audit/Statistiques : c'est un tableau de PRÉPARATION de la campagne CFEE, pas la lecture
/// bornée par classe déjà ouverte à l'Enseignant sur GET /exams/dossiers (JGK-J08).
/// </summary>
public record GetCfeeCandidatesQuery : IRequest<CfeeCandidatesResult>;

/// <summary>
/// <see cref="CfeeExamSessionId"/> est NULL tant qu'aucune session CFEE n'a été ouverte pour l'année
/// active (onglet Sessions) — le front s'en sert pour proposer « Ouvrir un dossier » ou l'export
/// ministériel seulement une fois non-null, plutôt que de laisser échouer l'appel en 422.
/// </summary>
public record CfeeCandidatesResult(
    string? SchoolYearLabel,
    Guid? CfeeExamSessionId,
    IReadOnlyList<CfeeCandidate> Candidates);

/// <summary>
/// <see cref="HasDossier"/> distingue un candidat qui n'a simplement pas encore de dossier ouvert
/// (action « Ouvrir un dossier ») d'un dossier réellement vide — les champs de checklist valent alors
/// leurs défauts (false) plutôt que d'être rendus nullable, ce qui aurait fait porter à l'appelant la
/// distinction « pas de dossier » vs « dossier sans rien coché », déjà tranchée par ce booléen.
/// </summary>
public record CfeeCandidate(
    Guid StudentId,
    string StudentFullName,
    string Matricule,
    Guid ClassroomId,
    string ClassroomName,
    bool HasDossier,
    Guid? DossierId,
    string? Status,
    bool BirthCertificatePresent,
    bool? CivilStatusConforming,
    bool PhotoPresent,
    bool FeeReceiptPresent,
    bool? IsAdmitted,
    uint? RowVersion);
