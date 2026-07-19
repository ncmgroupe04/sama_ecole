using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Subjects.Commands.UpdateSubject;

public class UpdateSubjectCommandValidator : AbstractValidator<UpdateSubjectCommand>
{
    public UpdateSubjectCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80).NoHtml();

        // Niveau LIBRE, comme à la création : aucune liste figée de niveaux.
        RuleFor(x => x.Level).NotEmpty().MaximumLength(50).NoHtml();

        RuleFor(x => x.Coefficient)
            .GreaterThan(0).WithMessage("Le coefficient doit être supérieur à zéro.")
            .LessThanOrEqualTo(20).WithMessage("Le coefficient annoncé semble irréaliste (maximum 20).");
    }
}
