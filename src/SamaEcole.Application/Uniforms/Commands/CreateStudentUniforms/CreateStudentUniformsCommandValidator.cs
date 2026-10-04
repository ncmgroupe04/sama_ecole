using FluentValidation;

namespace SamaEcole.Application.Uniforms.Commands.CreateStudentUniforms;

public class CreateStudentUniformsCommandValidator : AbstractValidator<CreateStudentUniformsCommand>
{
    public const int MaxQuantity = 20;
    public const int MaxLines = 30;

    public CreateStudentUniformsCommandValidator()
    {
        RuleFor(x => x.EnrollmentId).NotEmpty();

        RuleFor(x => x.Lines)
            .NotEmpty().WithMessage("Indiquez au moins une tenue.")
            .Must(lines => lines.Count <= MaxLines).WithMessage($"Une demande ne peut pas dépasser {MaxLines} lignes.")
            .Must(lines => lines.Select(l => l.UniformItemId).Distinct().Count() == lines.Count)
            .WithMessage("Un même modèle ne peut figurer qu'une fois par demande : la quantité porte le nombre d'exemplaires.");

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.UniformItemId).NotEmpty();
            line.RuleFor(l => l.UniformVariantId).NotEqual(Guid.Empty).When(l => l.UniformVariantId.HasValue);
            line.RuleFor(l => l.Quantity).InclusiveBetween(1, MaxQuantity)
                .WithMessage($"La quantité doit être comprise entre 1 et {MaxQuantity}.");
        });
    }
}
