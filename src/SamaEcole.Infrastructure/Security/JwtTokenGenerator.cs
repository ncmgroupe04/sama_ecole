using System.Security.Claims;
using System.Text;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SamaEcole.Infrastructure.Security;

/// <summary>
/// Émission de l'access token (ticket JGK-A04).
///
/// Claims émis : <c>sub</c>, <c>schoolId</c>, <c>role</c> (AGENTS.md règle #10). Ce sont les noms
/// BRUTS attendus par TenantProvider et CurrentUserService — d'où MapInboundClaims = false côté
/// validation (SamaEcole.Web/Program.cs), sinon ASP.NET renommerait `sub` en `nameidentifier` et le
/// tenant ne serait plus résolu.
///
/// Un Super Admin n'a pas d'établissement : le claim schoolId est alors ABSENT (et non vide), ce qui
/// fait retourner null à TenantProvider — la RLS ne laisse alors passer aucune donnée d'école, ce qui
/// est le comportement voulu (docs/Volume_4_API_Design.md §1.3).
/// </summary>
public class JwtTokenGenerator(IOptions<JwtOptions> options, TimeProvider timeProvider) : IJwtTokenGenerator
{
    public AccessToken Generate(Guid userId, string fullName, Guid? schoolId, Role role)
        => Generate(userId, fullName, schoolId, role, impersonatedByUserId: null);

    /// <summary>Console Super Admin (bouton « Infiltrer ») — voir IJwtTokenGenerator.GenerateImpersonation.</summary>
    public AccessToken GenerateImpersonation(Guid targetUserId, string fullName, Guid targetSchoolId, Role targetRole, Guid impersonatedByUserId)
        => Generate(targetUserId, fullName, targetSchoolId, targetRole, impersonatedByUserId);

    private AccessToken Generate(Guid userId, string fullName, Guid? schoolId, Role role, Guid? impersonatedByUserId)
    {
        var settings = options.Value;

        if (string.IsNullOrWhiteSpace(settings.SigningKey))
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey est manquant : impossible d'émettre un token. Voir .env.example.");
        }

        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(settings.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new("role", role.ToString()),
            new("name", fullName),
            // jti : identifiant unique du token, indispensable pour tracer/révoquer un access token précis.
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString())
        };

        if (schoolId is { } school)
        {
            claims.Add(new Claim("schoolId", school.ToString()));
        }

        if (impersonatedByUserId is { } superAdminId)
        {
            claims.Add(new Claim("impersonatedBy", superAdminId.ToString()));
        }

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = credentials
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);

        return new AccessToken(token, (int)(expiresAt - now).TotalSeconds);
    }
}
