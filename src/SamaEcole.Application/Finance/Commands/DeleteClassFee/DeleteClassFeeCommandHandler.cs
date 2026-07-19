using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Commands.DeleteClassFee;

public class DeleteClassFeeCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser)
    : IRequestHandler<DeleteClassFeeCommand, Unit>
{
    public async Task<Unit> Handle(DeleteClassFeeCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Le Global Query Filter + la policy RLS bornent la recherche à l'école courante : viser une
        // ligne de barème d'une autre école renvoie 404, jamais une suppression silencieuse.
        var fee = await dbContext.ClassFees
            .FirstOrDefaultAsync(f => f.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Ligne de barème {request.Id} introuvable.");

        // Même verrou optimiste que UpdateClassFeeCommandHandler (AGENTS.md règle #5) : une ligne
        // modifiée entre-temps par un autre utilisateur ne doit jamais disparaître en silence.
        dbContext.SetOriginalConcurrencyToken(fee, request.RowVersion);

        fee.SoftDelete(actorId.ToString());

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
