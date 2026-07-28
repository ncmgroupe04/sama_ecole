using Microsoft.AspNetCore.Authorization;

namespace SamaEcole.Web.Authorization;

/// <summary>
/// Ticket JGK-G02 — remplace le `[Authorize(Roles = "Directeur,Secretariat")]` codé en dur sur
/// PUT /schools/current/settings/grading-scale, POST /subjects et POST /grades/mentions. Le Directeur
/// passe toujours ; le Secrétariat ne passe que si SON école a activé la délégation
/// (SchoolSettings.AllowSecretaryToManageGrading) — voir CanManageGradingScaleHandler pour la règle.
/// </summary>
public class CanManageGradingScaleRequirement : IAuthorizationRequirement;
