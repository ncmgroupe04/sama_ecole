using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Attendance;
using SamaEcole.Application.Attendance.Events;
using SamaEcole.Application.Attendance.EntryTickets;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Schools;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.Absences.Commands.CreateLateArrival;

/// <summary>
/// Enregistre un retard, et — depuis l'Évolution N°5 — peut le faire VISER un cours d'emploi du temps : c'est
/// alors un billet d'entrée qui met à jour le registre d'appel de ce cours (EntryTicketRegister) et que
/// l'enseignant du cours pourra accepter. Sans cours visé, le comportement est exactement celui d'avant : le
/// statut du billet reste null et rien d'autre n'est touché.
/// </summary>
public class CreateLateArrivalCommandHandler(
    IApplicationDbContext _context,
    ITenantProvider _tenantProvider,
    IPublisher publisher,
    EntryTicketRegister register,
    WorkingDayGuard workingDayGuard)
    : IRequestHandler<CreateLateArrivalCommand, Guid>
{
    public async Task<Guid> Handle(CreateLateArrivalCommand request, CancellationToken cancellationToken)
    {
        var schoolId = _tenantProvider.CurrentSchoolId ?? throw new UnauthorizedAccessException("Tenant is required.");

        var student = await _context.Students.FindAsync(new object[] { request.StudentId }, cancellationToken);
        if (student == null)
            throw new NotFoundException(nameof(Student), request.StudentId.ToString());

        var lateArrival = new LateArrival
        {
            Id = Guid.NewGuid(),
            SchoolId = schoolId,
            StudentId = request.StudentId,
            Date = request.Date,
            Minutes = request.Minutes,
            Reason = request.Reason,
            Observations = request.Observations
        };

        AttendanceRecordedEvent? rectification = null;

        if (request.TargetScheduleSlotId is { } slotId)
        {
            const string field = nameof(request.TargetScheduleSlotId);
            var date = DateOnly.FromDateTime(request.Date);

            var slot = await _context.ScheduleSlots.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == slotId, cancellationToken)
                ?? throw new ValidationException([
                    new ValidationFailure(field, "Ce cours n'existe pas dans votre établissement.")
                ]);

            // Le cours doit être celui de la CLASSE de l'élève, un jour où il a lieu. La matière n'est pas
            // à comparer : c'est celle du cours (on passe donc la sienne).
            var mismatch = SlotPeriod.Mismatch(slot, student.ClassroomId, slot.SubjectId, date);
            if (mismatch is not null)
            {
                throw new ValidationException([new ValidationFailure(field, mismatch)]);
            }

            // Un jour de repos n'a pas d'appel : viser un cours ce jour-là n'aurait aucun sens. Un billet SANS
            // cours visé reste possible (arbitrage D4 de l'Évolution N°3) — c'est le cas d'un élève accueilli un
            // jour de repos, ou dans une classe qui n'a pas d'emploi du temps.
            await workingDayGuard.EnsureWorkingDayAsync(date, nameof(request.Date), cancellationToken);

            lateArrival.TargetScheduleSlotId = slot.Id;
            lateArrival.Status = EntryTicketStatus.Issued;

            rectification = await register.ApplyAsync(lateArrival, slot, date, schoolId, cancellationToken);
        }

        _context.LateArrivals.Add(lateArrival);

        // UN SEUL SaveChanges : le billet et la ligne d'appel qu'il modifie sont écrits ensemble. Un second
        // billet actif pour le même (élève, cours, jour) viole l'index unique et remonte en 409.
        await _context.SaveChangesAsync(cancellationToken);

        // Publié APRÈS l'enregistrement : la famille n'est jamais prévenue d'un retard qui aurait été rembobiné.
        if (rectification is not null)
        {
            await publisher.Publish(rectification, cancellationToken);
        }

        return lateArrival.Id;
    }
}
