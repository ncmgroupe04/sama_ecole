using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Schools.Commands.CreateSchool;

public class CreateSchoolCommandValidator : AbstractValidator<CreateSchoolCommand>
{
    public CreateSchoolCommandValidator()
    {
        RuleFor(c => c.Name)
            .NotEmpty().WithMessage("Le nom de l'établissement est obligatoire.")
            .MaximumLength(200)
            .NoHtml();

        RuleFor(c => c.Address)
            .NotEmpty().WithMessage("L'adresse est obligatoire.")
            .MaximumLength(300)
            .NoHtml();

        RuleFor(c => c.Phone)
            .MaximumLength(30).NoHtml()
            .When(c => !string.IsNullOrWhiteSpace(c.Phone));

        RuleFor(c => c.DirectorEmail)
            .NotEmpty().WithMessage("L'e-mail du Directeur est obligatoire.")
            .EmailAddress().WithMessage("L'e-mail du Directeur est invalide.")
            .MaximumLength(255)
            .NoHtml();

        RuleFor(c => c.DirectorFullName)
            .MaximumLength(200).NoHtml()
            .When(c => !string.IsNullOrWhiteSpace(c.DirectorFullName));
    }
}
