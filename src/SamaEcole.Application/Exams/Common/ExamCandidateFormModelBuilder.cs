using SamaEcole.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Exams.Common;

/// <summary>
/// Construit les <see cref="ExamCandidateFormModel"/> d'un ensemble de dossiers — point de passage
/// unique entre GetExamCandidateFormPdfQueryHandler (un dossier) et GetExamCandidateFormsBatchPdfQueryHandler
/// (plusieurs), pour que la fiche unitaire et celle imprimée en lot ne divergent jamais.
/// </summary>
internal static class ExamCandidateFormModelBuilder
{
    public static async Task<List<ExamCandidateFormModel>> BuildAsync(
        IApplicationDbContext dbContext, Guid schoolId, IQueryable<Domain.Entities.ExamDossier> dossiers,
        CancellationToken cancellationToken)
    {
        var school = await dbContext.Schools.AsNoTracking()
            .Where(s => s.Id == schoolId)
            .Select(s => new { s.Name, s.InspectionAcademie, s.InspectionEducationFormation, s.Address })
            .SingleAsync(cancellationToken);

        var rows = await dossiers
            .Select(d => new
            {
                d.Id,
                d.CandidateNumber,
                d.ExamCenterName,
                d.BirthCertificateNumber,
                d.BirthCertificatePresent,
                ExamType = d.ExamSession.ExamType.ToString(),
                d.ExamSession.Series,
                StudentFullName = dbContext.Students.Where(s => s.Id == d.StudentId).Select(s => s.FullName).FirstOrDefault() ?? "Élève supprimé",
                StudentMatricule = dbContext.Students.Where(s => s.Id == d.StudentId).Select(s => s.Matricule).FirstOrDefault() ?? "",
                BirthDate = dbContext.Students.Where(s => s.Id == d.StudentId).Select(s => s.BirthDate).FirstOrDefault(),
                BirthPlace = dbContext.Students.Where(s => s.Id == d.StudentId).Select(s => s.BirthPlace).FirstOrDefault() ?? "",
                Gender = dbContext.Students.Where(s => s.Id == d.StudentId).Select(s => s.Gender).FirstOrDefault() ?? "",
                ClassroomName = dbContext.Classrooms.Where(c => c.Id == d.ClassroomId).Select(c => c.Name).FirstOrDefault() ?? "Classe supprimée"
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row => new ExamCandidateFormModel(
                Reference(row.Id, row.CandidateNumber),
                school.Name,
                school.InspectionAcademie,
                school.InspectionEducationFormation,
                school.Address,
                row.StudentFullName,
                row.StudentMatricule,
                row.BirthDate,
                row.BirthPlace,
                row.Gender,
                row.ClassroomName,
                row.ExamType,
                row.Series,
                row.CandidateNumber,
                row.ExamCenterName,
                row.BirthCertificateNumber,
                row.BirthCertificatePresent))
            .ToList();
    }

    /// <summary>Le numéro de table dès qu'il existe (lisible, déjà connu du candidat) ; à défaut, un fragment stable de l'identifiant du dossier.</summary>
    public static string Reference(Guid dossierId, string? candidateNumber) =>
        candidateNumber ?? dossierId.ToString("N")[..8].ToUpperInvariant();
}
