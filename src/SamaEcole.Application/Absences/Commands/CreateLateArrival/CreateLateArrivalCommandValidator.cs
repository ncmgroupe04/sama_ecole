using FluentValidation;

namespace SamaEcole.Application.Absences.Commands.CreateLateArrival;

public class CreateLateArrivalCommandValidator : AbstractValidator<CreateLateArrivalCommand>
{
    public CreateLateArrivalCommandValidator()
    {
        RuleFor(v => v.StudentId).NotEmpty();
        RuleFor(v => v.Date).NotEmpty();
        // Avec une heure d'arrivée, les minutes sont DÉDUITES par le serveur (0 possible : arrivée pile à l'heure d'un cours).
        RuleFor(v => v.Minutes).GreaterThan(0).When(v => v.ArrivalTime is null);

        // Avec un cours visé, les minutes deviennent celles d'une LIGNE D'APPEL en retard : même borne que la
        // saisie de l'appel (SubmitAttendanceSheetCommandValidator.MaxLateMinutes).
        RuleFor(v => v.Minutes)
            .LessThanOrEqualTo(Attendance.Commands.SubmitAttendanceSheet.SubmitAttendanceSheetCommandValidator.MaxLateMinutes)
            .When(v => v.TargetScheduleSlotId is not null && v.ArrivalTime is null)
            .WithMessage($"Un retard doit indiquer entre 1 et {Attendance.Commands.SubmitAttendanceSheet.SubmitAttendanceSheetCommandValidator.MaxLateMinutes} minutes.");
        RuleFor(v => v.Reason).NotEmpty().MaximumLength(1000);
        RuleFor(v => v.Observations).MaximumLength(1000);
    }
}
