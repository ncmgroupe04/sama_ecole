using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetPayslip;

public record GetPayslipQuery(Guid FichePaieId) : IRequest<PayslipDto>;

public record PayslipDto(
    Guid FichePaieId,
    string PayslipNumber,
    string EmployeeFullName,
    string EmployeeRole,
    string ContractType,
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
    string SchoolName,
    string? SchoolAddress,
    string? SchoolCity,
    string? SchoolNinea,
    string? SchoolLogoUrl);

public class GetPayslipQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetPayslipQuery, PayslipDto>
{
    public async Task<PayslipDto> Handle(GetPayslipQuery request, CancellationToken cancellationToken)
    {
        var row = await (
            from f in dbContext.FichePaies.AsNoTracking()
            join c in dbContext.EmployeeContracts.AsNoTracking() on f.EmployeeContractId equals c.Id
            join sch in dbContext.Schools.AsNoTracking() on f.SchoolId equals sch.Id
            where f.Id == request.FichePaieId
            select new { FichePaie = f, Contract = c, School = sch })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Fiche de paie introuvable : {request.FichePaieId}");

        // Teacher/User sont chargés séparément (jointure conditionnelle peu naturelle en LINQ-to-SQL) :
        // un contrat porte l'un OU l'autre, jamais les deux (CreateEmployeeContractCommandValidator).
        string employeeFullName;
        string employeeRole;
        if (row.Contract.TeacherId.HasValue)
        {
            var teacher = await dbContext.Teachers.AsNoTracking()
                .FirstAsync(t => t.Id == row.Contract.TeacherId.Value, cancellationToken);
            employeeFullName = teacher.FullName;
            employeeRole = "Enseignant";
        }
        else
        {
            var user = await dbContext.Users.AsNoTracking()
                .FirstAsync(u => u.Id == row.Contract.UserId!.Value, cancellationToken);
            employeeFullName = user.FullName;
            employeeRole = user.Role.ToString();
        }

        var f2 = row.FichePaie;

        return new PayslipDto(
            f2.Id,
            $"BP-{f2.Id.ToString()[..8].ToUpperInvariant()}",
            employeeFullName,
            employeeRole,
            row.Contract.Type.ToString(),
            f2.Month,
            f2.Year,
            f2.HoursWorked,
            f2.GrossSalary,
            f2.TransportAllowance,
            f2.IpresEmployee,
            f2.IpresEmployer,
            f2.CssEmployer,
            f2.Vrs,
            f2.Brs,
            f2.NetSalary,
            row.School.Name,
            row.School.Address,
            ReceiptCity.FromAddress(row.School.Address),
            row.School.Ninea,
            row.School.LogoUrl);
    }
}
