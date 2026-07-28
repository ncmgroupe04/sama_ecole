using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Attendance.Queries.GetAttendanceSheet;

public class GetAttendanceSheetQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetAttendanceSheetQuery, AttendanceSheetDto>
{
    public async Task<AttendanceSheetDto> Handle(GetAttendanceSheetQuery request, CancellationToken cancellationToken)
    {
        // Jointures toutes filtrées sur le même tenant (RLS + Global Query Filter) : aucune fuite
        // possible d'une classe, matière ou année d'une autre école.
        var header = await (
            from a in dbContext.AttendanceSheets.AsNoTracking()
            join c in dbContext.Classrooms.AsNoTracking() on a.ClassroomId equals c.Id
            join s in dbContext.Subjects.AsNoTracking() on a.SubjectId equals s.Id
            join y in dbContext.SchoolYears.AsNoTracking() on a.SchoolYearId equals y.Id
            where a.Id == request.SheetId
            select new
            {
                a.Id,
                a.ClassroomId,
                ClassroomName = c.Name,
                a.SubjectId,
                SubjectName = s.Name,
                a.SchoolYearId,
                SchoolYearLabel = y.Label,
                a.Date,
                a.Period
            }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Fiche d'appel {request.SheetId} introuvable.");

        var lines = await (
            from sa in dbContext.StudentAttendances.AsNoTracking()
            join st in dbContext.Students.AsNoTracking() on sa.StudentId equals st.Id
            where sa.AttendanceSheetId == request.SheetId
            orderby st.FullName
            select new AttendanceLineDto(
                sa.StudentId,
                st.Matricule,
                st.FullName,
                sa.Status.ToString(),
                sa.LateMinutes))
            .ToListAsync(cancellationToken);

        return new AttendanceSheetDto(
            header.Id,
            header.ClassroomId,
            header.ClassroomName,
            header.SubjectId,
            header.SubjectName,
            header.SchoolYearId,
            header.SchoolYearLabel,
            header.Date,
            header.Period,
            lines);
    }
}
