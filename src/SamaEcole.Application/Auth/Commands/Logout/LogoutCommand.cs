using MediatR;

namespace SamaEcole.Application.Auth.Commands.Logout;

/// <summary>
/// POST /api/v1/auth/logout — openapi.yaml (endpoint authentifié, réponse 204, aucun corps).
///
/// L'utilisateur est déduit du JWT (ICurrentUserService), jamais d'un identifiant fourni par le
/// client : sans quoi n'importe qui pourrait déconnecter n'importe qui.
/// </summary>
public record LogoutCommand : IRequest;