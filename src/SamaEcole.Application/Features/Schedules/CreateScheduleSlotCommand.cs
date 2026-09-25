using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Schools;

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
    ICurrentUserService currentUserService,
    ScheduleOwnershipAuthorizer ownershipAuthorizer,
    WorkingDayGuard workingDayGuard) : IRequestHandler<CreateScheduleSlotCommand, Guid>
{
    public async Task<Guid> Handle(CreateScheduleSlotCommand request, CancellationToken cancellationToken)
    {
        var isTeacher = currentUserService.Role == SamaEcole.Domain.Enums.Role.Enseignant;

        // Un Enseignant ne propose de créneau QUE pour lui-même : le TeacherId du corps de requête est
        // rapproché de SA fiche, jamais accepté sur parole (règle #10). Pas de créneau existant à la
        // création, d'où le null. Voir ScheduleOwnershipAuthorizer.
        var teacherId = await ownershipAuthorizer.EnsureOwnsAsync(
            request.TeacherId, currentSlotTeacherId: null, cancellationToken);

        // Jour de repos de l'établissement (Évolution N°3) : aucun créneau n'y est placé.
        await workingDayGuard.EnsureWorkingDayAsync(request.DayOfWeek, nameof(request.DayOfWeek), cancellationToken);

        // Enseignant, classe ET salle (Évolution N°7 : la salle n'était jusqu'ici jamais comparée).
        var overlapMessage = await ScheduleOverlapGuard.FindOverlapAsync(
            context, excludedSlotId: null, request.DayOfWeek, request.StartTime, request.EndTime,
            teacherId, request.ClassroomId, request.RoomNumber, cancellationToken);

        if (overlapMessage != null)
        {
            throw new ValidationException(new[] {
                new FluentValidation.Results.ValidationFailure("global", overlapMessage)
            });
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
