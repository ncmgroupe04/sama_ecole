using MediatR;

namespace SamaEcole.Application.Auth.Commands.Refresh;

/// <summary>POST /api/v1/auth/refresh — openapi.yaml.</summary>
public record RefreshTokenCommand : IRequest<AuthTokensResult>
{
    public required string RefreshToken { get; init; }
}