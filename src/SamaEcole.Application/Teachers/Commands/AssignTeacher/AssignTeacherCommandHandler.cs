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

        // Pré-contrôle du doublon, pour répondre par un message lisible plutôt que par le refus de la
        // base. Sans IgnoreQueryFilters : le Global Query Filter écarte les attributions RETIRÉES
        // (soft delete), et c'est exactement ce qu'on veut ici. Les inclure — ce que faisait
        // IgnoreQueryFilters — faisait dire à la plateforme « déjà affecté » à propos d'une
        // attribution que le Directeur venait précisément de retirer, sans aucun moyen de revenir en
        // arrière. Voir l'index partiel correspondant dans TeacherAssignmentConfiguration.
        var assignmentExists = await dbContext.TeacherAssignments
            .AnyAsync(a => a.TeacherId == request.TeacherId
                        && a.ClassroomId == request.ClassroomId
                        && a.SubjectId == request.SubjectId
                        && a.SchoolYearId == activeYear.Id, cancellationToken);

        if (assignmentExists)
        {
            // Message destiné à un Directeur ou à un Secrétariat : ce qui s'est passé, pourquoi c'est
            // refusé, et quoi faire ensuite. Aucun terme technique (voir UniqueConstraintCatalog, qui
            // tient la même promesse pour les refus venus de la base).
            //
            // BusinessRuleException → 409, PAS ValidationException → 422 : ce doublon est un CONFLIT
            // d'état (la ressource existe déjà), pas une saisie mal formée. C'est le contrat publié
            // (openapi.yaml, « 409 : Cette attribution (enseignant, classe, matière, année) existe
            // déjà »), la convention du dépôt pour un doublon (Volume_4_API_Design.md §392) et le code
            // que rendait déjà l'index unique avant que ce pré-contrôle n'existe. Le pré-contrôle
            // améliore le MESSAGE, il ne doit pas changer le CODE.
            throw new BusinessRuleException(
                "Cet enseignant assure déjà cette matière dans cette classe pour l'année scolaire "
                + "en cours. Une matière ne peut lui être confiée qu'une seule fois par classe : "
                + "l'affectation figure déjà dans la liste ci-dessus. Pour la modifier, retirez-la "
                + "d'abord, ou choisissez une autre classe ou une autre matière.");
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
