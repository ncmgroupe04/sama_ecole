using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Teachers.Commands.SetTeacherPhoto;

public class SetTeacherPhotoCommandValidator : AbstractValidator<SetTeacherPhotoCommand>
{
    public SetTeacherPhotoCommandValidator()
    {
        RuleFor(x => x.TeacherId).NotEmpty();
        RuleFor(x => x.PhotoDataBase64).MustBeValidPhotoData();
    }
}
