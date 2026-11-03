using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetEmployeeContracts;

public record GetEmployeeContractsQuery : IRequest<List<EmployeeContractDto>>;

public record EmployeeContractDto(
    Guid Id,
    Guid? TeacherId,
    Guid? UserId,
    string EmployeeFullName,
    string EmployeeRole,
    string Type,
    decimal BaseSalary,
    decimal HourlyRate,
    decimal TransportAllowance,
    string PayoutMethod,
    string? PayoutAccountReference,
    DateOnly? EndDate,
    uint RowVersion,
    string? ExternalPhone = null,
    bool ApplyStatutoryDeductions = true);

public class GetEmployeeContractsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetEmployeeContractsQuery, List<EmployeeContractDto>>
{
    public async Task<List<EmployeeContractDto>> Handle(GetEmployeeContractsQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.EmployeeContracts.AsNoTracking()
            .Include(c => c.Teacher)
            .Include(c => c.User)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new EmployeeContractDto(
                c.Id,
                c.TeacherId,
                c.UserId,
                c.Teacher != null ? c.Teacher.FullName : c.User != null ? c.User.FullName : c.ExternalFullName ?? "—",
                c.Teacher != null ? "Enseignant" : c.User != null ? c.User.Role.ToString() : (c.ExternalJobTitle ?? "Personnel"),
                c.Type.ToString(),
                c.BaseSalary,
                c.HourlyRate,
                c.TransportAllowance,
                c.PayoutMethod.ToString(),
                c.PayoutAccountReference,
                c.EndDate,
                EF.Property<uint>(c, "xmin"),
                c.ExternalPhone,
                c.ApplyStatutoryDeductions))
            .ToListAsync(cancellationToken);
    }
}
