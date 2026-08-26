using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Exams.Queries.GetExamDossiers;

/// <summary>
/// GET /api/v1/exams/dossiers — dossiers de candidature, paginés. Pagination selon la convention
/// transverse (docs/Volume_4_API_Design.md §0.2 : page/pageSize, 20 par défaut, 100 max).
/// </summary>
public record GetExamDossiersQuery : IRequest<PaginatedExamDossiers>
{
    public Guid? ExamSessionId { get; init; }
    public Guid? ClassroomId { get; init; }
    public ExamDossierStatus? Status { get; init; }
    public string? Search { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public record PaginatedExamDossiers(
    IReadOnlyList<ExamDossierListItem> Items,
    int TotalCount,
    int Page,
    int PageSize);

public record ExamDossierListItem(
    Guid Id,
    Guid ExamSessionId,
    Guid StudentId,
    string StudentFullName,
    Guid ClassroomId,
    string ClassroomName,
    string? CandidateNumber,
    string? ExamCenterName,
    bool BirthCertificatePresent,
    bool? CivilStatusConforming,
    string Status,
    uint RowVersion);
