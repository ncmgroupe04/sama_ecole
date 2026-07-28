using Microsoft.AspNetCore.Authorization;

namespace SamaEcole.Web.Authorization;

/// <summary>
/// Remplace le `[Authorize(Roles = "Directeur")]` codé en dur sur PUT /finance/fees/{id}. Le
/// Directeur passe toujours ; la Finance ne passe que si SON école a activé la délégation
/// (SchoolSettings.AllowFinanceToModifyFees) — voir CanModifyFeesHandler pour la règle.
/// </summary>
public class CanModifyFeesRequirement : IAuthorizationRequirement;
