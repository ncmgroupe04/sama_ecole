using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.Internat.Commands.AssignInstructor;

/// <summary>
/// POST /api/v1/internat/students/assign-instructor — rattache un lot d'élèves à la Halqa d'un Oustaz, ou les en
/// détache (<see cref="InstructorId"/> null). Réservé au Directeur (InternatController). N'écrit QUE
/// <c>Student.InstructorId</c> : la classe (<c>ClassroomId</c>, axe administratif et de caisse) ne bouge jamais.
///
/// Tout ou rien : un seul SaveChanges pour tout le lot. Un élève déjà dans la bonne Halqa n'est pas réécrit
/// (idempotent : rejouer la commande ne change rien).
/// </summary>
public record AssignInstructorCommand(Guid? InstructorId, IReadOnlyList<Guid> StudentIds)
    : IRequest<AssignInstructorResult>, IAuditableRequest;

/// <param name="InstructorId">Halqa demandée (null = détachement).</param>
/// <param name="AssignedCount">Élèves dont la Halqa a effectivement changé.</param>
/// <param name="UnchangedCount">Élèves qui étaient déjà dans la Halqa demandée.</param>
public record AssignInstructorResult(Guid? InstructorId, int AssignedCount, int UnchangedCount);

public class AssignInstructorCommandValidator : AbstractValidator<AssignInstructorCommand>
{
    public AssignInstructorCommandValidator()
    {
        RuleFor(c => c.InstructorId).NotEqual(Guid.Empty).When(c => c.InstructorId is not null);
        RuleFor(c => c.StudentIds)
            .NotEmpty().WithMessage("Indiquez au moins un élève.")
            .Must(ids => ids.Count <= HizbRules.MaxStudentsPerAssignment)
            .WithMessage($"Une affectation porte sur {HizbRules.MaxStudentsPerAssignment} élèves au maximum.");
        RuleForEach(c => c.StudentIds).NotEmpty();
    }
}

public class AssignInstructorCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<AssignInstructorCommand, AssignInstructorResult>
{
    public async Task<AssignInstructorResult> Handle(AssignInstructorCommand request, CancellationToken cancellationToken)
    {
        // Global Query Filter + RLS : l'Oustaz d'une autre école est structurellement introuvable (404).
        if (request.InstructorId is { } instructorId)
        {
            var instructor = await dbContext.Instructors
                .AsNoTracking()
                .Where(i => i.Id == instructorId)
                .Select(i => new { i.Status })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException($"Oustaz {instructorId} introuvable.");

            // On ne confie pas de nouveaux élèves à un Oustaz suspendu ou bloqué. Le détachement (null) reste toujours permis.
            if (instructor.Status != EntityStatus.Active)
            {
                throw new ValidationException([
                    new ValidationFailure(nameof(request.InstructorId), "Cet Oustaz n'est pas actif : réactivez-le avant de lui affecter des élèves.")
                ]);
            }
        }

        var ids = request.StudentIds.Distinct().ToList();

        var students = await dbContext.Students
            .Where(s => ids.Contains(s.Id))
            .ToListAsync(cancellationToken);

        // Un id hors tenant est introuvable : on refuse TOUT le lot plutôt que d'en affecter la moitié en silence.
        // Le message ne nomme aucun id (pas d'énumération de ce qui existe ailleurs).
        if (students.Count != ids.Count)
        {
            throw new KeyNotFoundException("Un ou plusieurs élèves sont introuvables dans votre établissement. Aucune affectation n'a été faite.");
        }

        var changed = 0;
        foreach (var student in students.Where(s => s.InstructorId != request.InstructorId))
        {
            student.InstructorId = request.InstructorId;
            changed++;
        }

        if (changed > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new AssignInstructorResult(request.InstructorId, changed, students.Count - changed);
    }
}
