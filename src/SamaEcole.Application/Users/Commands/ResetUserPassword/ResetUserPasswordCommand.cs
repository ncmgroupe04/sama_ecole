using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.Users.Commands.ResetUserPassword;

/// <summary>
/// PATCH /users/{userId}/password — le Directeur fixe directement un nouveau mot de passe pour un
/// compte de son établissement (pas de lien par e-mail : voir CreateUserCommand pour le même choix).
/// Toutes les sessions actives du compte sont révoquées, comme un blocage (JGK-A05) : un mot de passe
/// qu'on vient de changer ne doit pas laisser une session déjà ouverte utilisable sous l'ancien.
///
/// IAuditableRequest (JGK-H01) : « changement de mot de passe » fait partie des écritures sensibles
/// explicitement listées par le journal d'audit centralisé (docs/Volume_7_Security.md §7).
/// </summary>
public record ResetUserPasswordCommand(Guid UserId, string NewPassword)
    : IRequest<ResetUserPasswordResult>, IAuditableRequest;

public record ResetUserPasswordResult(Guid UserId, int RevokedSessions);
