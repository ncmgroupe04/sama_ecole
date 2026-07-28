using MediatR;

namespace SamaEcole.Application.Auth.Commands.ForgotPassword;

/// <summary>
/// POST /api/v1/auth/forgot-password (docs/Volume_4_API_Design.md §1) — demande un lien de
/// réinitialisation. Route ANONYME : celui qui a oublié son mot de passe ne peut pas s'authentifier.
///
/// Ne renvoie RIEN d'exploitable, à dessein : voir ForgotPasswordCommandHandler.
/// </summary>
public record ForgotPasswordCommand(string Email) : IRequest;
