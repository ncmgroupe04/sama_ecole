using SamaEcole.Application.Common.Validation;
using SamaEcole.Application.Users.Common;
using SamaEcole.Domain.Enums;
using FluentValidation;

namespace SamaEcole.Application.Users.Commands.CreateUser;

public class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    /// <summary>
    /// SuperAdmin est un rôle plateforme, hors du contrôle d'une école. Directeur ne se crée que via
    /// JGK-B01 (création d'établissement) : permettre à un Directeur d'en créer un autre par cette
    /// voie serait une escalade de privilège non demandée et non auditée comme telle.
    /// </summary>
    private static readonly Role[] AssignableRoles = [Role.Secretariat, Role.Finance, Role.Enseignant];

    public CreateUserCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200).NoHtml();

        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256).NoHtml();

        RuleFor(x => x.Role)
            .Must(role => AssignableRoles.Contains(role))
            .WithMessage("Rôle non assignable ici : Secrétariat, Finance ou Enseignant uniquement.");

        RuleFor(x => x.Password).Custom((password, context) =>
        {
            foreach (var error in PasswordPolicy.Validate(password ?? string.Empty, context.InstanceToValidate.FullName))
            {
                context.AddFailure(error);
            }
        });
    }
}
