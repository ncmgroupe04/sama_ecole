using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Attendance.Queries.GetTeacherAttendances;

public record TeacherAttendanceDto
{
    public Guid Id { get; init; }
    public Guid TeacherId { get; init; }
    public string TeacherFullName { get; init; } = null!;
    public string TeacherMatricule { get; init; } = null!;
    public DateTime Date { get; init; }
    public AttendanceStatus Status { get; init; }
    public int LateMinutes { get; init; }
    public string? Reason { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public record GetTeacherAttendancesQuery(DateTime? Date) : IRequest<List<TeacherAttendanceDto>>;
