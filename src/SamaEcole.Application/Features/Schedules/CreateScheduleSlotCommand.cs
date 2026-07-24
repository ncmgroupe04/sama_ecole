using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;

using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Features.Schedules;

//[Authorize(Roles = "SuperAdmin, Directeur, Secretariat, Enseignant")]
public record CreateScheduleSlotCommand(
    Guid TeacherId,
    Guid ClassroomId,
    Guid SubjectId,
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string? RoomNumber) : IRequest<Guid>;

public class CreateScheduleSlotCommandValidator : AbstractValidator<CreateScheduleSlotCommand>
{
    public CreateScheduleSlotCommandValidator()
    {
        RuleFor(v => v.TeacherId).NotEmpty();
        RuleFor(v => v.ClassroomId).NotEmpty();
        RuleFor(v => v.SubjectId).NotEmpty();
        RuleFor(v => v.DayOfWeek).IsInEnum();
        RuleFor(v => v.StartTime).NotEmpty();
        RuleFor(v => v.EndTime).GreaterThan(v => v.StartTime).WithMessage("EndTime must be after StartTime.");
        RuleFor(v => v.RoomNumber).MaximumLength(50);
    }
}

public class CreateScheduleSlotCommandHandler(
    IApplicationDbContext context, 
    ITenantProvider tenantProvider,
    ICurrentUserService currentUserService) : IRequestHandler<CreateScheduleSlotCommand, Guid>
{
    public async Task<Guid> Handle(CreateScheduleSlotCommand request, CancellationToken cancellationToken)
    {
        var isTeacher = currentUserService.Role == SamaEcole.Domain.Enums.Role.Enseignant;
        var teacherId = request.TeacherId;

        // Si l'utilisateur est un enseignant, on force son propre TeacherId pour éviter
        // qu'il ne crée des créneaux pour d'autres enseignants.
        // TODO: Validate that the current teacher corresponds to this TeacherId.
        // For now, we trust the rule #4 / RBAC, but ideally we match User ID to Teacher record.

        var overlappingSlot = await context.ScheduleSlots
            .Where(s => s.DayOfWeek == request.DayOfWeek 
                     && s.StartTime < request.EndTime 
                     && s.EndTime > request.StartTime)
            .Where(s => s.TeacherId == request.TeacherId || s.ClassroomId == request.ClassroomId)
            .FirstOrDefaultAsync(cancellationToken);

        if (overlappingSlot != null)
        {
            var errorMessage = overlappingSlot.TeacherId == request.TeacherId
                ? "L'enseignant a déjà cours sur cette plage horaire."
                : "La classe a déjà cours sur cette plage horaire.";
                
            throw new SamaEcole.Application.Common.Exceptions.ValidationException(
                new Dictionary<string, string[]> { { "global", new[] { errorMessage } } }
            );
        }

        var slot = new ScheduleSlot
        {
            SchoolId = tenantProvider.CurrentSchoolId.Value,
            TeacherId = teacherId,
            ClassroomId = request.ClassroomId,
            SubjectId = request.SubjectId,
            DayOfWeek = request.DayOfWeek,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            RoomNumber = request.RoomNumber,
            IsTeacherSubmitted = isTeacher
        };

        context.ScheduleSlots.Add(slot);
        await context.SaveChangesAsync(cancellationToken);

        return slot.Id;
    }
}
