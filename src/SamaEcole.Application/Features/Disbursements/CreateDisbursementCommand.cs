using FluentValidation;
using MediatR;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Services;

using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Features.Disbursements;

//[Authorize(Roles = "SuperAdmin, Directeur, Finance")]
public record CreateDisbursementCommand(
    string Reason,
    DisbursementCategory Category,
    decimal Amount,
    PaymentMethod PaymentMethod,
    DateOnly Date,
    string Beneficiary,
    string? ReceiptUrl,

    /// <summary>
    /// Taux de TVA (fraction, ex. 0.18) payé au fournisseur si cette dépense est assujettie, ou null
    /// (défaut) sinon — ex. Salaires n'est jamais assujettie. Déclaré explicitement par la Finance,
    /// jamais déduit automatiquement d'une catégorie.
    /// </summary>
    decimal? VatRate = null) : IRequest<Guid>;

public class CreateDisbursementCommandValidator : AbstractValidator<CreateDisbursementCommand>
{
    public CreateDisbursementCommandValidator()
    {
        RuleFor(v => v.Reason).NotEmpty().MaximumLength(255);
        RuleFor(v => v.Category).IsInEnum();
        RuleFor(v => v.Amount).GreaterThan(0);
        RuleFor(v => v.PaymentMethod).IsInEnum();
        RuleFor(v => v.Date).NotEmpty();
        RuleFor(v => v.Beneficiary).NotEmpty().MaximumLength(150);
        RuleFor(v => v.VatRate)
            .InclusiveBetween(0m, 1m).WithMessage("Le taux de TVA doit être compris entre 0 et 1 (ex. 0.18 pour 18 %).")
            .When(v => v.VatRate.HasValue);
    }
}

public class CreateDisbursementCommandHandler(
    IApplicationDbContext context,
    ITenantProvider tenantProvider) : IRequestHandler<CreateDisbursementCommand, Guid>
{
    public async Task<Guid> Handle(CreateDisbursementCommand request, CancellationToken cancellationToken)
    {
        var disbursement = new Disbursement
        {
            SchoolId = tenantProvider.CurrentSchoolId.Value,
            Reason = request.Reason,
            Category = request.Category,
            Amount = request.Amount,
            VatRate = request.VatRate,
            VatAmount = VatCalculator.ComputeVatAmount(request.Amount, request.VatRate),
            PaymentMethod = request.PaymentMethod,
            Date = request.Date,
            Beneficiary = request.Beneficiary,
            ReceiptUrl = request.ReceiptUrl
        };

        context.Disbursements.Add(disbursement);
        await context.SaveChangesAsync(cancellationToken);

        return disbursement.Id;
    }
}
