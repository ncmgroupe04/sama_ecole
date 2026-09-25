using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Attendance.Events;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Attendance.EntryTickets;

/// <summary>Numéro imprimé d'un billet d'entrée : dérivé de l'identifiant du retard, donc stable (voir GetEntryTicketQuery).</summary>
public static class EntryTicketNumber
{
    public static string For(Guid lateArrivalId) => $"BILLET-{lateArrivalId.ToString()[..8].ToUpperInvariant()}";
}

/// <summary>
/// Applique un billet d'entrée au REGISTRE D'APPEL du cours qu'il vise (Évolution N°5, arbitrage B6).
///
/// Si la fiche du cours (créneau, date) existe déjà, la ligne de l'élève passe à « Retard » avec les minutes du
/// billet et lui est rattachée ; le statut d'avant est CONSERVÉ sur le billet, ce qui permet de le restaurer si
/// le billet est annulé avant acceptation. Si la fiche n'existe pas encore, il n'y a RIEN à faire : la feuille
/// d'appel de l'enseignant présélectionne le retard (InitializeAttendanceSheetQueryHandler) et le billet est
/// rattaché à la soumission.
///
/// Ne sauvegarde jamais : il modifie des entités suivies, l'appelant fait UN SEUL SaveChanges — le billet et la
/// ligne d'appel sont donc écrits ensemble ou pas du tout.
/// </summary>
public class EntryTicketRegister(IApplicationDbContext dbContext)
{
    /// <summary>
    /// Passe la ligne d'appel de l'élève à « Retard » (à l'émission du billet, puis — de façon IDEMPOTENTE —
    /// à son acceptation, qui répare une ligne que l'enseignant aurait entre-temps saisie « Absent »).
    ///
    /// Renvoie la notification à publier APRÈS l'enregistrement, ou null. Elle n'existe que si la ligne passait
    /// d'une ABSENCE à un retard : la famille avait alors été prévenue que l'élève était absent, elle reçoit la
    /// rectification. Une ligne « Présent » qui devient retard, une ligne déjà en retard avec ce billet, ou
    /// l'absence de fiche n'en produisent aucune.
    /// </summary>
    public async Task<AttendanceRecordedEvent?> ApplyAsync(
        LateArrival ticket, ScheduleSlot slot, DateOnly date, Guid schoolId, CancellationToken cancellationToken)
    {
        var sheet = await dbContext.AttendanceSheets
            .FirstOrDefaultAsync(a => a.ScheduleSlotId == slot.Id && a.Date == date, cancellationToken);

        if (sheet is null)
        {
            return null;
        }

        var line = await dbContext.StudentAttendances
            .FirstOrDefaultAsync(sa => sa.AttendanceSheetId == sheet.Id && sa.StudentId == ticket.StudentId, cancellationToken);

        var wasAbsence = false;

        // Billet par heure d'arrivée pile à l'heure de début du cours visé (0 minute de retard) : il n'y a rien à
        // passer en « Retard » — le billet se contente d'être RATTACHÉ à la ligne, que l'enseignant du cours accepte.
        var noLate = ticket.Minutes <= 0;

        if (line is null)
        {
            if (noLate)
            {
                return null;
            }

            // L'élève ne figurait pas sur la fiche (arrivé après la saisie) : le billet crée sa ligne.
            dbContext.StudentAttendances.Add(new StudentAttendance
            {
                SchoolId = schoolId,
                AttendanceSheetId = sheet.Id,
                StudentId = ticket.StudentId,
                Status = AttendanceStatus.Late,
                LateMinutes = ticket.Minutes,
                EntryTicketId = ticket.Id
            });
        }
        else
        {
            // Déjà appliqué (émission puis acceptation, ou acceptation rejouée) : rien à changer.
            if (line.EntryTicketId == ticket.Id && (line.Status == AttendanceStatus.Late || noLate))
            {
                return null;
            }

            if (noLate)
            {
                line.EntryTicketId = ticket.Id;
                return null;
            }

            // Le statut d'AVANT le billet n'est conservé qu'une fois : c'est lui que l'annulation restaurera.
            ticket.PreviousStatus ??= line.Status;
            ticket.PreviousLateMinutes ??= line.LateMinutes;
            wasAbsence = line.Status is AttendanceStatus.JustifiedAbsence or AttendanceStatus.UnjustifiedAbsence;

            line.Status = AttendanceStatus.Late;
            line.LateMinutes = ticket.Minutes;
            line.EntryTicketId = ticket.Id;
        }

        return wasAbsence
            ? new AttendanceRecordedEvent(
                schoolId, ticket.StudentId, sheet.ClassroomId, sheet.SubjectId, date, sheet.Period,
                AttendanceStatus.Late, ticket.Minutes)
            : null;
    }

    /// <summary>
    /// Complément N°5 bis (arbitrage C5) : pour chaque cours MANQUÉ avant l'arrivée dont la fiche existe déjà, la ligne
    /// de l'élève passe de « Absent (non justifié) » à « Absent (justifié) » — le billet justifie ce cours. JAMAIS
    /// depuis « Présent », « Retard » ou une absence déjà justifiée (on ne réécrit que ce que le billet régularise),
    /// jamais l'inverse, et aucune ligne n'est créée pour un élève absent de la fiche. Le statut d'avant est gardé SUR
    /// LA LIGNE (PreviousStatus) : c'est ce qui permet à l'annulation de la restaurer. Fiche absente : rien à faire, la
    /// feuille d'appel présélectionne l'absence justifiée (InitializeAttendanceSheetQueryHandler). Aucune notification
    /// (arbitrage C7). Ne sauvegarde jamais : l'appelant fait UN SEUL SaveChanges.
    /// </summary>
    public async Task ApplyMissedAsync(LateArrival ticket, DateOnly date, CancellationToken cancellationToken)
    {
        if (ticket.MissedScheduleSlotIds is not { Length: > 0 } missed)
        {
            return;
        }

        var lines = await (
            from sa in dbContext.StudentAttendances
            join sheet in dbContext.AttendanceSheets on sa.AttendanceSheetId equals sheet.Id
            where sa.StudentId == ticket.StudentId
                  && sheet.Date == date
                  && sheet.ScheduleSlotId != null
                  && missed.Contains(sheet.ScheduleSlotId.Value)
            select sa).ToListAsync(cancellationToken);

        foreach (var line in lines.Where(l => l.Status == AttendanceStatus.UnjustifiedAbsence))
        {
            line.PreviousStatus = line.Status;
            line.PreviousLateMinutes = line.LateMinutes;
            line.Status = AttendanceStatus.JustifiedAbsence;
            line.LateMinutes = 0;
            line.EntryTicketId = ticket.Id;
        }
    }

    /// <summary>
    /// Annulation d'un billet NON accepté : chaque ligne d'appel rattachée au billet retrouve le statut qu'elle avait
    /// avant lui et s'en détache. Aucune notification — la famille n'est pas prévenue d'une annulation.
    ///
    /// Deux sources de « statut d'avant » : la LIGNE elle-même (cours manqués justifiés par le billet, Complément
    /// N°5 bis) ou, pour la ligne du cours VISÉ, le billet (PreviousStatus, Évolution N°5). Sans statut d'avant
    /// (la fiche n'existait pas à l'émission, la ligne a été créée ou saisie plus tard), la ligne garde ce qui a
    /// été constaté en classe et se détache seulement : le registre appartient à l'enseignant qui a fait l'appel,
    /// pas au billet.
    /// </summary>
    public async Task RestoreAsync(LateArrival ticket, DateOnly date, CancellationToken cancellationToken)
    {
        var rows = await (
            from sa in dbContext.StudentAttendances
            join sheet in dbContext.AttendanceSheets on sa.AttendanceSheetId equals sheet.Id
            where sheet.Date == date && sa.EntryTicketId == ticket.Id
            select new { Line = sa, sheet.ScheduleSlotId }).ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            var line = row.Line;

            if (line.PreviousStatus is { } lineBefore)
            {
                line.Status = lineBefore;
                line.LateMinutes = line.PreviousLateMinutes ?? 0;
                line.PreviousStatus = null;
                line.PreviousLateMinutes = null;
            }
            else if (row.ScheduleSlotId == ticket.TargetScheduleSlotId && ticket.PreviousStatus is { } ticketBefore)
            {
                line.Status = ticketBefore;
                line.LateMinutes = ticket.PreviousLateMinutes ?? 0;
            }

            line.EntryTicketId = null;
        }
    }
}
