using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Services;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Finance.Commands.GenerateTaxDeclaration;

public record GenerateTaxDeclarationCommand(int Month, int Year) : IRequest<Guid>;

public class GenerateTaxDeclarationCommandValidator : AbstractValidator<GenerateTaxDeclarationCommand>
{
    public GenerateTaxDeclarationCommandValidator()
    {
        RuleFor(v => v.Month).InclusiveBetween(1, 12);
        RuleFor(v => v.Year).GreaterThan(2000);
    }
}

public class GenerateTaxDeclarationCommandHandler(
    IApplicationDbContext context,
    ITenantProvider tenantProvider) : IRequestHandler<GenerateTaxDeclarationCommand, Guid>
{
    public async Task<Guid> Handle(GenerateTaxDeclarationCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId ?? throw new UnauthorizedAccessException();

        // 1. Check if already generated
        var existing = await context.TaxeDeclarations
            .AnyAsync(t => t.Month == request.Month && t.Year == request.Year, cancellationToken);
            
        if (existing)
            throw new BusinessRuleException("Une déclaration fiscale existe déjà pour cette période.");

        // 2. Fetch all FichePaies for the period
        var fiches = await context.FichePaies
            .Where(f => f.Month == request.Month && f.Year == request.Year)
            .ToListAsync(cancellationToken);

        // 3. TVA collectée : somme de VatAmount des encaissements du mois (Payment.VatRate/VatAmount,
        // renseignés transaction par transaction à l'encaissement — voir RecordPaymentCommandHandler).
        // Un paiement Cancelled n'a jamais représenté de l'argent réellement encaissé (même exclusion que
        // le tableau de bord Trésorerie) ; Status == Partial, lui, EST un encaissement réel, donc inclus.
        var startInclusive = new DateTimeOffset(new DateTime(request.Year, request.Month, 1), TimeSpan.Zero);
        var endExclusive = startInclusive.AddMonths(1);

        decimal tvaCollected = await context.Payments
            .Where(p => p.Status != PaymentStatus.Cancelled && p.PaidAt >= startInclusive && p.PaidAt < endExclusive)
            .SumAsync(p => p.VatAmount, cancellationToken);

        // 4. TVA déductible : somme de VatAmount des décaissements du mois (Disbursement.VatRate/VatAmount).
        var periodStart = DateOnly.FromDateTime(startInclusive.DateTime);
        var periodEnd = DateOnly.FromDateTime(endExclusive.DateTime.AddDays(-1));

        decimal tvaDeductible = await context.Disbursements
            .Where(d => d.Date >= periodStart && d.Date <= periodEnd)
            .SumAsync(d => d.VatAmount, cancellationToken);

        // 5. Generate Declaration
        var declaration = TaxCalculator.CalculateTaxDeclaration(
            schoolId,
            request.Month,
            request.Year,
            fiches,
            tvaCollected,
            tvaDeductible
        );

        context.TaxeDeclarations.Add(declaration);
        await context.SaveChangesAsync(cancellationToken);

        return declaration.Id;
    }
}
