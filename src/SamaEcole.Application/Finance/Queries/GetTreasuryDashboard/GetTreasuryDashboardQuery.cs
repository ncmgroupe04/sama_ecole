using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Finance.Queries.GetTreasuryDashboard;

public record TreasuryDashboardDto(
    decimal TotalRevenues,
    decimal TotalDisbursements,
    decimal NetLiquidity,
    decimal TotalOutstandingDebt,
    int PendingPaymentsCount
);

public record GetTreasuryDashboardQuery : IRequest<TreasuryDashboardDto>;

public class GetTreasuryDashboardQueryHandler(
    IApplicationDbContext context,
    ITenantProvider tenantProvider) : IRequestHandler<GetTreasuryDashboardQuery, TreasuryDashboardDto>
{
    public async Task<TreasuryDashboardDto> Handle(GetTreasuryDashboardQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId ?? throw new UnauthorizedAccessException();

        // 1. Total Revenues (Encaissements)
        var totalRevenues = await context.Payments
            .Where(p => p.SchoolId == schoolId && p.Status == Domain.Enums.PaymentStatus.Paid)
            .SumAsync(p => p.Amount, cancellationToken);

        // 2. Total Disbursements (Décaissements)
        var totalDisbursements = await context.Disbursements
            .Where(d => d.SchoolId == schoolId)
            .SumAsync(d => d.Amount, cancellationToken);

        // 3. Outstanding Debt (Reste à payer sur les inscriptions validées)
        var totalOutstandingDebt = await context.Enrollments
            .Where(e => e.SchoolId == schoolId && e.Status == Domain.Enums.EnrollmentStatus.Confirmed)
            .SumAsync(e => e.TotalDue - e.AmountPaid, cancellationToken);

        // 4. Pending Payments Count
        var pendingPaymentsCount = await context.Payments
            .Where(p => p.SchoolId == schoolId && p.Status == Domain.Enums.PaymentStatus.Partial)
            .CountAsync(cancellationToken);

        var netLiquidity = totalRevenues - totalDisbursements;

        return new TreasuryDashboardDto(
            TotalRevenues: totalRevenues,
            TotalDisbursements: totalDisbursements,
            NetLiquidity: netLiquidity,
            TotalOutstandingDebt: totalOutstandingDebt,
            PendingPaymentsCount: pendingPaymentsCount
        );
    }
}
