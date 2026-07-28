using MediatR;

namespace SamaEcole.Application.ReportCards.Queries.GetReportCardRemark;

/// <summary>
/// GET /api/v1/report-cards/remark?studentId=&amp;termId= — préremplit l'écran « Observations du
/// conseil » avant modification. Mêmes rôles que la génération du bulletin (Directeur, Enseignant) :
/// c'est la même préparation du même document.
/// </summary>
public record GetReportCardRemarkQuery(Guid StudentId, Guid TermId) : IRequest<ReportCardRemarkDto>;
