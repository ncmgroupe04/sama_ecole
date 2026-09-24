using FluentValidation;

namespace SamaEcole.Application.Quran.Commands.UpdateQuranEvaluation;

public class UpdateQuranEvaluationCommandValidator : AbstractValidator<UpdateQuranEvaluationCommand>
{
    public UpdateQuranEvaluationCommandValidator()
    {
        RuleFor(c => c.Id).NotEmpty();

        RuleFor(c => c.EvaluationDate)
            .NotEmpty()
            .LessThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("La date d'évaluation ne peut pas être future.");

        RuleFor(c => c.MemoryMistakes).GreaterThanOrEqualTo(0);
        RuleFor(c => c.TajwidMistakes).GreaterThanOrEqualTo(0);
        RuleFor(c => c.Hesitations).GreaterThanOrEqualTo(0);
        RuleFor(c => c.FinalScore).GreaterThanOrEqualTo(0).WithMessage("Une note ne peut pas être négative.");
    }
}
