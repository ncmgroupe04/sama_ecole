using FluentValidation;

namespace SamaEcole.Application.Grades.Commands.CreateMention;

public class CreateMentionCommandValidator : AbstractValidator<CreateMentionCommand>
{
    public CreateMentionCommandValidator()
    {
        RuleFor(c => c.Label).NotEmpty().MaximumLength(40);
        RuleFor(c => c.MinAverage).GreaterThanOrEqualTo(0).WithMessage("Le seuil ne peut pas être négatif.");
    }
}
