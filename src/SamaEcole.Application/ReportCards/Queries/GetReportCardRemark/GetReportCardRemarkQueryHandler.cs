using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Grades;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.ReportCards.Queries.GetReportCardRemark;

public class GetReportCardRemarkQueryHandler(IApplicationDbContext dbContext, ISender mediator)
    : IRequestHandler<GetReportCardRemarkQuery, ReportCardRemarkDto>
{
    public async Task<ReportCardRemarkDto> Handle(GetReportCardRemarkQuery request, CancellationToken cancellationToken)
    {
        // GRACIEUX PAR CONSTRUCTION : aucune remarque saisie n'est pas une erreur (même parti pris que
        // GetStudentDetailQueryHandler) — l'écran de saisie s'ouvre alors simplement vide, jamais en 404.
        var remark = await dbContext.ReportCardRemarks.AsNoTracking()
            .FirstOrDefaultAsync(r => r.StudentId == request.StudentId && r.TermId == request.TermId, cancellationToken);

        // Distinction PROPOSÉE par le moteur du bulletin : la MÊME formule que celle qu'appliquerait
        // ReportCardDataService faute de saisie (remark?.DisciplinaryMention ?? Suggest(...)). On la
        // remonte pour que l'écran la pré-coche — validation en un clic, ou override. Le calcul suppose
        // des notes : sans elles (TotalCoefficients == 0), aucune moyenne à qualifier, aucune suggestion.
        //
        // Le try/catch préserve le contrat gracieux : un élève ou un trimestre absent (cas limite hors
        // écran) laisse GetGradeSummaryQuery lever KeyNotFoundException — l'écran s'ouvre alors sans
        // suggestion plutôt qu'en erreur.
        DisciplinaryMention? suggested = null;
        decimal? generalAverage = null;
        try
        {
            var summary = await mediator.Send(new GetGradeSummaryQuery(request.StudentId, request.TermId), cancellationToken);
            if (summary.TotalCoefficients > 0)
            {
                generalAverage = summary.GeneralAverage;
                var scale = await GradingScaleGuard.ResolveScaleForStudentAsync(dbContext, request.StudentId, cancellationToken);
                var rules = await CouncilRules.ResolveAsync(dbContext, cancellationToken);
                suggested = DisciplinaryMentionPolicy.Suggest(
                    summary.GeneralAverage, scale, hasGrades: true, rules, rules.HasEliminatoryGrade(summary.Subjects));
            }
        }
        catch (KeyNotFoundException)
        {
            // Élève/trimestre introuvable : on reste gracieux, sans suggestion.
        }

        return new ReportCardRemarkDto(
            request.StudentId, request.TermId,
            remark?.DisciplinaryMention, remark?.CouncilDecision, remark?.Observations,
            suggested, generalAverage);
    }
}
