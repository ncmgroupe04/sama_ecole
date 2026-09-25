using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.Users.Commands.UpdateUserProfile;

/// <summary>
/// PATCH /users/{id}/profile — corrige le nom complet et/ou l'e-mail d'un compte que LE DIRECTEUR a
/// créé (Secrétariat, Finance, Enseignant, Surveillant). Réservé au Directeur (UsersController).
///
/// Distinct de POST /auth/change-email : celui-ci est le changement EN LIBRE-SERVICE du compte
/// authentifié courant, qui doit prouver connaître son mot de passe actuel et révoque toutes ses
/// sessions (protection contre une session volée). Ici, c'est le DIRECTEUR qui corrige la fiche
/// D'AUTRUI — typiquement une faute de frappe repérée après coup sur un prénom ou un e-mail saisi à la
/// création (CreateUserCommand). Pas de vérification de mot de passe (l'acteur n'est pas le titulaire
/// du compte modifié). Sur SA PROPRE fiche, seul le nom complet est modifiable ici : le Directeur change
/// son propre e-mail par la voie sécurisée /auth/change-email (mot de passe requis, sessions révoquées),
/// jamais par ici — un e-mail différent de l'actuel est refusé (422).
/// </summary>
public record UpdateUserProfileCommand(Guid UserId, string FullName, string Email)
    : IRequest<UpdateUserProfileResult>, IAuditableRequest;

public record UpdateUserProfileResult(Guid UserId, string FullName, string Email);
