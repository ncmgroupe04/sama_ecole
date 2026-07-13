using FluentValidation;

namespace Jangalekat.Application.Students.Commands.CreateStudent;

public class CreateStudentCommandValidator : AbstractValidator<CreateStudentCommand>
{
    public CreateStudentCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Gender).Must(g => g is "M" or "F").WithMessage("Le genre doit être 'M' ou 'F'.");
        RuleFor(x => x.ClassroomId).NotEmpty();
        RuleFor(x => x.BirthDate).LessThan(DateOnly.FromDateTime(DateTime.UtcNow));
    }
}
