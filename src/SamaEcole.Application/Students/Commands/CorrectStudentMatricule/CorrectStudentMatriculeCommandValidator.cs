using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Students.Commands.CorrectStudentMatricule;

public class CorrectStudentMatriculeCommandValidator : AbstractValidator<CorrectStudentMatriculeCommand>
{
    /// <summary>
    /// Longueur de la colonne <c>students.Matricule</c> (StudentConfiguration : HasMaxLength(30)) —
    /// et non les 50 caractères que valide <c>MatriculeFormat</c> pour le rendu du gabarit.
    /// </summary>
    private const int MaxMatriculeLength = 30;

    public CorrectStudentMatriculeCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();

        RuleFor(x => x.NewMatricule)
            .NotEmpty().WithMessage("Le matricule est obligatoire.")
            .MaximumLength(MaxMatriculeLength)
            .WithMessage($"Le matricule ne peut pas dépasser {MaxMatriculeLength} caractères.")
            .NoHtml();
    }
}
