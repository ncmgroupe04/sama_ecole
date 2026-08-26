using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Exams.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Exams.Queries.GetExamCandidateFormPdf;

public class GetExamCandidateFormPdfQueryHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ISchoolLogoProvider logoProvider,
    IExamCandidateFormPdfGenerator pdfGenerator)
    : IRequestHandler<GetExamCandidateFormPdfQuery, byte[]>
{
    public async Task<byte[]> Handle(GetExamCandidateFormPdfQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Le Global Query Filter + la policy RLS bornent déjà la recherche à l'école courante.
        if (!await dbContext.ExamDossiers.AsNoTracking().AnyAsync(d => d.Id == request.DossierId, cancellationToken))
        {
            throw new KeyNotFoundException($"Dossier d'examen {request.DossierId} introuvable.");
        }

        var candidates = await ExamCandidateFormModelBuilder.BuildAsync(
            dbContext, schoolId, dbContext.ExamDossiers.AsNoTracking().Where(d => d.Id == request.DossierId), cancellationToken);

        var logoUrl = await dbContext.Schools.AsNoTracking()
            .Where(s => s.Id == schoolId).Select(s => s.LogoUrl).SingleAsync(cancellationToken);
        var logo = await logoProvider.TryFetchAsync(logoUrl, cancellationToken);

        return pdfGenerator.Generate(candidates, logo);
    }
}
