using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Subscriptions.Pricing;

/// <summary>
/// PUT /admin/pricing-grid/{tier} — le Super Admin ajuste le prix d'une tranche. Réservé au Super Admin
/// (`quota_tier_prices` est une table plateforme). Le changement vaut pour les NOUVELLES demandes et les
/// prochains paiements : un paiement déjà initié garde son montant (relu par le webhook).
/// </summary>
public record UpdateQuotaTierPriceCommand : IRequest<AdminPricingGridTierDto>
{
    public required StudentQuotaTier Tier { get; init; }
    public required decimal MonthlyXof { get; init; }
    public required decimal AnnualXof { get; init; }
    public decimal BoardingSurchargeMonthlyXof { get; init; }
    public decimal BoardingSurchargeAnnualXof { get; init; }
}

public class UpdateQuotaTierPriceCommandValidator : AbstractValidator<UpdateQuotaTierPriceCommand>
{
    /// <summary>Garde-fou contre une faute de frappe (un zéro de trop) ; une tranche standard reste très en deçà.</summary>
    public const decimal MaxAmountXof = 50_000_000m;

    private const string WholeFrancsMessage = "Le montant doit être un nombre entier de FCFA.";

    public UpdateQuotaTierPriceCommandValidator()
    {
        RuleFor(c => c.Tier)
            .IsInEnum()
            .Must(t => !Enum.IsDefined(t) || !QuotaTierResolver.RequiresQuote(t))
            .WithMessage("Cette tranche est sur devis : elle n'a pas de prix publié.");

        RuleFor(c => c.MonthlyXof).GreaterThan(0).LessThanOrEqualTo(MaxAmountXof).Must(BeWholeFrancs).WithMessage(WholeFrancsMessage);
        RuleFor(c => c.AnnualXof).GreaterThan(0).LessThanOrEqualTo(MaxAmountXof).Must(BeWholeFrancs).WithMessage(WholeFrancsMessage);
        RuleFor(c => c.BoardingSurchargeMonthlyXof).GreaterThanOrEqualTo(0).LessThanOrEqualTo(MaxAmountXof).Must(BeWholeFrancs).WithMessage(WholeFrancsMessage);
        RuleFor(c => c.BoardingSurchargeAnnualXof).GreaterThanOrEqualTo(0).LessThanOrEqualTo(MaxAmountXof).Must(BeWholeFrancs).WithMessage(WholeFrancsMessage);

        // L'annuel est une remise d'engagement : plus cher que 12 mensualités, c'est une erreur de saisie.
        RuleFor(c => c.AnnualXof)
            .LessThanOrEqualTo(c => c.MonthlyXof * 12)
            .WithMessage("Le tarif annuel ne peut pas dépasser 12 mensualités.");
        RuleFor(c => c.BoardingSurchargeAnnualXof)
            .LessThanOrEqualTo(c => c.BoardingSurchargeMonthlyXof * 12)
            .WithMessage("La majoration annuelle ne peut pas dépasser 12 majorations mensuelles.");
    }

    private static bool BeWholeFrancs(decimal amount) => amount == decimal.Truncate(amount);
}

public class UpdateQuotaTierPriceCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser,
    ILogger<UpdateQuotaTierPriceCommandHandler> logger)
    : IRequestHandler<UpdateQuotaTierPriceCommand, AdminPricingGridTierDto>
{
    public async Task<AdminPricingGridTierDto> Handle(UpdateQuotaTierPriceCommand request, CancellationToken cancellationToken)
    {
        var superAdminId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Aucun utilisateur authentifié.");

        var row = await dbContext.QuotaTierPrices.FirstOrDefaultAsync(p => p.Tier == request.Tier, cancellationToken)
            ?? throw new NotFoundException($"Aucun tarif n'existe pour la tranche {request.Tier}.");

        var before = (row.MonthlyXof, row.AnnualXof, row.BoardingSurchargeMonthlyXof, row.BoardingSurchargeAnnualXof);

        row.MonthlyXof = request.MonthlyXof;
        row.AnnualXof = request.AnnualXof;
        row.BoardingSurchargeMonthlyXof = request.BoardingSurchargeMonthlyXof;
        row.BoardingSurchargeAnnualXof = request.BoardingSurchargeAnnualXof;
        row.UpdatedBy = superAdminId.ToString();

        // Un conflit xmin (deux Super Admin simultanés) devient une ConcurrencyConflictException (409) dans SaveChangesAsync.
        await dbContext.SaveChangesAsync(cancellationToken);

        // Pas d'entrée audit_logs : ce journal est rattaché à une école, et un prix n'appartient à aucune.
        // La trace est UpdatedAt/UpdatedBy sur la ligne, plus ce journal applicatif (avant → après).
        logger.LogInformation(
            "Super Admin {SuperAdminId} a modifié le tarif de la tranche {Tier} : " +
            "(mensuel, annuel, majoration Internat mensuelle, annuelle) {Before} → {After}.",
            superAdminId, request.Tier, before,
            (row.MonthlyXof, row.AnnualXof, row.BoardingSurchargeMonthlyXof, row.BoardingSurchargeAnnualXof));

        return PricingGridMapper.ToAdminDto(row);
    }
}
