using FluentValidation;

namespace SamaEcole.Application.Discipline.Commands.CreateDisciplineRecord;

public class CreateDisciplineRecordCommandValidator : AbstractValidator<CreateDisciplineRecordCommand>
{
    public CreateDisciplineRecordCommandValidator()
    {
        RuleFor(v => v.StudentId).NotEmpty();
        RuleFor(v => v.Date).NotEmpty();
        RuleFor(v => v.Type).IsInEnum();
        RuleFor(v => v.Reason).NotEmpty().MaximumLength(1000);
    }
}
