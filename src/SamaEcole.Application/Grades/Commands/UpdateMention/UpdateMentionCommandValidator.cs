using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Grades.Commands.UpdateMention;

public class UpdateMentionCommandValidator : AbstractValidator<UpdateMentionCommand>
{
    public UpdateMentionCommandValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Label).NotEmpty().MaximumLength(40).NoHtml();
        RuleFor(c => c.MinAverage).GreaterThanOrEqualTo(0).WithMessage("Le seuil ne peut pas être négatif.");
    }
}
