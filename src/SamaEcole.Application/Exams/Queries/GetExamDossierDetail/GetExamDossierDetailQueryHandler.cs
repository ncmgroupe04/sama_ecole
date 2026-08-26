using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Exams.Commands.RecordExamResult;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Exams.Queries.GetExamDossierDetail;

public class GetExamDossierDetailQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetExamDossierDetailQuery, ExamDossierDetail>
{
    public async Task<ExamDossierDetail> Handle(GetExamDossierDetailQuery request, CancellationToken cancellationToken)
    {
        var dossier = await dbContext.ExamDossiers.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Dossier d'examen {request.Id} introuvable.");

        var studentFullName = await dbContext.Students.AsNoTracking()
            .Where(s => s.Id == dossier.StudentId)
            .Select(s => s.FullName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Élève supprimé";

        var classroomName = await dbContext.Classrooms.AsNoTracking()
            .Where(c => c.Id == dossier.ClassroomId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "Classe supprimée";

        var result = await dbContext.ExamResults.AsNoTracking()
            .Where(r => r.ExamDossierId == dossier.Id)
            .Select(r => new ExamResultDto(r.Id, r.ExamDossierId, r.IsAdmitted, r.Mention, r.AverageScore, r.DeliberatedOn))
            .FirstOrDefaultAsync(cancellationToken);

        var rowVersion = EF.Property<uint>(dossier, "xmin");

        return new ExamDossierDetail(
            dossier.Id,
            dossier.ExamSessionId,
            dossier.StudentId,
            studentFullName,
            dossier.ClassroomId,
            classroomName,
            dossier.CandidateNumber,
            dossier.ExamCenterName,
            dossier.BirthCertificatePresent,
            dossier.CivilStatusConforming,
            dossier.Status.ToString(),
            rowVersion,
            dossier.BirthCertificateNumber,
            dossier.CivilStatusNotes,
            dossier.TransmittedOn,
            result);
    }
}
