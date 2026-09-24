using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.ReportCards.Queries.GetClassReportCardsPdf;

/// <summary>
/// Bulletins de TOUS les élèves d'une classe pour un trimestre, fusionnés en UN SEUL PDF (une page A5
/// par élève, ordre alphabétique) — pensé pour l'impression papier en lot (un seul document à envoyer au
/// bac d'imprimante), contrairement à l'archive ZIP (<c>GetClassReportCardsZipQuery</c>), faite pour un
/// envoi/une sauvegarde fichier par fichier.
///
/// IAuditableRequest (JGK-H01) : « impressions » fait partie des écritures sensibles explicitement
/// listées par le journal d'audit centralisé — une seule entrée pour toute la classe, pas une par élève.
/// </summary>
public record GetClassReportCardsPdfQuery(Guid ClassroomId, Guid TermId) : IRequest<ReportCardPdfResult>, IAuditableRequest;

public class GetClassReportCardsPdfQueryHandler(
    IApplicationDbContext dbContext,
    ReportCardDataService dataService,
    IClassBulletinsPdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider)
    : IRequestHandler<GetClassReportCardsPdfQuery, ReportCardPdfResult>
{
    public async Task<ReportCardPdfResult> Handle(GetClassReportCardsPdfQuery request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la policy RLS bornent déjà à l'école courante : une classe ou un
        // trimestre d'une autre école y est structurellement introuvable (404, jamais un bulletin fuité).
        var classroom = await dbContext.Classrooms.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.ClassroomId, cancellationToken)
            ?? throw new KeyNotFoundException($"Classe {request.ClassroomId} introuvable dans votre établissement.");

        var term = await dbContext.Terms.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.TermId, cancellationToken)
            ?? throw new KeyNotFoundException($"Période {request.TermId} introuvable dans votre établissement.");

        var studentIds = await dbContext.Students.AsNoTracking()
            .Where(s => s.ClassroomId == request.ClassroomId)
            .OrderBy(s => s.FullName)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        if (studentIds.Count == 0)
        {
            throw new BusinessRuleException("Cette classe ne compte aucun élève : aucun bulletin à générer.");
        }

        var reportCards = new List<ReportCardDto>(studentIds.Count);
        foreach (var studentId in studentIds)
        {
            reportCards.Add(await dataService.BuildAsync(studentId, request.TermId, cancellationToken));
        }

        // Le logo, la signature et le cachet sont les mêmes pour toute la classe (même école) : résolus
        // une seule fois à partir du premier bulletin plutôt qu'un aller-retour réseau répété par élève.
        var logo = await logoProvider.TryFetchAsync(reportCards[0].SchoolLogoUrl, cancellationToken);
        var directorSignature = await logoProvider.TryFetchAsync(reportCards[0].DirectorSignatureUrl, cancellationToken);
        var officialStamp = await logoProvider.TryFetchAsync(reportCards[0].OfficialStampUrl, cancellationToken);

        var fileName = $"Bulletins_{ClassBulletinsFileNaming.Sanitize(classroom.Name)}_{ClassBulletinsFileNaming.Sanitize(term.Label)}.pdf";
        return new ReportCardPdfResult(pdfGenerator.Generate(reportCards, logo, directorSignature, officialStamp), fileName);
    }
}
