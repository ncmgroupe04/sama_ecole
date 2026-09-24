using System.IO.Compression;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.ReportCards.Queries.GetClassReportCardsZip;

/// <summary>
/// Bulletins de TOUS les élèves d'une classe pour un trimestre, en une archive ZIP — un fichier PDF par
/// élève, numéroté dans l'ordre alphabétique (même tri que la liste de classe). Pratique pour un envoi
/// par e-mail ou une sauvegarde, contrairement au PDF fusionné (<c>GetClassReportCardsPdfQuery</c>), fait
/// pour l'impression papier en lot.
///
/// IAuditableRequest (JGK-H01) : « impressions » fait partie des écritures sensibles explicitement
/// listées par le journal d'audit centralisé — une seule entrée pour toute la classe, pas une par élève.
/// </summary>
public record GetClassReportCardsZipQuery(Guid ClassroomId, Guid TermId) : IRequest<ReportCardPdfResult>, IAuditableRequest;

public class GetClassReportCardsZipQueryHandler(
    IApplicationDbContext dbContext,
    ReportCardDataService dataService,
    IReportCardPdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider)
    : IRequestHandler<GetClassReportCardsZipQuery, ReportCardPdfResult>
{
    public async Task<ReportCardPdfResult> Handle(GetClassReportCardsZipQuery request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la policy RLS bornent déjà à l'école courante : une classe ou un
        // trimestre d'une autre école y est structurellement introuvable (404, jamais un bulletin fuité).
        var classroom = await dbContext.Classrooms.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.ClassroomId, cancellationToken)
            ?? throw new KeyNotFoundException($"Classe {request.ClassroomId} introuvable dans votre établissement.");

        var term = await dbContext.Terms.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.TermId, cancellationToken)
            ?? throw new KeyNotFoundException($"Période {request.TermId} introuvable dans votre établissement.");

        var students = await dbContext.Students.AsNoTracking()
            .Where(s => s.ClassroomId == request.ClassroomId)
            .OrderBy(s => s.FullName)
            .ToListAsync(cancellationToken);

        if (students.Count == 0)
        {
            throw new BusinessRuleException("Cette classe ne compte aucun élève : aucun bulletin à générer.");
        }

        byte[]? logo = null;
        byte[]? directorSignature = null;
        byte[]? officialStamp = null;
        var imagesFetched = false;

        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var index = 1;
            foreach (var student in students)
            {
                var reportCard = await dataService.BuildAsync(student.Id, request.TermId, cancellationToken);

                // Le logo, la signature et le cachet sont les mêmes pour toute la classe (même école) :
                // récupérés une seule fois, au premier élève, plutôt qu'un aller-retour réseau répété
                // pour chaque bulletin.
                if (!imagesFetched)
                {
                    logo = await logoProvider.TryFetchAsync(reportCard.SchoolLogoUrl, cancellationToken);
                    directorSignature = await logoProvider.TryFetchAsync(reportCard.DirectorSignatureUrl, cancellationToken);
                    officialStamp = await logoProvider.TryFetchAsync(reportCard.OfficialStampUrl, cancellationToken);
                    imagesFetched = true;
                }

                var pdfBytes = pdfGenerator.Generate(reportCard, logo, directorSignature, officialStamp);

                var entryName = $"{index:D2}_{ClassBulletinsFileNaming.Sanitize(student.FullName)}.pdf";
                var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                await using var entryStream = entry.Open();
                await entryStream.WriteAsync(pdfBytes, cancellationToken);

                index++;
            }
        }

        var zipFileName = $"Bulletins_{ClassBulletinsFileNaming.Sanitize(classroom.Name)}_{ClassBulletinsFileNaming.Sanitize(term.Label)}.zip";
        return new ReportCardPdfResult(memoryStream.ToArray(), zipFileName);
    }
}
