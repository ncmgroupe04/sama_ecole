using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Teachers.Commands.CreateTeacher;

public class CreateTeacherCommandValidator : AbstractValidator<CreateTeacherCommand>
{
    public CreateTeacherCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200).NoHtml();

        // EmailAddress() (mode ASP.NET Core) vérifie peu de choses au-delà du « @ » : sans NoHtml,
        // « <script>@x.com » passerait (JGK-F01).
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(255).NoHtml();
        RuleFor(x => x.Phone).MaximumLength(30).NoHtml();

        RuleFor(x => x.SubjectIds).NotEmpty()
            .WithMessage("Au moins une matière est requise.");

        RuleFor(x => x.SubjectIds)
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .When(x => x.SubjectIds.Count > 0)
            .WithMessage("Une même matière ne peut être indiquée deux fois.");
    }
}
