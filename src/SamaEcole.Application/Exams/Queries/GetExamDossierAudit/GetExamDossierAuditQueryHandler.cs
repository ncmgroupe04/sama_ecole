using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Exams.Queries.GetExamDossierAudit;

public class GetExamDossierAuditQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetExamDossierAuditQuery, IReadOnlyList<ExamDossierAuditEntry>>
{
    private record AuditRow(
        Guid Id,
        string StudentFullName,
        string ClassroomName,
        bool BirthCertificatePresent,
        bool? CivilStatusConforming,
        string? CandidateNumber);

    public async Task<IReadOnlyList<ExamDossierAuditEntry>> Handle(
        GetExamDossierAuditQuery request, CancellationToken cancellationToken)
    {
        var rows = await dbContext.ToListOrEmptyOnMissingTableAsync(
            dbContext.ExamDossiers.AsNoTracking()
                .Where(d => d.ExamSessionId == request.ExamSessionId && d.Status == ExamDossierStatus.Incomplet)
                .Select(d => new AuditRow(
                    d.Id,
                    dbContext.Students.Where(s => s.Id == d.StudentId).Select(s => s.FullName).FirstOrDefault() ?? "Élève supprimé",
                    dbContext.Classrooms.Where(c => c.Id == d.ClassroomId).Select(c => c.Name).FirstOrDefault() ?? "Classe supprimée",
                    d.BirthCertificatePresent,
                    d.CivilStatusConforming,
                    d.CandidateNumber)),
            cancellationToken);

        return rows
            .Select(row => new ExamDossierAuditEntry(row.Id, row.StudentFullName, row.ClassroomName, MissingItems(row)))
            .ToList();
    }

    private static List<string> MissingItems(AuditRow row)
    {
        var missing = new List<string>();

        if (!row.BirthCertificatePresent)
        {
            missing.Add("Extrait de naissance non déclaré présent");
        }

        if (row.CivilStatusConforming is null)
        {
            missing.Add("Conformité de l'état civil non contrôlée");
        }
        else if (row.CivilStatusConforming == false)
        {
            missing.Add("État civil déclaré non conforme");
        }

        if (row.CandidateNumber is null)
        {
            missing.Add("Centre et numéro de table non attribués");
        }

        return missing;
    }
}
