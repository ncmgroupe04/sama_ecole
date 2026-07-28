using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Attendance.Commands.CreateTeacherAttendance;

public record CreateTeacherAttendanceCommand : IRequest<Guid>
{
    public Guid TeacherId { get; init; }
    public DateTime Date { get; init; }
    public AttendanceStatus Status { get; init; }
    public int LateMinutes { get; init; }
    public string? Reason { get; init; }
}
