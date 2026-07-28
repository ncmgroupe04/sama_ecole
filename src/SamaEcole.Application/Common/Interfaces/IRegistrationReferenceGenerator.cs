namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Génère la référence de suivi d'une demande d'inscription self-service (ticket JGK-I01).
///
/// Elle est communiquée au Directeur pour suivre sa demande SANS authentification (ticket JGK-I02) :
/// elle doit donc être imprévisible (tirée d'un CSPRNG, jamais de l'horloge — sinon un tiers pourrait
/// énumérer les demandes des autres) autant qu'unique. L'unicité finale est garantie par l'index unique
/// de la base ; ce générateur fournit assez d'entropie pour qu'une collision soit négligeable.
/// </summary>
public interface IRegistrationReferenceGenerator
{
    string Generate();
}
