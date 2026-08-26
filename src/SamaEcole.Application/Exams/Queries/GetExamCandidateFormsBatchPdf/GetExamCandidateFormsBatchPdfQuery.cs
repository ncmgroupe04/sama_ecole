using MediatR;

namespace SamaEcole.Application.Exams.Queries.GetExamCandidateFormsBatchPdf;

/// <summary>
/// POST /api/v1/exams/dossiers/candidate-forms/pdf — impression par lot. Ne retient que les dossiers
/// `Complet`, `Transmis` ou `Valide` : un dossier `Incomplet` n'a rien à faire dans un lot destiné à
/// être signé et transmis (Volume 1 §22.5). Au moins un des deux filtres est requis.
/// </summary>
public record GetExamCandidateFormsBatchPdfQuery : IRequest<byte[]>
{
    public Guid? ExamSessionId { get; init; }
    public Guid? ClassroomId { get; init; }
}
