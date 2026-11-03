using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Boarding.Leaves.DeleteLeave;

/// <summary>
/// DELETE /api/v1/boarding/leaves/{id}?rowVersion= — annule une sortie déclarée par ERREUR (mauvais élève, mauvaise date) :
/// elle est archivée et disparaît du registre. Suppression logique uniquement (règle #6). Une sortie ouverte, en retard ou déjà
/// rentrée peut être annulée ; l'élève n'est plus considéré en sortie, ce qui lève un éventuel blocage de « Libérer ».
/// </summary>
public record DeleteLeaveCommand(Guid Id, uint RowVersion) : IRequest<Unit>, IAuditableRequest;

public class DeleteLeaveCommandValidator : AbstractValidator<DeleteLeaveCommand>
{
    public DeleteLeaveCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public class DeleteLeaveCommandHandler(IApplicationDbContext dbContext, ICurrentUserService currentUser)
    : IRequestHandler<DeleteLeaveCommand, Unit>
{
    public async Task<Unit> Handle(DeleteLeaveCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        var leave = await dbContext.BoardingLeaves
            .FirstOrDefaultAsync(l => l.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Sortie {request.Id} introuvable.");

        dbContext.SetOriginalConcurrencyToken(leave, request.RowVersion);

        leave.SoftDelete(actorId.ToString());

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
