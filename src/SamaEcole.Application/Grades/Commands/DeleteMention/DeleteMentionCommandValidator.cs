using FluentValidation;

namespace SamaEcole.Application.Grades.Commands.DeleteMention;

public class DeleteMentionCommandValidator : AbstractValidator<DeleteMentionCommand>
{
    public DeleteMentionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
