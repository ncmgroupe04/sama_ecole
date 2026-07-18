using FluentValidation;

namespace SamaEcole.Application.Attendance.Queries.InitializeAttendanceSheet;

public class InitializeAttendanceSheetQueryValidator : AbstractValidator<InitializeAttendanceSheetQuery>
{
    public InitializeAttendanceSheetQueryValidator()
    {
        RuleFor(x => x.ClassroomId).NotEmpty();
        RuleFor(x => x.SubjectId).NotEmpty();
        RuleFor(x => x.Period).NotEmpty().MaximumLength(50);
    }
}
