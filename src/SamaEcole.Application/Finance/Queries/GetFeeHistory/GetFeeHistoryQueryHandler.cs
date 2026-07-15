using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetFeeHistory;

public class GetFeeHistoryQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetFeeHistoryQuery, IReadOnlyList<FeeHistoryDto>>
{
    public async Task<IReadOnlyList<FeeHistoryDto>> Handle(
        GetFeeHistoryQuery request, CancellationToken cancellationToken)
    {
        // Jointure GAUCHE vers les comptes : l'auteur d'un changement pourrait avoir été archivé
        // depuis (soft delete), et son entrée d'audit doit rester lisible malgré tout — l'histoire ne
        // s'efface pas parce qu'un compte a été fermé. Sans le left join, ces lignes disparaîtraient.
        return await (
            from h in dbContext.FeeChangeHistory.AsNoTracking()
            where h.ClassFeeId == request.ClassFeeId
            join u in dbContext.Users on h.ChangedByUserId equals u.Id into authors
            from author in authors.DefaultIfEmpty()
            orderby h.ChangedAt descending
            select new FeeHistoryDto(
                h.OldAmount,
                h.NewAmount,
                h.ChangedByUserId,
                author != null ? author.FullName : "Compte supprimé",
                h.ChangedAt))
            .ToListAsync(cancellationToken);
    }
}
