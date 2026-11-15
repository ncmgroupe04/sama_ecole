using FluentValidation;

namespace SamaEcole.Application.Absences.Commands.CreateAbsenceJustification;

public class CreateAbsenceJustificationCommandValidator : AbstractValidator<CreateAbsenceJustificationCommand>
{
    /// <summary>Plafond d'un billet : au-delà, c'est une situation (maladie longue, exclusion) à traiter autrement.</summary>
    public const int MaxDays = 60;

    public CreateAbsenceJustificationCommandValidator()
    {
        RuleFor(v => v.StudentId).NotEmpty();
        RuleFor(v => v.StartDate).NotEqual(default(DateOnly)).WithMessage("La date de début est obligatoire.");
        RuleFor(v => v.Reason).NotEmpty().WithMessage("Le motif est obligatoire.").MaximumLength(1000);

        RuleFor(v => v.EndDate)
            .Must((v, end) => end is null || end.Value >= v.StartDate)
            .WithMessage("La date de fin ne peut pas précéder la date de début.");

        RuleFor(v => v)
            .Must(v => (v.EndDate ?? v.StartDate).DayNumber - v.StartDate.DayNumber < MaxDays)
            .WithName("EndDate")
            .WithMessage($"Un billet d'absence couvre au plus {MaxDays} jours.");

        RuleFor(v => v.AuthorizedReturnDate)
            .Must((v, back) => back is null || back.Value > (v.EndDate ?? v.StartDate))
            .WithMessage("La date de retour autorisée doit suivre le dernier jour d'absence.");
    }
}
