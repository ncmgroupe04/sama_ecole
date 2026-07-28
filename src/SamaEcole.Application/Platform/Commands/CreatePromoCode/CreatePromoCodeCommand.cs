using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Platform.Commands.CreatePromoCode;

/// <summary>
/// POST /admin/promo-codes — module Tarification, Réductions &amp; Offres Promotionnelles. Réservé au
/// Super Admin. `promo_codes` est une table PLATEFORME (aucun SchoolId, aucune policy RLS, voir
/// PromoCodeConfiguration) : un INSERT EF classique suffit, contrairement à `subscriptions`.
/// </summary>
public record CreatePromoCodeCommand : IRequest<CreatePromoCodeResult>
{
    public required string Code { get; init; }
    public required PromoDiscountType DiscountType { get; init; }
    public decimal DiscountValue { get; init; }
    public int? DurationMonths { get; init; }
    public int? MaxUses { get; init; }
    public required DateTime StartDateUtc { get; init; }
    public required DateTime EndDateUtc { get; init; }
}

public record CreatePromoCodeResult(Guid Id, string Code);

public class CreatePromoCodeCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<CreatePromoCodeCommand, CreatePromoCodeResult>
{
    public async Task<CreatePromoCodeResult> Handle(CreatePromoCodeCommand request, CancellationToken cancellationToken)
    {
        // Normalisé en amont de l'unicité (le Validator, lui, ne voit que la saisie brute) : deux
        // Super Admin qui créeraient "bienvenue2026" et "BIENVENUE2026" ne doivent PAS produire deux
        // codes distincts pour l'école qui les saisirait au clavier.
        var code = request.Code.Trim().ToUpperInvariant();

        var promoCode = new PromoCode
        {
            Code = code,
            DiscountType = request.DiscountType,
            DiscountValue = request.DiscountValue,
            DurationMonths = request.DurationMonths,
            MaxUses = request.MaxUses,
            CurrentUses = 0,
            StartDateUtc = request.StartDateUtc,
            EndDateUtc = request.EndDateUtc,
            IsActive = true
        };

        dbContext.PromoCodes.Add(promoCode);

        // Une violation d'unicité sur Code est traduite en ConcurrencyConflictException (409) par
        // ApplicationDbContext.SaveChangesAsync — aucun contrôle d'existence préalable nécessaire ici.
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreatePromoCodeResult(promoCode.Id, promoCode.Code);
    }
}
