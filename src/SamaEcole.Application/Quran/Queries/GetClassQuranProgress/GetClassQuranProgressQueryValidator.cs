using FluentValidation;

namespace SamaEcole.Application.Quran.Queries.GetClassQuranProgress;

public class GetClassQuranProgressQueryValidator : AbstractValidator<GetClassQuranProgressQuery>
{
    public GetClassQuranProgressQueryValidator()
    {
        RuleFor(q => q.ClassroomId).NotEmpty();
    }
}
