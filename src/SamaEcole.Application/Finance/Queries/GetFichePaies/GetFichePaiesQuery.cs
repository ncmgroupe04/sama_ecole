using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetFichePaies;

public record GetFichePaiesQuery(int? Month = null, int? Year = null) : IRequest<List<FichePaieListItemDto>>;

public record FichePaieListItemDto(
    Guid Id,
    Guid EmployeeContractId,
    string EmployeeFullName,
    int Month,
    int Year,
    decimal HoursWorked,
    decimal GrossSalary,
    decimal TransportAllowance,
    decimal IpresEmployee,
    decimal IpresEmployer,
    decimal CssEmployer,
    decimal Vrs,
    decimal Brs,
    decimal NetSalary,
    DateTimeOffset CreatedAt);

public class GetFichePaiesQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetFichePaiesQuery, List<FichePaieListItemDto>>
{
    public async Task<List<FichePaieListItemDto>> Handle(GetFichePaiesQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.FichePaies.AsNoTracking()
            .Include(f => f.EmployeeContract).ThenInclude(c => c.Teacher)
            .Include(f => f.EmployeeContract).ThenInclude(c => c.User)
            .AsQueryable();

        if (request.Month.HasValue)
        {
            query = query.Where(f => f.Month == request.Month.Value);
        }

        if (request.Year.HasValue)
        {
            query = query.Where(f => f.Year == request.Year.Value);
        }

        return await query
            .OrderByDescending(f => f.Year).ThenByDescending(f => f.Month).ThenByDescending(f => f.CreatedAt)
            .Select(f => new FichePaieListItemDto(
                f.Id,
                f.EmployeeContractId,
                f.EmployeeContract.Teacher != null ? f.EmployeeContract.Teacher.FullName : f.EmployeeContract.User!.FullName,
                f.Month,
                f.Year,
                f.HoursWorked,
                f.GrossSalary,
                f.TransportAllowance,
                f.IpresEmployee,
                f.IpresEmployer,
                f.CssEmployer,
                f.Vrs,
                f.Brs,
                f.NetSalary,
                f.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
