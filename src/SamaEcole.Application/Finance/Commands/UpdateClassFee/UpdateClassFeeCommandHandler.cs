using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Commands.UpdateClassFee;

public class UpdateClassFeeCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateClassFeeCommand, UpdateClassFeeResult>
{
    public async Task<UpdateClassFeeResult> Handle(UpdateClassFeeCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Le Global Query Filter + la policy RLS bornent la recherche à l'école courante : viser une
        // ligne de barème d'une autre école renvoie 404, jamais une modification silencieuse.
        var fee = await dbContext.ClassFees
            .FirstOrDefaultAsync(f => f.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Ligne de barème {request.Id} introuvable.");

        // Montant inchangé : ne rien écrire. Réécrire polluerait l'historique d'une entrée vide, et
        // le verrou optimiste n'a rien à arbitrer puisque la valeur ne bouge pas.
        if (fee.Amount == request.Amount)
        {
            return new UpdateClassFeeResult(fee.Id, fee.Amount, request.RowVersion);
        }

        // Cœur du verrou optimiste : on impose à EF la version LUE PAR LE CLIENT comme valeur
        // d'origine du jeton xmin. Si la ligne a changé en base depuis l'affichage, l'UPDATE
        // « WHERE xmin = <version client> » ne touchera aucune ligne et SaveChangesAsync lèvera une
        // ConcurrencyConflictException → 409 (AGENTS.md règle #5).
        dbContext.SetOriginalConcurrencyToken(fee, request.RowVersion);

        var previousAmount = fee.Amount;
        fee.Amount = request.Amount;

        dbContext.FeeChangeHistory.Add(new FeeChangeHistory
        {
            SchoolId = schoolId,
            ClassFeeId = fee.Id,
            OldAmount = previousAmount,
            NewAmount = request.Amount,
            ChangedByUserId = actorId,
            ChangedAt = timeProvider.GetUtcNow()
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        // Nouveau jeton après écriture : PostgreSQL a réécrit xmin (tout UPDATE le fait). On le relit
        // pour que le client puisse enchaîner une seconde modification sans recharger toute la grille.
        var newRowVersion = await dbContext.ClassFees
            .AsNoTracking()
            .Where(f => f.Id == fee.Id)
            .Select(f => EF.Property<uint>(f, "xmin"))
            .FirstAsync(cancellationToken);

        return new UpdateClassFeeResult(fee.Id, fee.Amount, newRowVersion);
    }
}
