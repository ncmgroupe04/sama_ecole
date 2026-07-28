using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Platform.Queries.GetPromoCodes;

/// <summary>
/// GET /admin/promo-codes — tableau de bord des offres promotionnelles (Super Admin). Liste complète,
/// non paginée, comme GetPlatformSubscriptionsQuery : le nombre de codes promo reste modeste, le
/// filtrage (actif/expiré) se fait côté client.
/// </summary>
public record GetPromoCodesQuery : IRequest<IReadOnlyList<PromoCodeDto>>;

public record PromoCodeDto(
    Guid Id,
    string Code,
    string DiscountType,
    decimal DiscountValue,
    int? DurationMonths,
    int? MaxUses,
    int CurrentUses,
    DateTime StartDateUtc,
    DateTime EndDateUtc,
    bool IsActive,
    IReadOnlyList<string> BeneficiarySchoolNames);

public class GetPromoCodesQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetPromoCodesQuery, IReadOnlyList<PromoCodeDto>>
{
    public async Task<IReadOnlyList<PromoCodeDto>> Handle(
        GetPromoCodesQuery request, CancellationToken cancellationToken)
    {
        var promoCodes = await dbContext.PromoCodes
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        // `subscriptions`/`schools` ne portent aucune navigation l'une vers l'autre (FK explicites
        // uniquement, voir SubscriptionConfiguration) : jointure manuelle, comme partout ailleurs dans
        // le module Platform (GetPlatformSubscriptionsQuery passe par une vue dédiée pour la même raison).
        //
        // IgnoreQueryFilters : lecture VOLONTAIREMENT hors cloisonnement tenant — le Super Admin n'a
        // aucun SchoolId, et le Global Query Filter de Subscription (ITenantEntity) le réduirait sinon
        // à `SchoolId == null`, donc à zéro bénéficiaire. Même idiome que GetGlobalAuditLogsAsync ;
        // la garde réelle reste [Authorize(SuperAdmin)] au contrôleur.
        var beneficiaries = await (
            from s in dbContext.Subscriptions.AsNoTracking().IgnoreQueryFilters()
            where s.PromoCodeId != null && !s.IsDeleted
            join school in dbContext.Schools.AsNoTracking() on s.SchoolId equals school.Id
            select new { s.PromoCodeId, school.Name })
            .ToListAsync(cancellationToken);

        var beneficiariesByPromoCode = beneficiaries
            .GroupBy(b => b.PromoCodeId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(b => b.Name).ToList());

        return promoCodes
            .Select(p => new PromoCodeDto(
                p.Id,
                p.Code,
                p.DiscountType.ToString(),
                p.DiscountValue,
                p.DurationMonths,
                p.MaxUses,
                p.CurrentUses,
                p.StartDateUtc,
                p.EndDateUtc,
                p.IsActive,
                beneficiariesByPromoCode.GetValueOrDefault(p.Id, [])))
            .ToList();
    }
}
