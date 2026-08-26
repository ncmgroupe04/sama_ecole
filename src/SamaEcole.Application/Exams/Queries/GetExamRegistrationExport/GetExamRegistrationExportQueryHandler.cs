using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Exams.Queries.GetExamRegistrationExport;

public class GetExamRegistrationExportQueryHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    IExamRegistrationExcelGenerator excelGenerator)
    : IRequestHandler<GetExamRegistrationExportQuery, ExamRegistrationExportFile>
{
    public async Task<ExamRegistrationExportFile> Handle(
        GetExamRegistrationExportQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var session = await dbContext.ExamSessions.AsNoTracking()
            .Where(s => s.Id == request.ExamSessionId)
            .Select(s => new { s.ExamType, s.Series, SchoolYearLabel = s.SchoolYear.Label })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Session d'examen {request.ExamSessionId} introuvable.");

        var schoolName = await dbContext.Schools.AsNoTracking()
            .Where(s => s.Id == schoolId)
            .Select(s => s.Name)
            .SingleAsync(cancellationToken);

        // Même filtre que l'impression par lot des fiches de candidature (Volume 1 §22.5) : un dossier
        // encore Incomplet n'a rien à faire dans un relevé transmis à l'IEF/l'IA.
        var rows = await dbContext.ToListOrEmptyOnMissingTableAsync(
            dbContext.ExamDossiers.AsNoTracking()
                .Where(d => d.ExamSessionId == request.ExamSessionId && d.Status != ExamDossierStatus.Incomplet)
                .Select(d => new ExamRegistrationRow(
                    d.CandidateNumber,
                    dbContext.Students.Where(s => s.Id == d.StudentId).Select(s => s.FullName).FirstOrDefault() ?? "Élève supprimé",
                    dbContext.Students.Where(s => s.Id == d.StudentId).Select(s => s.Matricule).FirstOrDefault() ?? "",
                    dbContext.Students.Where(s => s.Id == d.StudentId).Select(s => s.BirthDate).FirstOrDefault(),
                    dbContext.Students.Where(s => s.Id == d.StudentId).Select(s => s.BirthPlace).FirstOrDefault() ?? "",
                    dbContext.Students.Where(s => s.Id == d.StudentId).Select(s => s.Gender).FirstOrDefault() ?? "",
                    dbContext.Classrooms.Where(c => c.Id == d.ClassroomId).Select(c => c.Name).FirstOrDefault() ?? "Classe supprimée",
                    d.ExamSession.Series,
                    d.ExamCenterName,
                    d.Status.ToString())),
            cancellationToken);

        var sessionLabel = session.Series is null
            ? $"{session.ExamType} {session.SchoolYearLabel}"
            : $"{session.ExamType} {session.Series} {session.SchoolYearLabel}";

        var content = excelGenerator.Generate(rows, schoolName, sessionLabel);
        var fileName = $"inscriptions_{session.ExamType}_{session.SchoolYearLabel.Replace("/", "-")}.xlsx";

        return new ExamRegistrationExportFile(content, fileName);
    }
}
