using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Absences;

/// <summary>
/// Applique un billet d'absence au REGISTRE D'APPEL : les lignes « Absent (non justifié) » des jours couverts
/// passent à « Absent (justifié) », le statut d'avant est gardé sur la ligne (PreviousStatus) et le billet y est
/// rattaché. JAMAIS depuis « Présent » ou « Retard » (ce que l'enseignant a constaté en classe prime), jamais
/// l'inverse. Ne sauvegarde jamais : l'appelant fait UN SEUL SaveChanges.
/// </summary>
public class AbsenceJustificationRegister(IApplicationDbContext dbContext)
{
    public async Task<int> ApplyAsync(AbsenceJustification justification, CancellationToken cancellationToken)
    {
        var lines = await (
            from sa in dbContext.StudentAttendances
            join sheet in dbContext.AttendanceSheets on sa.AttendanceSheetId equals sheet.Id
            where sa.StudentId == justification.StudentId
                  && sheet.Date >= justification.StartDate
                  && sheet.Date <= justification.EndDate
                  && sa.Status == AttendanceStatus.UnjustifiedAbsence
            select sa).ToListAsync(cancellationToken);

        foreach (var line in lines)
        {
            line.PreviousStatus = line.Status;
            line.PreviousLateMinutes = line.LateMinutes;
            line.Status = AttendanceStatus.JustifiedAbsence;
            line.LateMinutes = 0;
            line.AbsenceJustificationId = justification.Id;
        }

        return lines.Count;
    }

    /// <summary>Annulation : chaque ligne rattachée retrouve son statut d'avant et se détache du billet.</summary>
    public async Task<int> RestoreAsync(Guid justificationId, CancellationToken cancellationToken)
    {
        var lines = await dbContext.StudentAttendances
            .Where(sa => sa.AbsenceJustificationId == justificationId)
            .ToListAsync(cancellationToken);

        foreach (var line in lines)
        {
            if (line.PreviousStatus is { } before)
            {
                line.Status = before;
                line.LateMinutes = line.PreviousLateMinutes ?? 0;
                line.PreviousStatus = null;
                line.PreviousLateMinutes = null;
            }

            line.AbsenceJustificationId = null;
        }

        return lines.Count;
    }
}
