using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;

using SamaEcole.Domain.Enums;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Features.Disbursements;

//[Authorize(Roles = "SuperAdmin, Directeur, Finance")]
public record GetDisbursementsQuery(
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    DisbursementCategory? Category = null) : IRequest<List<DisbursementDto>>;

public class DisbursementDto
{
    public Guid Id { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string PaymentMethod { get; init; } = string.Empty;
    public DateOnly Date { get; init; }
    public string Beneficiary { get; init; } = string.Empty;
    public string? ReceiptUrl { get; init; }
}

public class GetDisbursementsQueryHandler(IApplicationDbContext context) 
    : IRequestHandler<GetDisbursementsQuery, List<DisbursementDto>>
{
    public async Task<List<DisbursementDto>> Handle(GetDisbursementsQuery request, CancellationToken cancellationToken)
    {
        var query = context.Disbursements.AsNoTracking();

        if (request.StartDate.HasValue)
            query = query.Where(d => d.Date >= request.StartDate.Value);
            
        if (request.EndDate.HasValue)
            query = query.Where(d => d.Date <= request.EndDate.Value);
            
        if (request.Category.HasValue)
            query = query.Where(d => d.Category == request.Category.Value);

        return await query
            .OrderByDescending(d => d.Date)
            .ThenByDescending(d => d.CreatedAt)
            .Select(d => new DisbursementDto
            {
                Id = d.Id,
                Reason = d.Reason,
                Category = d.Category.ToString(),
                Amount = d.Amount,
                PaymentMethod = d.PaymentMethod.ToString(),
                Date = d.Date,
                Beneficiary = d.Beneficiary,
                ReceiptUrl = d.ReceiptUrl
            })
            .ToListAsync(cancellationToken);
    }
}
