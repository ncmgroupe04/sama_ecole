using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetHourRecordSheet;

public record GetHourRecordSheetQuery(Guid EmployeeContractId, int Month, int Year) : IRequest<HourRecordSheetDto>;

/// <summary>
/// Fiche de suivi des heures d'un Vacataire pour un mois donné — document RH interne (comme le
/// bulletin de paie), pas de bandeau M.E.N.
/// </summary>
public record HourRecordSheetDto(
    Guid EmployeeContractId,
    string SheetNumber,
    string EmployeeFullName,
    int Month,
    int Year,
    decimal HourlyRate,
    IReadOnlyList<HourRecordLineDto> Lines,
    decimal TotalHours,
    string SchoolName,
    string? SchoolAddress,
    string? SchoolCity,
    string? SchoolPhone,
    string? SchoolNinea,
    string? SchoolLogoUrl);

public record HourRecordLineDto(DateOnly Date, decimal Hours, string? Note);

public class GetHourRecordSheetQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetHourRecordSheetQuery, HourRecordSheetDto>
{
    public async Task<HourRecordSheetDto> Handle(GetHourRecordSheetQuery request, CancellationToken cancellationToken)
    {
        var contract = await (
            from c in dbContext.EmployeeContracts.AsNoTracking()
            join sch in dbContext.Schools.AsNoTracking() on c.SchoolId equals sch.Id
            where c.Id == request.EmployeeContractId
            select new
            {
                c.Id,
                c.HourlyRate,
                EmployeeFullName = c.Teacher != null ? c.Teacher.FullName : c.User!.FullName,
                School = sch
            }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Contrat introuvable : {request.EmployeeContractId}");

        var lines = await dbContext.TeacherHourRecords.AsNoTracking()
            .Where(r => r.EmployeeContractId == request.EmployeeContractId
                && r.Date.Month == request.Month && r.Date.Year == request.Year)
            .OrderBy(r => r.Date)
            .Select(r => new HourRecordLineDto(r.Date, r.Hours, r.Note))
            .ToListAsync(cancellationToken);

        return new HourRecordSheetDto(
            contract.Id,
            $"FH-{contract.Id.ToString()[..8].ToUpperInvariant()}-{request.Year}{request.Month:D2}",
            contract.EmployeeFullName,
            request.Month,
            request.Year,
            contract.HourlyRate,
            lines,
            lines.Sum(l => l.Hours),
            contract.School.Name,
            contract.School.Address,
            ReceiptCity.FromAddress(contract.School.Address),
            contract.School.Phone,
            contract.School.Ninea,
            contract.School.LogoUrl);
    }
}
