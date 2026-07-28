using FluentValidation;

namespace SamaEcole.Application.Registration.Queries.GetRegistrationRequestStatus;

/// <summary>
/// Ticket JGK-I02. MaximumLength reflète la contrainte de colonne (varchar(20),
/// SchoolRegistrationRequestConfiguration) : une référence trop longue ne peut correspondre à
/// aucune ligne, autant le dire en 422 plutôt que de lancer une requête pour rien.
/// </summary>
public class GetRegistrationRequestStatusValidator : AbstractValidator<GetRegistrationRequestStatusQuery>
{
    public GetRegistrationRequestStatusValidator()
    {
        RuleFor(x => x.TrackingReference).NotEmpty().MaximumLength(20);
    }
}
