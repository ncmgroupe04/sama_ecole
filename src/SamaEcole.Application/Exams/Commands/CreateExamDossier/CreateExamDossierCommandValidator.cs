using FluentValidation;

namespace SamaEcole.Application.Exams.Commands.CreateExamDossier;

public class CreateExamDossierCommandValidator : AbstractValidator<CreateExamDossierCommand>
{
    public CreateExamDossierCommandValidator()
    {
        RuleFor(x => x.ExamSessionId).NotEmpty();
        RuleFor(x => x.StudentId).NotEmpty();
        RuleFor(x => x.ClassroomId).NotEmpty();
    }
}
