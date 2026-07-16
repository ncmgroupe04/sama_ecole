using MediatR;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Users.Queries.GetUsers;

/// <summary>GET /users — comptes de l'établissement courant (écran de gestion des utilisateurs, Directeur).</summary>
public record GetUsersQuery : IRequest<IReadOnlyList<UserListItem>>;

/// <summary>
/// IsSelf : calculé côté serveur plutôt que laissé au client à déduire du JWT — l'écran s'en sert
/// pour masquer les actions de blocage/réinitialisation sur la propre ligne de l'acteur (règles déjà
/// appliquées côté serveur par ChangeUserStatus/ResetUserPassword, ceci n'est qu'un confort d'affichage).
/// </summary>
public record UserListItem(Guid Id, string FullName, string Email, Role Role, EntityStatus Status, bool IsSelf);
