using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Teachers.Commands.AssignTeacher;

public class AssignTeacherCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider)
    : IRequestHandler<AssignTeacherCommand, AssignTeacherResult>
{
    public async Task<AssignTeacherResult> Handle(AssignTeacherCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Le Global Query Filter + la RLS bornent déjà cette recherche à l'école courante : un
        // TeacherId d'une autre école est simplement introuvable ici, jamais exposé.
        var teacherExists = await dbContext.Teachers.AnyAsync(t => t.Id == request.TeacherId, cancellationToken);
        if (!teacherExists)
        {
            throw new KeyNotFoundException($"Enseignant {request.TeacherId} introuvable.");
        }

        // L'attribution porte l'année ACTIVE (même règle que CreateEnrollmentCommandHandler) : sans
        // année active, l'école ne peut rien rattacher — on refuse en 422 plutôt que de deviner.
        // AsNoTracking : seul activeYear.Id est lu plus bas, jamais modifié.
        var activeYear = await dbContext.SchoolYears
            .AsNoTracking()
            .FirstOrDefaultAsync(y => y.IsActive, cancellationToken)
            ?? throw new ValidationException([
                new ValidationFailure(
                    "SchoolYear",
                    "Aucune année scolaire active. Activez une année scolaire avant d'attribuer un enseignant.")
            ]);

        var classroomExists = await dbContext.Classrooms.AnyAsync(c => c.Id == request.ClassroomId, cancellationToken);
        if (!classroomExists)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.ClassroomId), "La classe indiquée n'existe pas dans votre établissement.")
            ]);
        }

        var subjectExists = await dbContext.Subjects.AnyAsync(s => s.Id == request.SubjectId, cancellationToken);
        if (!subjectExists)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.SubjectId), "La matière indiquée n'existe pas dans votre établissement.")
            ]);
        }

        var assignmentExists = await dbContext.TeacherAssignments
            .AnyAsync(a => a.TeacherId == request.TeacherId 
                        && a.ClassroomId == request.ClassroomId
                        && a.SubjectId == request.SubjectId
                        && a.SchoolYearId == activeYear.Id, cancellationToken);
                        
        if (assignmentExists)
        {
            throw new ValidationException([
                new ValidationFailure(
                    "Assignation", 
                    "Cet enseignant est déjà affecté à cette classe pour cette matière (année en cours).")
            ]);
        }

        var assignment = new TeacherAssignment
        {
            SchoolId = schoolId,
            TeacherId = request.TeacherId,
            ClassroomId = request.ClassroomId,
            SubjectId = request.SubjectId,
            SchoolYearId = activeYear.Id
        };

        dbContext.TeacherAssignments.Add(assignment);

        // Une même attribution (enseignant, classe, matière, année) déjà posée viole l'index unique :
        // SaveChangesAsync la traduit en ConcurrencyConflictException → 409 (AGENTS.md règle #5).
        await dbContext.SaveChangesAsync(cancellationToken);

        return new AssignTeacherResult(assignment.Id, assignment.ClassroomId, assignment.SubjectId, assignment.SchoolYearId);
    }
}
