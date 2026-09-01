using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Exams;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Exams.Queries.GetExamConvocationPdf;

public class GetExamConvocationPdfQueryHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ISchoolLogoProvider logoProvider,
    IQrCodeService qrCodeService,
    TimeProvider timeProvider,
    IExamConvocationPdfGenerator pdfGenerator)
    : IRequestHandler<GetExamConvocationPdfQuery, byte[]>
{
    public async Task<byte[]> Handle(GetExamConvocationPdfQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var dossier = await dbContext.ExamDossiers.AsNoTracking()
            .Where(d => d.Id == request.DossierId)
            .Select(d => new
            {
                d.CandidateNumber,
                d.ExamCenterName,
                ExamType = d.ExamSession.ExamType.ToString(),
                d.ExamSession.Series,
                StudentFullName = dbContext.Students.Where(s => s.Id == d.StudentId).Select(s => s.FullName).FirstOrDefault() ?? "Élève supprimé",
                StudentMatricule = dbContext.Students.Where(s => s.Id == d.StudentId).Select(s => s.Matricule).FirstOrDefault() ?? "",
                ClassroomName = dbContext.Classrooms.Where(c => c.Id == d.ClassroomId).Select(c => c.Name).FirstOrDefault() ?? "Classe supprimée"
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Dossier d'examen {request.DossierId} introuvable.");

        if (string.IsNullOrWhiteSpace(dossier.CandidateNumber) || string.IsNullOrWhiteSpace(dossier.ExamCenterName))
        {
            throw new BusinessRuleException(
                "Le centre d'examen et le numéro de candidat doivent être attribués avant d'émettre la convocation.");
        }

        var school = await dbContext.Schools.AsNoTracking()
            .Where(s => s.Id == schoolId)
            .Select(s => new { s.Name, s.InspectionAcademie, s.InspectionEducationFormation, s.City, s.LogoUrl })
            .SingleAsync(cancellationToken);

        var reference = $"CONV-{dossier.CandidateNumber}";

        var model = new ExamConvocationModel(
            reference,
            school.Name,
            school.InspectionAcademie,
            school.InspectionEducationFormation,
            school.City,
            timeProvider.GetUtcNow(),
            dossier.StudentFullName,
            dossier.StudentMatricule,
            dossier.ClassroomName,
            dossier.ExamType,
            dossier.Series,
            dossier.CandidateNumber,
            dossier.ExamCenterName);

        var logo = await logoProvider.TryFetchAsync(school.LogoUrl, cancellationToken);
        var qrCode = qrCodeService.GenerateQrCode($"https://app.samaecole.sn/verify?ref={reference}");

        return pdfGenerator.Generate(model, logo, qrCode);
    }
}
