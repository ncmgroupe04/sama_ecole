using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.ReportCards.Queries.GetClassDeliberationPdf;

/// <summary>
/// PV de délibération d'une classe pour un trimestre.
/// Agrège les résultats de tous les élèves de la classe et génère un rapport PDF avec les statistiques
/// et la liste des élèves, triés par rang ou par ordre alphabétique.
/// </summary>
public record GetClassDeliberationPdfQuery(Guid ClassroomId, Guid TermId) : IRequest<ReportCardPdfResult>, IAuditableRequest;

public class GetClassDeliberationPdfQueryHandler(
    IApplicationDbContext dbContext,
    ReportCardDataService dataService,
    IClassDeliberationPdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider)
    : IRequestHandler<GetClassDeliberationPdfQuery, ReportCardPdfResult>
{
    public async Task<ReportCardPdfResult> Handle(GetClassDeliberationPdfQuery request, CancellationToken cancellationToken)
    {
        var classroom = await dbContext.Classrooms.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.ClassroomId, cancellationToken)
            ?? throw new KeyNotFoundException($"Classe {request.ClassroomId} introuvable dans votre établissement.");

        var term = await dbContext.Terms.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.TermId, cancellationToken)
            ?? throw new KeyNotFoundException($"Trimestre {request.TermId} introuvable dans votre établissement.");

        var studentIds = await dbContext.Students.AsNoTracking()
            .Where(s => s.ClassroomId == request.ClassroomId)
            .OrderBy(s => s.FullName)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        if (studentIds.Count == 0)
        {
            throw new BusinessRuleException("Cette classe ne compte aucun élève : aucun PV à générer.");
        }

        var reportCards = new List<ReportCardDto>(studentIds.Count);
        foreach (var studentId in studentIds)
        {
            reportCards.Add(await dataService.BuildAsync(studentId, request.TermId, cancellationToken));
        }

        // Tri par ordre de mérite (rang général) par défaut pour le PV.
        // Les élèves non classés (sans note) apparaissent à la fin.
        var sortedReportCards = reportCards
            .OrderBy(r => r.GeneralRank)
            .ThenBy(r => r.StudentFullName)
            .ToList();

        var logo = await logoProvider.TryFetchAsync(reportCards[0].SchoolLogoUrl, cancellationToken);

        var fileName = $"PV_{ClassBulletinsFileNaming.Sanitize(classroom.Name)}_{ClassBulletinsFileNaming.Sanitize(term.Label)}.pdf";
        return new ReportCardPdfResult(pdfGenerator.Generate(sortedReportCards, logo), fileName);
    }
}
