using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;

namespace SamaEcole.Application.ReportCards.Queries.GetClassAnnualDeliberationPdf;

/// <summary>
/// GET /api/v1/report-cards/class-deliberation/annual/pdf — procès-verbal ANNUEL du conseil de classe
/// (Évolution N°7) : moyenne et rang annuels de chaque élève, statistiques Filles/Garçons, et la décision de
/// fin d'année — saisie par le conseil, à défaut PROPOSÉE d'après les seuils de l'école (CouncilRules).
///
/// Bâti sur la DERNIÈRE période de l'exercice : c'est elle qui porte la décision du conseil et le récapitulatif
/// annuel (ReportCardDataService) — une seule source de vérité avec les bulletins.
/// </summary>
public record GetClassAnnualDeliberationPdfQuery(Guid ClassroomId, Guid SchoolYearId)
    : IRequest<ReportCardPdfResult>, IAuditableRequest;

public class GetClassAnnualDeliberationPdfQueryHandler(
    IApplicationDbContext dbContext,
    ReportCardDataService dataService,
    IClassDeliberationPdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider)
    : IRequestHandler<GetClassAnnualDeliberationPdfQuery, ReportCardPdfResult>
{
    public async Task<ReportCardPdfResult> Handle(GetClassAnnualDeliberationPdfQuery request, CancellationToken cancellationToken)
    {
        var classroom = await dbContext.Classrooms.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.ClassroomId, cancellationToken)
            ?? throw new KeyNotFoundException($"Classe {request.ClassroomId} introuvable dans votre établissement.");

        var year = await dbContext.SchoolYears.AsNoTracking()
            .FirstOrDefaultAsync(y => y.Id == request.SchoolYearId, cancellationToken)
            ?? throw new KeyNotFoundException($"Année scolaire {request.SchoolYearId} introuvable dans votre établissement.");

        var lastTermId = await dbContext.Terms.AsNoTracking()
            .Where(t => t.SchoolYearId == year.Id)
            .OrderByDescending(t => t.Order)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new BusinessRuleException("Cette année scolaire n'a aucune période : aucun PV annuel à générer.");

        var studentIds = await dbContext.Students.AsNoTracking()
            .Where(s => s.ClassroomId == request.ClassroomId)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        if (studentIds.Count == 0)
        {
            throw new BusinessRuleException("Cette classe ne compte aucun élève : aucun PV à générer.");
        }

        var cards = new List<ReportCardDto>(studentIds.Count);
        foreach (var studentId in studentIds)
        {
            cards.Add(await dataService.BuildAsync(studentId, lastTermId, cancellationToken));
        }

        // Ordre de mérite annuel ; les élèves non classés (aucune moyenne) en fin de liste, par nom.
        var sorted = cards
            .OrderBy(c => c.AnnualRank ?? int.MaxValue)
            .ThenBy(c => c.StudentFullName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var logo = await logoProvider.TryFetchAsync(cards[0].SchoolLogoUrl, cancellationToken);
        var fileName = $"PV_annuel_{ClassBulletinsFileNaming.Sanitize(classroom.Name)}_{ClassBulletinsFileNaming.Sanitize(year.Label)}.pdf";

        return new ReportCardPdfResult(pdfGenerator.Generate(sorted, logo, DeliberationScope.Annual), fileName);
    }
}
