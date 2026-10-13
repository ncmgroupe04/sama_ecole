using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Subscriptions.Pricing;

/// <param name="Tier">Identifiant de l'API (<c>StudentQuotaTier</c>), jamais un libellé.</param>
/// <param name="MaxStudents">Plafond nominal de la tranche.</param>
/// <param name="SoftLimit">Dernier effectif toléré avec avertissement ; au-delà, les nouvelles inscriptions sont refusées.</param>
public sealed record PricingGridTierDto(
    string Tier,
    int MaxStudents,
    int SoftLimit,
    decimal MonthlyXof,
    decimal AnnualXof,
    decimal BoardingSurchargeMonthlyXof,
    decimal BoardingSurchargeAnnualXof);

/// <summary>
/// Grille publique. <paramref name="QuoteAboveStudents"/> : au-delà de cet effectif il n'y a plus de prix,
/// l'offre « Grand Groupe » se négocie.
/// </summary>
public sealed record PublicPricingGridDto(
    string Currency,
    int QuoteAboveStudents,
    IReadOnlyList<PricingGridTierDto> Tiers);

/// <summary>Ligne de la grille côté Super Admin : mêmes montants, plus la trace de la dernière modification.</summary>
public sealed record AdminPricingGridTierDto(
    PricingGridTierDto Price,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy);

public static class PricingGridMapper
{
    public static PricingGridTierDto ToDto(TierPrice price)
    {
        var (max, soft) = StudentQuotaDefaults.For(price.Tier);

        return new PricingGridTierDto(
            price.Tier.ToString(), max, soft,
            price.MonthlyXof, price.AnnualXof, price.BoardingSurchargeMonthlyXof, price.BoardingSurchargeAnnualXof);
    }

    public static AdminPricingGridTierDto ToAdminDto(QuotaTierPrice row) =>
        new(ToDto(SubscriptionPricingEngine.ToTierPrice(row)), row.UpdatedAt, row.UpdatedBy);
}

/// <summary>
/// GET /api/v1/public/pricing-grid — ANONYME : le formulaire d'inscription affiche la grille avant que
/// l'école n'existe. Données non sensibles (prix publiés) ; rien d'une école n'y figure.
/// </summary>
public record GetPublicPricingGridQuery : IRequest<PublicPricingGridDto>;

public class GetPublicPricingGridQueryHandler(SubscriptionPricingEngine engine)
    : IRequestHandler<GetPublicPricingGridQuery, PublicPricingGridDto>
{
    public async Task<PublicPricingGridDto> Handle(GetPublicPricingGridQuery request, CancellationToken cancellationToken)
    {
        var grid = await engine.GetGridAsync(cancellationToken);
        var quoteAbove = QuotaTierPricing.PricedTiers().Select(t => StudentQuotaDefaults.For(t).Max).Max();

        return new PublicPricingGridDto("XOF", quoteAbove, grid.Select(PricingGridMapper.ToDto).ToList());
    }
}

/// <summary>GET /api/v1/admin/pricing-grid — Super Admin : la grille et qui l'a modifiée en dernier.</summary>
public record GetAdminPricingGridQuery : IRequest<IReadOnlyList<AdminPricingGridTierDto>>;

public class GetAdminPricingGridQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetAdminPricingGridQuery, IReadOnlyList<AdminPricingGridTierDto>>
{
    public async Task<IReadOnlyList<AdminPricingGridTierDto>> Handle(
        GetAdminPricingGridQuery request, CancellationToken cancellationToken)
    {
        var rows = await dbContext.QuotaTierPrices.AsNoTracking().ToListAsync(cancellationToken);

        return SubscriptionPricingEngine.Order(rows).Select(PricingGridMapper.ToAdminDto).ToList();
    }
}
