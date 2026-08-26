using MediatR;

namespace SamaEcole.Application.Exams.Queries.GetExamDossierAudit;

/// <summary>
/// GET /api/v1/exams/dossiers/audit — dossiers `Incomplet` d'une session, avec le détail de ce qui
/// manque. C'est ce relevé qui autorise (ou non) une transmission ou une impression par lot (Volume 1
/// §22.3) — pas une case cochée manuellement.
/// </summary>
public record GetExamDossierAuditQuery(Guid ExamSessionId) : IRequest<IReadOnlyList<ExamDossierAuditEntry>>;

public record ExamDossierAuditEntry(
    Guid DossierId,
    string StudentFullName,
    string ClassroomName,
    IReadOnlyList<string> MissingItems);
