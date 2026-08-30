using SamaEcole.Application.Common;
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Teachers.Queries.GetTeacherById;

public class GetTeacherByIdQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetTeacherByIdQuery, TeacherProfileDto>
{
    public async Task<TeacherProfileDto> Handle(GetTeacherByIdQuery request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la RLS bornent déjà la recherche à l'école courante : un
        // TeacherId d'une autre école est simplement introuvable ici, jamais exposé.
        var teacher = await dbContext.Teachers.AsNoTracking()
            .Where(t => t.Id == request.TeacherId)
            .Select(t => new { Entity = t, RowVersion = EF.Property<uint>(t, "xmin") })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Enseignant {request.TeacherId} introuvable.");

        var subjects = await dbContext.TeacherSubjects.AsNoTracking()
            .Where(ts => ts.TeacherId == request.TeacherId)
            .Select(ts => dbContext.Subjects
                .AsNoTracking()
                .Where(s => s.Id == ts.SubjectId)
                .Select(s => s.Name)
                .FirstOrDefault() ?? "Matière supprimée")
            .ToListAsync(cancellationToken);

        // L'HISTORIQUE demandé par le ticket : toutes les attributions posées au fil des années, pas
        // seulement celles de l'année active — TeacherAssignment n'est jamais réécrite (voir son
        // commentaire de classe), la table entière EST l'historique.
        var assignments = await (
            from a in dbContext.TeacherAssignments.AsNoTracking()
            join c in dbContext.Classrooms.AsNoTracking() on a.ClassroomId equals c.Id
            join s in dbContext.Subjects.AsNoTracking() on a.SubjectId equals s.Id
            join y in dbContext.SchoolYears.AsNoTracking() on a.SchoolYearId equals y.Id
            where a.TeacherId == request.TeacherId
            orderby y.StartDate descending, c.Name, s.Name
            select new TeacherAssignmentDto(a.Id, c.Name, s.Name, y.Id, y.Label, y.IsActive))
            .ToListAsync(cancellationToken);

        return new TeacherProfileDto(
            teacher.Entity.Id,
            teacher.Entity.Matricule,
            teacher.Entity.FullName,
            teacher.Entity.Email,
            teacher.Entity.Phone,
            teacher.Entity.BirthDate,
            teacher.Entity.BirthPlace,
            teacher.Entity.Address,
            teacher.Entity.PhotoUrl,
            PhotoDisplay.ToDisplayUrl(teacher.Entity.PhotoData, teacher.Entity.PhotoUrl),
            teacher.Entity.Status.ToString(),
            subjects,
            assignments,
            teacher.RowVersion,
            teacher.Entity.Gender,
            teacher.Entity.AcademicQualification,
            teacher.Entity.ProfessionalQualification,
            teacher.Entity.CivilServiceStatus,
            teacher.Entity.CivilServiceMatricule,
            teacher.Entity.FirstAppointmentDate);
    }
}
