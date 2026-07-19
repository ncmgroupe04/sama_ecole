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

    /// <summary>
    /// Console Super Admin (bouton « Infiltrer ») : jeton de MÊME format qu'un jeton normal (mêmes
    /// claims obligatoires sub/schoolId/role, AGENTS.md règle #10 — la RLS et [Authorize(Roles=...)]
    /// ne voient donc aucune différence), avec en plus le claim <c>impersonatedBy</c> qui trace l'acteur
    /// réel. Toujours plus court qu'un jeton normal et jamais accompagné d'un refresh token : une
    /// session d'impersonation ne se prolonge pas, elle expire pour de bon (docs/Volume_7_Security.md).
    /// </summary>
    AccessToken GenerateImpersonation(Guid targetUserId, Guid targetSchoolId, Role targetRole, Guid impersonatedByUserId);
}

/// <param name="ExpiresInSeconds">Durée de vie, exposée telle quelle dans AuthTokens.expiresIn (openapi.yaml).</param>
public record AccessToken(string Value, int ExpiresInSeconds);