using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Émet l'access token JWT. Claims obligatoires : <c>sub</c>, <c>schoolId</c>, <c>role</c>
/// (AGENTS.md règle #10, docs/Volume_4_API_Design.md §1.3). Le <c>schoolId</c> vient TOUJOURS
/// du compte en base, jamais d'une donnée fournie par le client.
/// </summary>
public interface IJwtTokenGenerator
{
    AccessToken Generate(Guid userId, Guid? schoolId, Role role);
}

/// <param name="ExpiresInSeconds">Durée de vie, exposée telle quelle dans AuthTokens.expiresIn (openapi.yaml).</param>
public record AccessToken(string Value, int ExpiresInSeconds);