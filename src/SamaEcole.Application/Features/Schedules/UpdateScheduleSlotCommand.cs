using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;

using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Features.Schedules;

//[Authorize(Roles = "SuperAdmin, Directeur, Secretariat, Enseignant")]
public record UpdateScheduleSlotCommand(
    Guid Id,
    Guid TeacherId,
    Guid ClassroomId,
    Guid SubjectId,
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string? RoomNumber) : IRequest;

public class UpdateScheduleSlotCommandValidator : AbstractValidator<UpdateScheduleSlotCommand>
{
    public UpdateScheduleSlotCommandValidator()
    {
        RuleFor(v => v.Id).NotEmpty();
        RuleFor(v => v.TeacherId).NotEmpty();
        RuleFor(v => v.ClassroomId).NotEmpty();
        RuleFor(v => v.SubjectId).NotEmpty();
        RuleFor(v => v.DayOfWeek).IsInEnum();
        RuleFor(v => v.StartTime).NotEmpty();
        RuleFor(v => v.EndTime).GreaterThan(v => v.StartTime).WithMessage("EndTime must be after StartTime.");
        RuleFor(v => v.RoomNumber).MaximumLength(50);
    }
}

public class UpdateScheduleSlotCommandHandler(
    IApplicationDbContext context,
    ICurrentUserService currentUserService,
    ScheduleOwnershipAuthorizer ownershipAuthorizer) : IRequestHandler<UpdateScheduleSlotCommand>
{
    public async Task Handle(UpdateScheduleSlotCommand request, CancellationToken cancellationToken)
    {
        var slot = await context.ScheduleSlots.FindAsync([request.Id], cancellationToken)
            ?? throw new NotFoundException(nameof(ScheduleSlot), request.Id);

        var isTeacher = currentUserService.Role == SamaEcole.Domain.Enums.Role.Enseignant;

        // Un Enseignant ne modifie QUE ses propres créneaux, et ne peut pas les réattribuer à un
        // collègue : le propriétaire actuel (slot.TeacherId) ET le propriétaire demandé sont tous les
        // deux confrontés à sa fiche (règle #10). Sans ce contrôle, le verrou posé à la création serait
        // contournable en deux appels — créer pour soi, puis réattribuer.
        var teacherId = await ownershipAuthorizer.EnsureOwnsAsync(
            request.TeacherId, currentSlotTeacherId: slot.TeacherId, cancellationToken);

        var overlappingSlot = await context.ScheduleSlots
            .Where(s => s.Id != request.Id) // Exclude current slot
            .Where(s => s.DayOfWeek == request.DayOfWeek
                     && s.StartTime < request.EndTime
                     && s.EndTime > request.StartTime)
            .Where(s => s.TeacherId == teacherId || s.ClassroomId == request.ClassroomId)
            .FirstOrDefaultAsync(cancellationToken);

        if (overlappingSlot != null)
        {
            var errorMessage = overlappingSlot.TeacherId == teacherId
                ? "L'enseignant a déjà cours sur cette plage horaire."
                : "La classe a déjà cours sur cette plage horaire.";

            throw new SamaEcole.Application.Common.Exceptions.ValidationException(new[] {
                new FluentValidation.Results.ValidationFailure("global", errorMessage)
            });
        }

        slot.TeacherId = teacherId;
        slot.ClassroomId = request.ClassroomId;
        slot.SubjectId = request.SubjectId;
        slot.DayOfWeek = request.DayOfWeek;
        slot.StartTime = request.StartTime;
        slot.EndTime = request.EndTime;
        slot.RoomNumber = request.RoomNumber;

        // Si l'enseignant modifie, on met IsTeacherSubmitted à true
        if (isTeacher)
        {
            slot.IsTeacherSubmitted = true;
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
