using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Teachers.Commands.CorrectTeacherMatricule;

public class CorrectTeacherMatriculeCommandValidator : AbstractValidator<CorrectTeacherMatriculeCommand>
{
    /// <summary>Longueur de la colonne <c>teachers.Matricule</c> (TeacherConfiguration : HasMaxLength(30)).</summary>
    private const int MaxMatriculeLength = 30;

    public CorrectTeacherMatriculeCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();

        RuleFor(x => x.NewMatricule)
            .NotEmpty().WithMessage("Le matricule est obligatoire.")
            .MaximumLength(MaxMatriculeLength)
            .WithMessage($"Le matricule ne peut pas dépasser {MaxMatriculeLength} caractères.")
            .NoHtml();
    }
}
