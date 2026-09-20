using FluentValidation;

namespace SamaEcole.Application.Quran.Queries.GetStudentQuranProgress;

public class GetStudentQuranProgressQueryValidator : AbstractValidator<GetStudentQuranProgressQuery>
{
    public GetStudentQuranProgressQueryValidator()
    {
        RuleFor(q => q.StudentId).NotEmpty();
    }
}
