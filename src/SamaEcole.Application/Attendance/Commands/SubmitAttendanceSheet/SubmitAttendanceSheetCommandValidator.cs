using SamaEcole.Application.Common.Validation;
using SamaEcole.Domain.Enums;
using FluentValidation;

namespace SamaEcole.Application.Attendance.Commands.SubmitAttendanceSheet;

public class SubmitAttendanceSheetCommandValidator : AbstractValidator<SubmitAttendanceSheetCommand>
{
    /// <summary>Borne haute des minutes de retard : un « retard » de plus de 4 h est une absence, pas un retard.</summary>
    public const int MaxLateMinutes = 240;

    public SubmitAttendanceSheetCommandValidator()
    {
        RuleFor(x => x.ClassroomId).NotEmpty();
        RuleFor(x => x.SubjectId).NotEmpty();
        RuleFor(x => x.Period).NotEmpty().MaximumLength(50).NoHtml();

        RuleFor(x => x.Date)
            .LessThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("La date de l'appel ne peut pas être dans le futur.");

        RuleFor(x => x.Entries).NotEmpty()
            .WithMessage("L'appel doit contenir au moins un élève.");

        // Un même élève ne peut pas figurer deux fois dans le même appel : la contrainte d'unicité en
        // base le refuserait, mais le signaler en 422 sur le bon champ est plus lisible qu'un 409.
        RuleFor(x => x.Entries)
            .Must(entries => entries.Select(e => e.StudentId).Distinct().Count() == entries.Count)
            .When(x => x.Entries.Count > 0)
            .WithMessage("Un même élève figure plusieurs fois dans l'appel.");

        RuleForEach(x => x.Entries).ChildRules(entry =>
        {
            entry.RuleFor(e => e.StudentId).NotEmpty();
            entry.RuleFor(e => e.Status).IsInEnum();

            // Un retard PORTE un nombre de minutes strictement positif ; tout autre statut n'en porte
            // aucun. C'est la traduction, à la saisie, de l'invariant de StudentAttendance.
            entry.RuleFor(e => e.LateMinutes)
                .InclusiveBetween(1, MaxLateMinutes)
                .When(e => e.Status == AttendanceStatus.Late)
                .WithMessage($"Un retard doit indiquer entre 1 et {MaxLateMinutes} minutes.");

            entry.RuleFor(e => e.LateMinutes)
                .Equal(0)
                .When(e => e.Status != AttendanceStatus.Late)
                .WithMessage("Les minutes de retard ne s'appliquent qu'à un statut « Retard ».");
        });
    }
}
