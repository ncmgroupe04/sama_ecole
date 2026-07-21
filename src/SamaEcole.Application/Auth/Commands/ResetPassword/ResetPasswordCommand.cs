using MediatR;

namespace SamaEcole.Application.Auth.Commands.ResetPassword;

/// <summary>
/// POST /api/v1/auth/reset-password (docs/Volume_4_API_Design.md §1) — applique un nouveau mot de
/// passe à partir du jeton reçu par e-mail. Route ANONYME : le jeton fait office d'authentification.
///
/// Renvoie le nombre de sessions coupées, comme la voie administrative
/// (ResetUserPasswordResult) : l'utilisateur doit savoir que ses autres appareils ont été déconnectés.
/// </summary>
public record ResetPasswordCommand(string Token, string NewPassword) : IRequest<ResetPasswordResult>;

public record ResetPasswordResult(int RevokedSessions);
