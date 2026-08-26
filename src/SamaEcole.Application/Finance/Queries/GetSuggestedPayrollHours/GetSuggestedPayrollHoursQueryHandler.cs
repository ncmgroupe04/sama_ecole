using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetSuggestedPayrollHours;

public class GetSuggestedPayrollHoursQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetSuggestedPayrollHoursQuery, SuggestedPayrollHoursDto>
{
    public async Task<SuggestedPayrollHoursDto> Handle(
        GetSuggestedPayrollHoursQuery request, CancellationToken cancellationToken)
    {
        var contract = await dbContext.EmployeeContracts.AsNoTracking()
            .Where(c => c.Id == request.EmployeeContractId)
            .Select(c => new { c.Id, c.TeacherId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Contrat {request.EmployeeContractId} introuvable.");

        // Même source que la fiche de suivi imprimable (GetHourRecordSheetQuery) : les deux doivent
        // toujours raconter le même total, faute de quoi la Direction verrait deux chiffres
        // contradictoires pour le même mois.
        var declaredByDate = await dbContext.TeacherHourRecords.AsNoTracking()
            .Where(r => r.EmployeeContractId == request.EmployeeContractId
                        && r.Date.Month == request.Month && r.Date.Year == request.Year)
            .GroupBy(r => r.Date)
            .Select(g => new { Date = g.Key, Hours = g.Sum(r => r.Hours) })
            .ToListAsync(cancellationToken);

        var suggestedHours = declaredByDate.Sum(d => d.Hours);

        // Un contrat lié à un UserId (personnel non-enseignant) n'a pas d'emploi du temps de classe :
        // pas de rapprochement possible, seule la suggestion agrégée est renvoyée.
        if (contract.TeacherId is not { } teacherId)
        {
            return new SuggestedPayrollHoursDto(contract.Id, suggestedHours, []);
        }

        var scheduledHoursByDayOfWeek = await dbContext.ScheduleSlots.AsNoTracking()
            .Where(s => s.TeacherId == teacherId)
            .GroupBy(s => s.DayOfWeek)
            .Select(g => new { DayOfWeek = g.Key, Hours = g.Sum(s => (decimal)(s.EndTime - s.StartTime).TotalHours) })
            .ToDictionaryAsync(g => g.DayOfWeek, g => g.Hours, cancellationToken);

        var discrepancies = declaredByDate
            .Select(d => new
            {
                d.Date,
                d.Hours,
                Scheduled = scheduledHoursByDayOfWeek.GetValueOrDefault(d.Date.DayOfWeek, 0m)
            })
            .Where(d => d.Hours != d.Scheduled)
            .OrderBy(d => d.Date)
            .Select(d => new PayrollHoursDiscrepancyDto(d.Date, d.Hours, d.Scheduled))
            .ToList();

        return new SuggestedPayrollHoursDto(contract.Id, suggestedHours, discrepancies);
    }
}
