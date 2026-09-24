using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Schools;
using SamaEcole.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Attendance.Commands.CreateTeacherAttendance;

public class CreateTeacherAttendanceCommandHandler(
    IApplicationDbContext _context,
    ITenantProvider _tenantProvider,
    WorkingDayGuard _workingDayGuard)
    : IRequestHandler<CreateTeacherAttendanceCommand, Guid>
{
    public async Task<Guid> Handle(CreateTeacherAttendanceCommand request, CancellationToken cancellationToken)
    {
        var schoolId = _tenantProvider.CurrentSchoolId ?? throw new UnauthorizedAccessException("Tenant is required.");

        var teacherExists = await _context.Teachers.FindAsync(new object[] { request.TeacherId }, cancellationToken);
        if (teacherExists == null)
            throw new NotFoundException(nameof(Teacher), request.TeacherId.ToString());

        // Jour de repos de l'établissement (Évolution N°3) : aucun pointage enseignant ne s'y saisit.
        await _workingDayGuard.EnsureWorkingDayAsync(DateOnly.FromDateTime(request.Date), nameof(request.Date), cancellationToken);

        var existingRecord = await _context.TeacherAttendances
            .FirstOrDefaultAsync(t => t.TeacherId == request.TeacherId && t.Date.Date == request.Date.Date, cancellationToken);
            
        if (existingRecord != null)
        {
            throw new InvalidOperationException($"Un pointage existe déjà pour l'enseignant {teacherExists.FullName} à la date du {request.Date:dd/MM/yyyy}");
        }

        var record = new TeacherAttendance
        {
            Id = Guid.NewGuid(),
            SchoolId = schoolId,
            TeacherId = request.TeacherId,
            Date = request.Date,
            Status = request.Status,
            LateMinutes = request.LateMinutes,
            Reason = request.Reason
        };

        _context.TeacherAttendances.Add(record);
        await _context.SaveChangesAsync(cancellationToken);

        return record.Id;
    }
}
