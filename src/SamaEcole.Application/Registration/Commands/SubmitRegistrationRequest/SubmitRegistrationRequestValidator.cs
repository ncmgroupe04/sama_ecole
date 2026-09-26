using SamaEcole.Application.Common.Validation;
using SamaEcole.Application.Users.Common;
using SamaEcole.Domain.Enums;
using FluentValidation;

namespace SamaEcole.Application.Registration.Commands.SubmitRegistrationRequest;

/// <summary>
/// Ticket JGK-I01. Les longueurs maximales reflètent les contraintes de colonne (docs/Volume_3_DDS.md §5.7) :
/// mieux vaut un 422 explicite sur le bon champ qu'une DbUpdateException remontée en 500.
///
/// La politique de mot de passe (docs/Volume_7_Security.md §2) est la MÊME PasswordPolicy partagée que la
/// création/réinitialisation de compte (JGK-A05) : le mot de passe est CHOISI par le Directeur ici
/// (contrairement à JGK-B01 où il est généré), c'est donc un des rares endroits où un humain le saisit.
/// </summary>
public class SubmitRegistrationRequestValidator : AbstractValidator<SubmitRegistrationRequestCommand>
{
    public SubmitRegistrationRequestValidator()
    {
        RuleFor(x => x.DirectorFullName).NotEmpty().MaximumLength(200).NoHtml();
        RuleFor(x => x.DirectorEmail).NotEmpty().EmailAddress().MaximumLength(200).NoHtml();
        RuleFor(x => x.DirectorPhone).NotEmpty().MaximumLength(30).NoHtml().MustBeValidSenegalPhone();

        RuleFor(x => x.DirectorPassword).Custom((password, context) =>
        {
            foreach (var error in PasswordPolicy.Validate(
                password ?? string.Empty, context.InstanceToValidate.DirectorFullName, context.InstanceToValidate.SchoolName))
            {
                context.AddFailure(error);
            }
        });

        RuleFor(x => x.SchoolName).NotEmpty().MaximumLength(200).NoHtml();
        RuleFor(x => x.SchoolAddress).MaximumLength(300).NoHtml();
        RuleFor(x => x.City).MaximumLength(100).NoHtml();
        RuleFor(x => x.Region).MaximumLength(100).NoHtml();

        // Un effectif renseigné doit être plausible ; NULL reste autorisé (champ facultatif).
        RuleFor(x => x.EstimatedStudentCount)
            .GreaterThan(0).LessThanOrEqualTo(100_000)
            .When(x => x.EstimatedStudentCount.HasValue);

        // Profil tarifaire : un choix EXPLICITE de chaque champ (les propriétés sont nullables), puis la cohérence de la
        // combinaison avec la grille — public : un seul cycle, sans palier ; privé : palier de taille obligatoire.
        RuleFor(x => x.Ownership).NotNull().WithMessage("Précisez si votre établissement est public ou privé.")
            .IsInEnum();
        RuleFor(x => x.CycleProfile).NotNull().WithMessage("Précisez les cycles gérés par votre établissement.")
            .IsInEnum();
        RuleFor(x => x.SizeTier).IsInEnum().When(x => x.SizeTier.HasValue);

        RuleFor(x => x.SizeTier)
            .NotNull().WithMessage("Précisez la taille de votre établissement.")
            .When(x => x.Ownership == SchoolOwnership.Private);

        RuleFor(x => x.CycleProfile)
            .Must(profile => profile is SchoolCycleProfile.Primaire or SchoolCycleProfile.College or SchoolCycleProfile.Lycee)
            .WithMessage("Un établissement public gère un seul cycle : école élémentaire, collège (CEM) ou lycée.")
            .When(x => x.Ownership == SchoolOwnership.Public && x.CycleProfile.HasValue);
    }
}
