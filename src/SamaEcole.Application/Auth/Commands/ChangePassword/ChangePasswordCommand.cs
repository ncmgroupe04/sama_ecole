using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.Auth.Commands.ChangePassword;

/// <summary>
/// POST /api/v1/auth/change-password — l'utilisateur AUTHENTIFIÉ change lui-même son mot de passe, en
/// prouvant qu'il connaît l'actuel. Distinct des deux autres routes qui touchent au mot de passe :
/// ResetPasswordCommand (jeton reçu par e-mail, compte pas authentifié, mot de passe oublié) et
/// ResetUserPasswordCommand (un Directeur fixe le mot de passe d'AUTRUI, sans le connaître). Les
/// trois existent pour des acteurs et des preuves d'identité différents — aucun ne remplace les
/// autres.
///
/// L'identité vient de ICurrentUserService (JWT), jamais d'un paramètre de requête (AGENTS.md règle
/// #10) : personne ne peut changer le mot de passe d'un autre compte par cette voie.
///
/// IAuditableRequest (JGK-H01) : même classement que ResetUserPasswordCommand — un changement de mot
/// de passe fait partie des écritures sensibles listées par le journal d'audit centralisé.
/// </summary>
public record ChangePasswordCommand(string CurrentPassword, string NewPassword)
    : IRequest<ChangePasswordResult>, IAuditableRequest;

public record ChangePasswordResult(int RevokedSessions);
