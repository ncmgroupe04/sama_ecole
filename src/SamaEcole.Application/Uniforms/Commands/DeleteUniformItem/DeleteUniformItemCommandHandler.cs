using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Uniforms.Commands.DeleteUniformItem;

public class DeleteUniformItemCommandHandler(IApplicationDbContext dbContext, ICurrentUserService currentUser)
    : IRequestHandler<DeleteUniformItemCommand, Unit>
{
    public async Task<Unit> Handle(DeleteUniformItemCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        var item = await dbContext.UniformItems.FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Modèle de tenue {request.Id} introuvable.");

        // Archiver un modèle dont des élèves attendent encore la tenue ferait disparaître leur besoin de la liste.
        var hasPendingNeed = await dbContext.StudentUniforms.AnyAsync(
            u => u.UniformItemId == item.Id && u.Status == StudentUniformStatus.Requested, cancellationToken);

        if (hasPendingNeed)
        {
            throw new BusinessRuleException(
                "Impossible d'archiver ce modèle : des élèves attendent encore cette tenue. Annulez ou remettez ces besoins d'abord.",
                "UNIFORM_ITEM_IN_USE");
        }

        dbContext.SetOriginalConcurrencyToken(item, request.RowVersion);

        item.SoftDelete(actorId.ToString());

        // Ses tailles partent avec lui, dans la même transaction. Les LOTS d'inventaire restent actifs : archiver un
        // modèle n'efface pas le stock physique ni son journal (append-only).
        var variants = await dbContext.UniformVariants.Where(v => v.UniformItemId == item.Id).ToListAsync(cancellationToken);

        foreach (var variant in variants)
        {
            variant.SoftDelete(actorId.ToString());
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
