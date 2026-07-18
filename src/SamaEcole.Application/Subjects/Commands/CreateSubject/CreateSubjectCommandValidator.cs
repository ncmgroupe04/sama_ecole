using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Subjects.Commands.CreateSubject;

public class CreateSubjectCommandValidator : AbstractValidator<CreateSubjectCommand>
{
    public CreateSubjectCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80).NoHtml();

        // Niveau LIBRE, comme pour les classes (openapi.yaml) : primaire, collège et lycée ne
        // découpent pas leur scolarité de la même façon. Imposer une énumération exclurait des écoles.
        RuleFor(x => x.Level).NotEmpty().MaximumLength(50).NoHtml();

        RuleFor(x => x.Coefficient)
            .GreaterThan(0).WithMessage("Le coefficient doit être supérieur à zéro.")
            .LessThanOrEqualTo(20).WithMessage("Le coefficient annoncé semble irréaliste (maximum 20).");
    }
}
