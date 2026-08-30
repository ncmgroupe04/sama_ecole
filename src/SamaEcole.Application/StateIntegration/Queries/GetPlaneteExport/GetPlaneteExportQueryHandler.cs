using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.StateIntegration.Queries.GetPlaneteExport;

/// <summary>
/// Construit la matrice « Planète Ready » (Volume 1 §23.2).
///
/// LE PÉRIMÈTRE EST L'INSCRIPTION, PAS L'ÉLÈVE. On part des <c>Enrollments</c> non annulés de l'année
/// demandée, et non de la table des élèves : un élève parti en janvier a bien été scolarisé cette
/// année-là et doit figurer au fichier, tandis qu'un élève créé pour l'année suivante n'y a pas sa
/// place. Partir des élèves « actuellement en base » aurait donné les deux erreurs à la fois.
///
/// La CLASSE retenue est celle de l'inscription (<c>Enrollment.ClassroomId</c>), figée, et non
/// <c>Student.ClassroomId</c> qui suit les transferts : un élève passé de 6e A à 6e B en cours d'année
/// doit apparaître dans la classe où il était inscrit, sans quoi les effectifs transmis ne
/// correspondraient plus à ceux déjà déclarés à la rentrée.
/// </summary>
public class GetPlaneteExportQueryHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    IPlaneteExportSerializer serializer,
    TimeProvider timeProvider)
    : IRequestHandler<GetPlaneteExportQuery, StateExportFile>
{
    public async Task<StateExportFile> Handle(
        GetPlaneteExportQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement dans le jeton d'authentification.");

        var school = await dbContext.Schools.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == schoolId, cancellationToken)
            ?? throw new KeyNotFoundException("Établissement introuvable.");

        // REFUS EXPLICITE plutôt qu'un fichier au code vide. Le SIMEN rejette un lot sans code
        // établissement, mais silencieusement et plusieurs jours plus tard : l'école croirait avoir
        // transmis. Mieux vaut échouer ici, où le message peut dire quoi corriger et où.
        if (string.IsNullOrWhiteSpace(school.NationalSchoolCode))
        {
            throw new BusinessRuleException(
                "Le code établissement national (SIMEN) n'est pas renseigné. "
                + "Saisissez-le dans Paramètres → Établissement avant de générer un export Planète : "
                + "un fichier transmis sans ce code est rejeté par le ministère.");
        }

        var schoolYear = await dbContext.SchoolYears.AsNoTracking()
            .FirstOrDefaultAsync(y => y.Id == request.SchoolYearId, cancellationToken)
            ?? throw new KeyNotFoundException("Année scolaire introuvable dans votre établissement.");

        // Aucun filtre sur SchoolId : le Global Query Filter l'applique, la policy RLS le rejouerait
        // même s'il disparaissait (AGENTS.md règle #2).
        var enrollments = dbContext.Enrollments.AsNoTracking()
            .Where(e => e.SchoolYearId == request.SchoolYearId
                        && e.Status != EnrollmentStatus.Cancelled);

        if (request.ClassroomId is { } classroomId)
        {
            enrollments = enrollments.Where(e => e.ClassroomId == classroomId);
        }

        var rows = await enrollments
            .Join(dbContext.Students.AsNoTracking(),
                enrollment => enrollment.StudentId,
                student => student.Id,
                (enrollment, student) => new { enrollment, student })
            .Join(dbContext.Classrooms.AsNoTracking(),
                pair => pair.enrollment.ClassroomId,
                classroom => classroom.Id,
                (pair, classroom) => new
                {
                    pair.student,
                    pair.enrollment.IsRepeating,
                    ClassroomName = classroom.Name,
                    classroom.Level,
                    classroom.Cycle
                })
            .OrderBy(r => r.Level).ThenBy(r => r.ClassroomName).ThenBy(r => r.student.FullName)
            .ToListAsync(cancellationToken);

        var students = rows.Select(r =>
        {
            // Le format national sépare nom et prénoms ; le modèle ne porte qu'un FullName. On
            // réutilise l'heuristique DÉJÀ éprouvée du bulletin (dernier mot = nom de famille, usage
            // sénégalais) plutôt que d'en écrire une seconde qui divergerait : un élève doit porter le
            // même découpage sur son bulletin et dans le fichier du ministère.
            var (firstNames, lastName) = StudentNameSplitter.Split(r.student.FullName);

            return new PlaneteStudentSyncDto(
                IenNumber: r.student.IenNumber,
                IsIenProvisional: r.student.IsIenProvisional,
                Matricule: r.student.Matricule,
                LastName: lastName,
                FirstNames: firstNames,
                BirthDate: r.student.BirthDate,
                BirthPlace: r.student.BirthPlace,
                Gender: r.student.Gender,
                ClassroomName: r.ClassroomName,
                Level: r.Level,
                Cycle: r.Cycle.ToString(),
                SchoolYearLabel: schoolYear.Label,
                IsRepeating: r.IsRepeating,
                GuardianName: r.student.GuardianName,
                GuardianPhone: r.student.GuardianPhone,
                NationalSchoolCode: school.NationalSchoolCode);
        }).ToList();

        var export = new PlaneteExportDto(
            NationalSchoolCode: school.NationalSchoolCode,
            SchoolName: school.Name,
            InspectionAcademie: school.InspectionAcademie,
            InspectionEducationFormation: school.InspectionEducationFormation,
            SchoolDistrictCode: school.SchoolDistrictCode,
            SchoolYearLabel: schoolYear.Label,
            GeneratedAt: timeProvider.GetUtcNow(),
            Students: students);

        return serializer.Serialize(export, request.Format);
    }
}
