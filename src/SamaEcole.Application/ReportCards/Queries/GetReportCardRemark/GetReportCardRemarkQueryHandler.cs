using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.ReportCards.Queries.GetReportCardRemark;

public class GetReportCardRemarkQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetReportCardRemarkQuery, ReportCardRemarkDto>
{
    public async Task<ReportCardRemarkDto> Handle(GetReportCardRemarkQuery request, CancellationToken cancellationToken)
    {
        // GRACIEUX PAR CONSTRUCTION : aucune remarque saisie n'est pas une erreur (même parti pris que
        // GetStudentDetailQueryHandler) — l'écran de saisie s'ouvre alors simplement vide, jamais en 404.
        var remark = await dbContext.ReportCardRemarks.AsNoTracking()
            .FirstOrDefaultAsync(r => r.StudentId == request.StudentId && r.TermId == request.TermId, cancellationToken);

        return new ReportCardRemarkDto(request.StudentId, request.TermId, remark?.DisciplinaryMention, remark?.Observations);
    }
}
