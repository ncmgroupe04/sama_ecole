using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.Auth.Commands.ChangeEmail;

/// <summary>
/// POST /api/v1/auth/change-email — l'utilisateur AUTHENTIFIÉ change lui-même son e-mail de connexion,
/// en prouvant qu'il connaît son mot de passe actuel. L'e-mail EST l'identifiant de login (JWT `sub`,
/// AGENTS.md règle #10) : le changer mérite exactement la même preuve d'identité qu'un changement de
/// mot de passe (ChangePasswordCommand) — une session volée ne doit pas suffire à en prendre le
/// contrôle en changeant l'adresse par laquelle on s'y reconnecte.
///
/// L'identité vient de ICurrentUserService (JWT), jamais d'un paramètre de requête (AGENTS.md règle
/// #10) : personne ne peut changer l'e-mail d'un autre compte par cette voie.
///
/// IAuditableRequest (JGK-H01) : même classement que ChangePasswordCommand — changer l'identifiant de
/// connexion d'un compte fait partie des écritures sensibles listées par le journal d'audit centralisé.
/// </summary>
public record ChangeUserEmailCommand(string NewEmail, string CurrentPassword)
    : IRequest<ChangeUserEmailResult>, IAuditableRequest;

public record ChangeUserEmailResult(string Email, int RevokedSessions);
