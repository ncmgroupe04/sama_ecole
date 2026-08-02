using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Students.Queries.GetStudentsExportPdf;

public class GetStudentsExportPdfQueryHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    IStudentsExportPdfGenerator pdfGenerator)
    : IRequestHandler<GetStudentsExportPdfQuery, StudentsExportPdfResult>
{
    public async Task<StudentsExportPdfResult> Handle(GetStudentsExportPdfQuery request, CancellationToken cancellationToken)
    {
        // Aucun filtre sur SchoolId ici, volontairement : le Global Query Filter l'applique
        // automatiquement, et la policy RLS PostgreSQL le rejouerait même si ce filtre disparaissait
        // un jour (AGENTS.md règle #2) — voir GetStudentsQueryHandler pour le même raisonnement.
        var query = dbContext.Students.AsNoTracking();

        string? schoolYearLabel = null;
        if (request.ActiveYearOnly)
        {
            var activeYear = await dbContext.SchoolYears.AsNoTracking()
                .Where(y => y.IsActive)
                .Select(y => new { y.Id, y.Label })
                .FirstOrDefaultAsync(cancellationToken);

            // Sans année active, « inscrits » ne veut rien dire : export vide, jamais un repli
            // silencieux sur tout l'effectif (même choix que GetStudentsQueryHandler).
            if (activeYear is null)
            {
                var emptySchoolName = await SchoolNameAsync(dbContext, tenantProvider, cancellationToken);
                var emptyModel = new StudentsExportModel(emptySchoolName, null, null, []);
                return new StudentsExportPdfResult(pdfGenerator.Generate(emptyModel));
            }

            schoolYearLabel = activeYear.Label;
            var yearId = activeYear.Id;
            query = query.Where(s => dbContext.Enrollments.Any(e =>
                e.StudentId == s.Id && e.SchoolYearId == yearId && e.Status != EnrollmentStatus.Cancelled));
        }

        if (request.ClassroomId is { } classroomId)
        {
            query = query.Where(s => s.ClassroomId == classroomId);
        }

        var rows = await query
            .OrderBy(s => s.FullName)
            .Select(s => new
            {
                s.Matricule,
                s.FullName,
                s.BirthDate,
                s.Gender,
                ClassroomName = dbContext.Classrooms
                    .AsNoTracking()
                    .Where(c => c.Id == s.ClassroomId)
                    .Select(c => c.Name)
                    .FirstOrDefault() ?? "Classe supprimée",
                s.GuardianName,
                s.GuardianPhone
            })
            .ToListAsync(cancellationToken);

        var students = rows
            .Select(r => new StudentExportRow(
                r.Matricule, r.FullName, r.BirthDate, r.Gender, r.ClassroomName, r.GuardianName, r.GuardianPhone))
            .ToList();

        string? className = null;
        if (request.ClassroomId is { } filteredClassroomId)
        {
            className = await dbContext.Classrooms.AsNoTracking()
                .Where(c => c.Id == filteredClassroomId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var schoolName = await SchoolNameAsync(dbContext, tenantProvider, cancellationToken);

        var model = new StudentsExportModel(schoolName, className, schoolYearLabel, students);

        return new StudentsExportPdfResult(pdfGenerator.Generate(model));
    }

    private static async Task<string> SchoolNameAsync(
        IApplicationDbContext dbContext, ITenantProvider tenantProvider, CancellationToken cancellationToken)
    {
        if (tenantProvider.CurrentSchoolId is not { } schoolId)
        {
            return string.Empty;
        }

        return await dbContext.Schools.AsNoTracking()
            .Where(s => s.Id == schoolId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
    }
}
