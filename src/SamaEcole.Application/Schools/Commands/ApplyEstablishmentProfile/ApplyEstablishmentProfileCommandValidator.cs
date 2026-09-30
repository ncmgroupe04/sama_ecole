using SamaEcole.Domain.Enums;
using FluentValidation;

namespace SamaEcole.Application.Schools.Commands.ApplyEstablishmentProfile;

public class ApplyEstablishmentProfileCommandValidator : AbstractValidator<ApplyEstablishmentProfileCommand>
{
    public ApplyEstablishmentProfileCommandValidator()
    {
        RuleFor(c => c.Profile)
            .Must(profile => Enum.TryParse<ProfileEtablissement>(profile, ignoreCase: true, out _))
            .WithMessage("Profil d'établissement invalide.");
    }
}
