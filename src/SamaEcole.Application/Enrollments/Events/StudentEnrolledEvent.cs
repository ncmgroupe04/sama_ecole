using MediatR;

namespace SamaEcole.Application.Enrollments.Events;

/// <summary>
/// Ticket JGK-E03. Publié par <c>CreateEnrollmentCommandHandler</c> après un
/// <c>SaveChangesAsync</c> réussi — jamais avant, l'événement ne doit pas partir si la transaction
/// échoue. Même philosophie que <see cref="SamaEcole.Application.Attendance.Events.AttendanceRecordedEvent"/> :
/// un événement minimal, pas l'entité complète. Le handler consommateur recharge lui-même tout ce
/// dont il a besoin via <see cref="SamaEcole.Application.Common.Interfaces.IApplicationDbContext"/>.
/// </summary>
public record StudentEnrolledEvent(
    Guid SchoolId,
    Guid StudentId,
    Guid EnrollmentId,
    string StudentFullName,
    string Matricule,
    DateTimeOffset EnrolledAt) : INotification;
