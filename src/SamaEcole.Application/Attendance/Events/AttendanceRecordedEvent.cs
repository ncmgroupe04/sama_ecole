using MediatR;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Attendance.Events;

public record AttendanceRecordedEvent(
    Guid SchoolId,
    Guid StudentId,
    Guid ClassroomId,
    Guid SubjectId,
    DateOnly Date,
    string Period,
    AttendanceStatus Status,
    int LateMinutes) : INotification;
