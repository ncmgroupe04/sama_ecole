using MediatR;

namespace SamaEcole.Application.Exams.Commands.CreateExamDossier;

/// <summary>
/// POST /api/v1/exams/dossiers — ouvre un dossier de candidature. <see cref="ClassroomId"/> est
/// FIGÉ à la création (Volume 1 §22.1) : aucun autre Handler de ce module ne l'expose en écriture.
/// </summary>
public record CreateExamDossierCommand : IRequest<ExamDossierResult>
{
    public required Guid ExamSessionId { get; init; }
    public required Guid StudentId { get; init; }
    public required Guid ClassroomId { get; init; }
}

public record ExamDossierResult(
    Guid Id,
    Guid ExamSessionId,
    Guid StudentId,
    Guid ClassroomId,
    string? CandidateNumber,
    string? ExamCenterName,
    bool BirthCertificatePresent,
    bool? CivilStatusConforming,
    string Status,
    uint RowVersion);
