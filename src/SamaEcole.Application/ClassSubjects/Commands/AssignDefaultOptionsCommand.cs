using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Coefficients;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.ClassSubjects.Commands;

/// <summary>
/// POST /api/v1/class-subjects/assign-default-options — donne l'option par défaut de chaque groupe aux élèves de
/// la classe qui n'en ont pas encore pour l'année active (Évolution N°6). Utile pour une classe déjà peuplée au
/// moment où ses groupes d'options sont créés : sans choix, un élève n'apparaît dans aucune grille de saisie du
/// groupe. Ne modifie JAMAIS un choix existant. Idempotente.
/// </summary>
public record AssignDefaultOptionsCommand(Guid ClassroomId) : IRequest<AssignDefaultOptionsResult>, IAuditableRequest;

/// <param name="StudentsUpdated">Élèves qui ont reçu au moins une option.</param>
/// <param name="OptionsAssigned">Options attribuées au total.</param>
public record AssignDefaultOptionsResult(int StudentsUpdated, int OptionsAssigned);

public class AssignDefaultOptionsCommandValidator : AbstractValidator<AssignDefaultOptionsCommand>
{
    public AssignDefaultOptionsCommandValidator() => RuleFor(x => x.ClassroomId).NotEmpty();
}

public class AssignDefaultOptionsCommandHandler(
    IApplicationDbContext dbContext, ITenantProvider tenantProvider, ICurrentUserService currentUser)
    : IRequestHandler<AssignDefaultOptionsCommand, AssignDefaultOptionsResult>
{
    public async Task<AssignDefaultOptionsResult> Handle(AssignDefaultOptionsCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        if (!await dbContext.Classrooms.AnyAsync(c => c.Id == request.ClassroomId, cancellationToken))
        {
            throw new KeyNotFoundException($"Classe {request.ClassroomId} introuvable.");
        }

        var yearId = await CoefficientRules.ActiveSchoolYearIdAsync(dbContext, cancellationToken);
        var groups = await ClassOptionCatalog.LoadAsync(dbContext, request.ClassroomId, yearId, null, cancellationToken);
        if (groups.Count == 0)
        {
            return new AssignDefaultOptionsResult(0, 0);
        }

        var optionIds = groups.SelectMany(g => g.Options).Select(o => o.ClassSubjectId).ToHashSet();

        var studentIds = await dbContext.Students.AsNoTracking()
            .Where(s => s.ClassroomId == request.ClassroomId)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        var choices = (await dbContext.StudentSubjectEnrollments.AsNoTracking()
                .Where(e => e.SchoolYearId == yearId && studentIds.Contains(e.StudentId))
                .Select(e => new { e.StudentId, e.ClassSubjectId })
                .ToListAsync(cancellationToken))
            .Where(e => optionIds.Contains(e.ClassSubjectId))
            .ToLookup(e => e.StudentId, e => e.ClassSubjectId);

        var actor = currentUser.UserId?.ToString() ?? "system";
        int studentsUpdated = 0, assigned = 0;

        foreach (var studentId in studentIds)
        {
            var current = choices[studentId].ToList();
            if (current.Count == groups.Count)
            {
                continue;
            }

            // Les choix existants sont renvoyés tels quels : seul un groupe vide reçoit l'option par défaut.
            var final = await StudentOptionWriter.SetAsync(
                dbContext, schoolId, actor, studentId, yearId, groups, current, fillDefaults: true, cancellationToken);

            studentsUpdated++;
            assigned += final.Count - current.Count;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new AssignDefaultOptionsResult(studentsUpdated, assigned);
    }
}
