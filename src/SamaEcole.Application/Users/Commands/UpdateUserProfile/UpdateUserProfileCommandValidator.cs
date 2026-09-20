using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Users.Commands.UpdateUserProfile;

public class UpdateUserProfileCommandValidator : AbstractValidator<UpdateUserProfileCommand>
{
    public UpdateUserProfileCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200).NoHtml();
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256).NoHtml();
    }
}
