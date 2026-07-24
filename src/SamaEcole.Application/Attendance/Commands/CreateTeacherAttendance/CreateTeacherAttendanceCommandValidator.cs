using FluentValidation;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Attendance.Commands.CreateTeacherAttendance;

public class CreateTeacherAttendanceCommandValidator : AbstractValidator<CreateTeacherAttendanceCommand>
{
    public CreateTeacherAttendanceCommandValidator()
    {
        RuleFor(v => v.TeacherId).NotEmpty();
        RuleFor(v => v.Date).NotEmpty();
        RuleFor(v => v.Status).IsInEnum();
        
        When(v => v.Status == AttendanceStatus.Late, () => {
            RuleFor(v => v.LateMinutes).GreaterThan(0).WithMessage("Le retard doit être supérieur à 0 minute.");
        }).Otherwise(() => {
            RuleFor(v => v.LateMinutes).Equal(0).WithMessage("Un statut autre que 'Retard' doit avoir 0 minute de retard.");
        });

        RuleFor(v => v.Reason).MaximumLength(1000);
    }
}
