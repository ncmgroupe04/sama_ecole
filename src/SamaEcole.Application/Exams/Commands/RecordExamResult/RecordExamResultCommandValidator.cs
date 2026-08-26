using FluentValidation;

namespace SamaEcole.Application.Exams.Commands.RecordExamResult;

public class RecordExamResultCommandValidator : AbstractValidator<RecordExamResultCommand>
{
    public RecordExamResultCommandValidator()
    {
        RuleFor(x => x.ExamDossierId).NotEmpty();
        RuleFor(x => x.DeliberatedOn).NotEmpty();

        RuleFor(x => x.AverageScore)
            .GreaterThanOrEqualTo(0)
            .When(x => x.AverageScore.HasValue);

        // Une mention ne se conçoit que pour un admis — le CFEE (qui n'attribue aucune mention) se
        // vérifie côté Handler, seul endroit qui connaît le type d'examen de la session.
        RuleFor(x => x.Mention)
            .Null()
            .When(x => !x.IsAdmitted)
            .WithMessage("Une mention ne peut être saisie que pour un candidat admis.");
    }
}
