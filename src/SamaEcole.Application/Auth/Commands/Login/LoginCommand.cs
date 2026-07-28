using MediatR;

namespace SamaEcole.Application.Auth.Commands.Login;

/// <summary>POST /api/v1/auth/login — openapi.yaml.</summary>
public record LoginCommand : IRequest<AuthTokensResult>
{
    public required string Email { get; init; }
    public required string Password { get; init; }
}