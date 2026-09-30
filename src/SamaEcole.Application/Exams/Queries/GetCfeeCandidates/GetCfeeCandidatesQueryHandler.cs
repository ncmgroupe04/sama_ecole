using SamaEcole.Application.Classrooms;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Exams.Queries.GetCfeeCandidates;

public class GetCfeeCandidatesQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetCfeeCandidatesQuery, CfeeCandidatesResult>
{
    public async Task<CfeeCandidatesResult> Handle(GetCfeeCandidatesQuery request, CancellationToken cancellationToken)
    {
        var activeYear = await dbContext.SchoolYears.AsNoTracking()
            .Where(y => y.IsActive)
            .Select(y => new { y.Id, y.Label })
            .FirstOrDefaultAsync(cancellationToken);

        // Sans année active, « CM2 cette année » ne veut rien dire : cohorte vide, jamais un repli
        // silencieux sur une autre année (même choix que GetStudentsExportPdfQueryHandler).
        if (activeYear is null)
        {
            return new CfeeCandidatesResult(null, null, []);
        }

        var yearId = activeYear.Id;

        // Classroom.Level est du texte libre (AGENTS.md : pas d'énumération de niveaux) : le niveau
        // CM2 se lit sur Classroom.Name via ClassroomGradeLevels, une reconnaissance qui n'est PAS
        // traduisible en SQL — matérialisée en mémoire (une école a au plus quelques dizaines de
        // classes de cycle Primaire, jamais un volume qui justifierait autre chose).
        var primaryClassrooms = await dbContext.Classrooms.AsNoTracking()
            .Where(c => c.Cycle == CycleType.Primaire)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(cancellationToken);

        var classroomNames = primaryClassrooms.ToDictionary(c => c.Id, c => c.Name);

        var cm2ClassroomIds = primaryClassrooms
            .Where(c => ClassroomGradeLevels.FromClassroomName(c.Name, CycleType.Primaire) == "CM2")
            .Select(c => c.Id)
            .ToList();

        var cfeeSessionId = await dbContext.ExamSessions.AsNoTracking()
            .Where(s => s.ExamType == ExamType.CFEE && s.SchoolYearId == yearId)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (cm2ClassroomIds.Count == 0)
        {
            return new CfeeCandidatesResult(activeYear.Label, cfeeSessionId, []);
        }

        // Élève « actuellement en CM2 cette année » = Student.ClassroomId pointe vers une classe CM2
        // ET une inscription vivante existe pour l'année active — même filtre qu'
        // GetStudentsExportPdfQueryHandler (ActiveYearOnly) : ClassroomId seul ne suffit pas, un élève
        // non réinscrit cette année garderait le ClassroomId de sa dernière inscription connue.
        var students = await dbContext.Students.AsNoTracking()
            .Where(s => cm2ClassroomIds.Contains(s.ClassroomId)
                && dbContext.Enrollments.Any(e =>
                    e.StudentId == s.Id && e.SchoolYearId == yearId && e.Status != EnrollmentStatus.Cancelled))
            .OrderBy(s => s.FullName)
            .Select(s => new { s.Id, s.FullName, s.Matricule, s.ClassroomId })
            .ToListAsync(cancellationToken);

        if (students.Count == 0)
        {
            return new CfeeCandidatesResult(activeYear.Label, cfeeSessionId, []);
        }

        var studentIds = students.Select(s => s.Id).ToList();

        var dossiers = new List<CfeeDossierProjection>();
        if (cfeeSessionId is { } sessionId)
        {
            dossiers = await dbContext.ExamDossiers.AsNoTracking()
                .Where(d => d.ExamSessionId == sessionId && studentIds.Contains(d.StudentId))
                .Select(d => new CfeeDossierProjection(
                    d.Id,
                    d.StudentId,
                    d.Status.ToString(),
                    d.BirthCertificatePresent,
                    d.CivilStatusConforming,
                    d.PhotoPresent,
                    d.FeeReceiptPresent,
                    EF.Property<uint>(d, "xmin")))
                .ToListAsync(cancellationToken);
        }

        var admissionByDossierId = dossiers.Count == 0
            ? []
            : await dbContext.ExamResults.AsNoTracking()
                .Where(r => dossiers.Select(d => d.Id).Contains(r.ExamDossierId))
                .ToDictionaryAsync(r => r.ExamDossierId, r => r.IsAdmitted, cancellationToken);

        var dossierByStudentId = dossiers.ToDictionary(d => d.StudentId);

        var candidates = students.Select(s =>
        {
            var classroomName = classroomNames.GetValueOrDefault(s.ClassroomId, "Classe supprimée");

            if (!dossierByStudentId.TryGetValue(s.Id, out var dossier))
            {
                return new CfeeCandidate(
                    s.Id, s.FullName, s.Matricule, s.ClassroomId, classroomName,
                    HasDossier: false, DossierId: null, Status: null,
                    BirthCertificatePresent: false, CivilStatusConforming: null,
                    PhotoPresent: false, FeeReceiptPresent: false,
                    IsAdmitted: null, RowVersion: null);
            }

            var isAdmitted = admissionByDossierId.TryGetValue(dossier.Id, out var admitted) ? admitted : (bool?)null;

            return new CfeeCandidate(
                s.Id, s.FullName, s.Matricule, s.ClassroomId, classroomName,
                HasDossier: true, dossier.Id, dossier.Status,
                dossier.BirthCertificatePresent, dossier.CivilStatusConforming,
                dossier.PhotoPresent, dossier.FeeReceiptPresent,
                isAdmitted, dossier.RowVersion);
        }).ToList();

        return new CfeeCandidatesResult(activeYear.Label, cfeeSessionId, candidates);
    }

    private sealed record CfeeDossierProjection(
        Guid Id,
        Guid StudentId,
        string Status,
        bool BirthCertificatePresent,
        bool? CivilStatusConforming,
        bool PhotoPresent,
        bool FeeReceiptPresent,
        uint RowVersion);
}
