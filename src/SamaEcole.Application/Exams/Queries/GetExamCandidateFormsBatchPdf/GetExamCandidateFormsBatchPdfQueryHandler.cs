using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Exams.Common;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Exams.Queries.GetExamCandidateFormsBatchPdf;

public class GetExamCandidateFormsBatchPdfQueryHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ISchoolLogoProvider logoProvider,
    IExamCandidateFormPdfGenerator pdfGenerator)
    : IRequestHandler<GetExamCandidateFormsBatchPdfQuery, byte[]>
{
    public async Task<byte[]> Handle(GetExamCandidateFormsBatchPdfQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var dossiers = dbContext.ExamDossiers.AsNoTracking()
            .Where(d => d.Status == ExamDossierStatus.Complet
                        || d.Status == ExamDossierStatus.Transmis
                        || d.Status == ExamDossierStatus.Valide);

        if (request.ExamSessionId is { } examSessionId)
        {
            dossiers = dossiers.Where(d => d.ExamSessionId == examSessionId);
        }

        if (request.ClassroomId is { } classroomId)
        {
            dossiers = dossiers.Where(d => d.ClassroomId == classroomId);
        }

        var candidates = await ExamCandidateFormModelBuilder.BuildAsync(dbContext, schoolId, dossiers, cancellationToken);

        var logoUrl = await dbContext.Schools.AsNoTracking()
            .Where(s => s.Id == schoolId).Select(s => s.LogoUrl).SingleAsync(cancellationToken);
        var logo = await logoProvider.TryFetchAsync(logoUrl, cancellationToken);

        return pdfGenerator.Generate(candidates, logo);
    }
}
