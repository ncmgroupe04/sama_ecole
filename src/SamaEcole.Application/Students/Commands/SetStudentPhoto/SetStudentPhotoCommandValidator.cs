using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Students.Commands.SetStudentPhoto;

public class SetStudentPhotoCommandValidator : AbstractValidator<SetStudentPhotoCommand>
{
    public SetStudentPhotoCommandValidator()
    {
        RuleFor(x => x.StudentId).NotEmpty();
        RuleFor(x => x.PhotoDataBase64).MustBeValidPhotoData();
    }
}
