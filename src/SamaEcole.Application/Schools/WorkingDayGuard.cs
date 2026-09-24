using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Schools;

/// <summary>
/// Refuse (422) toute saisie datée un JOUR DE REPOS de l'établissement (Évolution N°3, réglage
/// SchoolSettings.WorkingDays). Lit les réglages de l'école COURANTE — le Global Query Filter et la RLS
/// bornent la lecture —, jamais un identifiant fourni par le client (règle #10). Une école sans ligne de
/// réglages, ou dont la valeur est illisible, garde la semaine par défaut (lundi → samedi).
///
/// Ne s'applique qu'aux saisies NOUVELLES : les données déjà enregistrées un jour devenu jour de repos
/// restent lisibles et comptées (arbitrage D2 du plan 2026-09-24-working-days).
/// </summary>
public class WorkingDayGuard(IApplicationDbContext dbContext)
{
    public async Task<IReadOnlyList<DayOfWeek>> GetWorkingDaysAsync(CancellationToken cancellationToken)
    {
        var stored = await dbContext.SchoolSettings.AsNoTracking()
            .Select(s => s.WorkingDays)
            .FirstOrDefaultAsync(cancellationToken);

        return SchoolWeek.FromStored(stored);
    }

    public async Task EnsureWorkingDayAsync(DateOnly date, string field, CancellationToken cancellationToken)
    {
        var days = await GetWorkingDaysAsync(cancellationToken);
        if (SchoolWeek.IsWorkingDay(days, date)) return;

        throw new ValidationException([
            new ValidationFailure(field,
                $"Le {SchoolWeek.FrenchName(date.DayOfWeek)} {date:dd/MM/yyyy} est un jour de repos de l'établissement "
                + $"({SchoolWeek.RestDaysLabel(days)}) : aucune saisie n'y est possible. "
                + "Les jours ouvrés se règlent dans Paramètres › Pédagogie.")
        ]);
    }

    public async Task EnsureWorkingDayAsync(DayOfWeek day, string field, CancellationToken cancellationToken)
    {
        var days = await GetWorkingDaysAsync(cancellationToken);
        if (SchoolWeek.IsWorkingDay(days, day)) return;

        throw new ValidationException([
            new ValidationFailure(field,
                $"Le {SchoolWeek.FrenchName(day)} est un jour de repos de l'établissement ({SchoolWeek.RestDaysLabel(days)}) : "
                + "un créneau d'emploi du temps ne peut pas y être placé.")
        ]);
    }
}
