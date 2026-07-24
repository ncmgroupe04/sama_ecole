using FluentValidation;
using MediatR;
using SamaEcole.Application.Common.Interfaces;

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
    string? ReceiptUrl) : IRequest<Guid>;

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
