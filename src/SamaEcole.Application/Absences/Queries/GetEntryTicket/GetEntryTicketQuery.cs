using SamaEcole.Application.Attendance;
using SamaEcole.Application.Attendance.EntryTickets;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Absences.Queries.GetEntryTicket;

/// <summary>
/// Données d'un billet d'entrée, dérivées d'un retard (<c>LateArrival</c>) déjà enregistré par la
/// Surveillance. Le billet n'est pas une entité à part : c'est l'impression officielle d'un retard,
/// ce qui lui donne un numéro stable (dérivé de l'identifiant du retard) et garantit la traçabilité
/// (le retard reste historisé, jamais supprimé physiquement).
/// </summary>
public record GetEntryTicketQuery(Guid LateArrivalId) : IRequest<EntryTicketDto>;

/// <summary>Un cours manqué avant l'arrivée, tel qu'il s'imprime sur le billet (Complément N°5 bis).</summary>
public record EntryTicketMissedSlot(string SubjectName, string TimeRange, int Minutes);

public record EntryTicketDto(
    Guid LateArrivalId,
    string TicketNumber,
    DateTimeOffset IssuedAt,
    string StudentFullName,
    string Matricule,
    string ClassroomName,
    string ClassroomLevel,
    DateTime Date,
    int Minutes,
    string Reason,
    string? Observations,
    string SchoolName,
    string? SchoolAddress,
    string? SchoolPhone,
    string? SchoolEmail,
    string? SchoolCity,
    string? SchoolNinea,
    string? SchoolRegistreCommerce,
    string? SchoolLogoUrl,
    string? SurveillantSignatureUrl,

    // Cours visé et statut (Évolution N°5). Tous nuls pour un billet SANS cours visé : il s'imprime alors
    // exactement comme avant. Les défauts gardent compatible toute construction existante du billet.
    /// <summary>Matière du cours que l'élève rejoint.</summary>
    string? TargetSubjectName = null,
    /// <summary>Horaires du cours, « 08:00-10:00 ».</summary>
    string? TargetTimeRange = null,
    /// <summary>Enseignant titulaire du cours — celui qui accepte le billet en classe.</summary>
    string? TargetTeacherName = null,
    /// <summary>« Issued », « Accepted » ou « Cancelled » ; null sans cours visé.</summary>
    string? Status = null,

    // Billet par heure d'arrivée (Complément N°5 bis). Nuls pour un billet saisi à l'ancienne : il s'imprime alors
    // exactement comme avant (« RETARD DE X MIN »).
    /// <summary>Heure d'arrivée réelle de l'élève.</summary>
    TimeOnly? ArrivalTime = null,
    /// <summary>Durée régularisée (cours manqués + retard), calculée à l'émission.</summary>
    int? TotalMinutes = null,
    /// <summary>Cours entièrement manqués avant l'arrivée, dans l'ordre de la journée.</summary>
    IReadOnlyList<EntryTicketMissedSlot>? MissedSlots = null);

public class GetEntryTicketQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetEntryTicketQuery, EntryTicketDto>
{
    public async Task<EntryTicketDto> Handle(GetEntryTicketQuery request, CancellationToken cancellationToken)
    {
        var row = await (
            from l in dbContext.LateArrivals.AsNoTracking()
            join s in dbContext.Students.AsNoTracking() on l.StudentId equals s.Id
            join c in dbContext.Classrooms.AsNoTracking() on s.ClassroomId equals c.Id
            join sch in dbContext.Schools.AsNoTracking() on l.SchoolId equals sch.Id
            where l.Id == request.LateArrivalId
            select new
            {
                l.Id,
                l.CreatedAt,
                s.Matricule,
                StudentFullName = s.FullName,
                ClassroomName = c.Name,
                ClassroomLevel = c.Level,
                l.Date,
                l.Minutes,
                l.Reason,
                l.Observations,
                l.TargetScheduleSlotId,
                l.Status,
                l.ArrivalTime,
                l.TotalMinutes,
                l.MissedScheduleSlotIds,
                SchoolName = sch.Name,
                SchoolAddress = sch.Address,
                SchoolPhone = sch.Phone,
                SchoolEmail = sch.Email,
                sch.Ninea,
                sch.RegistreCommerce,
                sch.LogoUrl
            }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Retard introuvable : {request.LateArrivalId}");

        // Cours visé (Évolution N°5) : matière, horaires et titulaire, lus seulement si le billet en vise un.
        string? targetSubject = null, targetTimeRange = null, targetTeacher = null;
        if (row.TargetScheduleSlotId is { } slotId)
        {
            var slot = await dbContext.ScheduleSlots.AsNoTracking()
                .Where(s => s.Id == slotId)
                .Select(s => new { SubjectName = s.Subject.Name, TeacherName = s.Teacher.FullName, s.StartTime, s.EndTime })
                .FirstOrDefaultAsync(cancellationToken);

            if (slot is not null)
            {
                targetSubject = slot.SubjectName;
                targetTimeRange = SlotPeriod.Label(slot.StartTime, slot.EndTime);
                targetTeacher = slot.TeacherName;
            }
        }

        // Cours manqués (instantané pris à l'émission) : matière et horaires, dans l'ordre de la journée.
        List<EntryTicketMissedSlot>? missedSlots = null;
        if (row.MissedScheduleSlotIds is { Length: > 0 } missedIds)
        {
            missedSlots = (await dbContext.ScheduleSlots.AsNoTracking()
                    .Where(s => missedIds.Contains(s.Id))
                    .OrderBy(s => s.StartTime)
                    .Select(s => new { SubjectName = s.Subject.Name, s.StartTime, s.EndTime })
                    .ToListAsync(cancellationToken))
                .Select(s => new EntryTicketMissedSlot(
                    s.SubjectName, SlotPeriod.Label(s.StartTime, s.EndTime), (int)(s.EndTime - s.StartTime).TotalMinutes))
                .ToList();
        }

        // Global Query Filter + RLS bornent déjà cette lecture à l'école courante (même tenant que
        // LateArrivals ci-dessus) : au plus une ligne de réglages par école (JGK-B02).
        var surveillantSignatureUrl = await dbContext.SchoolSettings.AsNoTracking()
            .Select(set => set.SurveillantSignatureUrl)
            .FirstOrDefaultAsync(cancellationToken);

        return new EntryTicketDto(
            row.Id,
            EntryTicketNumber.For(row.Id),
            row.CreatedAt,
            row.StudentFullName,
            row.Matricule,
            row.ClassroomName,
            row.ClassroomLevel,
            row.Date,
            row.Minutes,
            row.Reason,
            row.Observations,
            row.SchoolName,
            row.SchoolAddress,
            row.SchoolPhone,
            row.SchoolEmail,
            ReceiptCity.FromAddress(row.SchoolAddress),
            row.Ninea,
            row.RegistreCommerce,
            row.LogoUrl,
            surveillantSignatureUrl,
            targetSubject,
            targetTimeRange,
            targetTeacher,
            row.Status?.ToString(),
            row.ArrivalTime,
            row.TotalMinutes,
            missedSlots);
    }
}
