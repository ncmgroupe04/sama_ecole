using FluentValidation;

namespace SamaEcole.Application.Attendance.Queries.InitializeAttendanceSheet;

public class InitializeAttendanceSheetQueryValidator : AbstractValidator<InitializeAttendanceSheetQuery>
{
    public InitializeAttendanceSheetQueryValidator()
    {
        RuleFor(x => x.ClassroomId).NotEmpty();
        RuleFor(x => x.SubjectId).NotEmpty();
        RuleFor(x => x.Period).NotEmpty().When(x => x.ScheduleSlotId is null);
        RuleFor(x => x.Period).MaximumLength(50);
    }
}
