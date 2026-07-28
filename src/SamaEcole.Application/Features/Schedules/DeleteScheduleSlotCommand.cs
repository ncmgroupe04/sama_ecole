using MediatR;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;

using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Features.Schedules;

//[Authorize(Roles = "SuperAdmin, Directeur, Secretariat, Enseignant")]
public record DeleteScheduleSlotCommand(Guid Id) : IRequest;

public class DeleteScheduleSlotCommandHandler(
    IApplicationDbContext context,
    ICurrentUserService currentUserService,
    ScheduleOwnershipAuthorizer ownershipAuthorizer) : IRequestHandler<DeleteScheduleSlotCommand>
{
    public async Task Handle(DeleteScheduleSlotCommand request, CancellationToken cancellationToken)
    {
        var slot = await context.ScheduleSlots.FindAsync([request.Id], cancellationToken)
            ?? throw new NotFoundException(nameof(ScheduleSlot), request.Id);

        // Un Enseignant ne supprime que ses propres créneaux : interdire la modification tout en
        // laissant la suppression ouverte reviendrait à laisser supprimer le cours d'un collègue.
        await ownershipAuthorizer.EnsureOwnsAsync(
            slot.TeacherId, currentSlotTeacherId: slot.TeacherId, cancellationToken);

        slot.SoftDelete(currentUserService.UserId?.ToString() ?? "system");
        await context.SaveChangesAsync(cancellationToken);
    }
}
