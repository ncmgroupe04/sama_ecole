using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Teachers.Queries.GetTeachersExportPdf;

public class GetTeachersExportPdfQueryHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ITeachersExportPdfGenerator pdfGenerator)
    : IRequestHandler<GetTeachersExportPdfQuery, TeachersExportPdfResult>
{
    public async Task<TeachersExportPdfResult> Handle(GetTeachersExportPdfQuery request, CancellationToken cancellationToken)
    {
        // Aucun filtre sur SchoolId ici, volontairement : le Global Query Filter l'applique déjà et la
        // policy RLS PostgreSQL le rejouerait même en son absence (AGENTS.md règle #2) — même
        // raisonnement que GetTeachersQueryHandler et GetStudentsExportPdfQueryHandler.
        var query = dbContext.Teachers.AsNoTracking();

        if (request.Status is { } status)
        {
            query = query.Where(t => t.Status == status);
        }

        var rows = await query
            .OrderBy(t => t.FullName)
            .Select(t => new { t.Id, t.Matricule, t.FullName, t.BirthDate, t.Phone, t.Email, t.Status })
            .ToListAsync(cancellationToken);

        // Une seconde requête plutôt qu'une sous-requête par ligne, comme GetTeachersQueryHandler.
        // Ici l'export N'EST PAS paginé : la jointure imbriquée coûterait un aller-retour par
        // enseignant sur tout l'effectif, ce que la liste écran ne subissait pas.
        var teacherIds = rows.Select(t => t.Id).ToList();

        var subjectsByTeacher = await dbContext.TeacherSubjects
            .AsNoTracking()
            .Where(ts => teacherIds.Contains(ts.TeacherId))
            .Select(ts => new
            {
                ts.TeacherId,
                SubjectName = dbContext.Subjects
                    .AsNoTracking()
                    .Where(s => s.Id == ts.SubjectId)
                    .Select(s => s.Name)
                    .FirstOrDefault() ?? "Matière supprimée"
            })
            .ToListAsync(cancellationToken);

        var teachers = rows
            .Select(t => new TeacherExportRow(
                t.Matricule,
                t.FullName,
                t.BirthDate,
                t.Phone,
                t.Email,
                StatusLabel(t.Status),
                string.Join(", ", subjectsByTeacher
                    .Where(s => s.TeacherId == t.Id)
                    .Select(s => s.SubjectName)
                    .OrderBy(name => name))))
            .ToList();

        var schoolName = await SchoolNameAsync(cancellationToken);

        var model = new TeachersExportModel(
            schoolName,
            request.Status is { } filtered ? StatusLabel(filtered) : null,
            teachers);

        return new TeachersExportPdfResult(pdfGenerator.Generate(model));
    }

    /// <summary>
    /// Mêmes libellés que la liste écran (wwwroot/js/teachers.js, <c>statusLabel</c>) : un document
    /// imprimé et l'écran dont il est issu ne doivent jamais nommer le même statut différemment.
    /// </summary>
    private static string StatusLabel(EntityStatus status) => status switch
    {
        EntityStatus.Active => "Actif",
        EntityStatus.Suspended => "Suspendu",
        EntityStatus.Blocked => "Bloqué",
        _ => status.ToString()
    };

    private async Task<string> SchoolNameAsync(CancellationToken cancellationToken)
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
