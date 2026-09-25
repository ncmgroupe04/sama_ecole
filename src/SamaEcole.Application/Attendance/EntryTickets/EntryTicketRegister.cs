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

        if (line is null)
        {
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
            if (line.EntryTicketId == ticket.Id && line.Status == AttendanceStatus.Late)
            {
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
    /// Annulation d'un billet NON accepté : la ligne d'appel retrouve le statut qu'elle avait avant lui et se
    /// détache du billet. Aucune notification — la famille n'est pas prévenue d'une annulation.
    ///
    /// Si le billet n'a conservé aucun statut d'avant (la fiche n'existait pas à l'émission, la ligne a été créée
    /// ou saisie plus tard), la ligne garde ce qui a été constaté en classe et se détache seulement : le
    /// registre appartient à l'enseignant qui a fait l'appel, pas au billet.
    /// </summary>
    public async Task RestoreAsync(
        LateArrival ticket, ScheduleSlot slot, DateOnly date, CancellationToken cancellationToken)
    {
        var line = await (
            from sa in dbContext.StudentAttendances
            join sheet in dbContext.AttendanceSheets on sa.AttendanceSheetId equals sheet.Id
            where sheet.ScheduleSlotId == slot.Id && sheet.Date == date && sa.EntryTicketId == ticket.Id
            select sa).FirstOrDefaultAsync(cancellationToken);

        if (line is null)
        {
            return;
        }

        if (ticket.PreviousStatus is { } previous)
        {
            line.Status = previous;
            line.LateMinutes = ticket.PreviousLateMinutes ?? 0;
        }

        line.EntryTicketId = null;
    }
}
