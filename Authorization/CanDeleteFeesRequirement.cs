using Microsoft.AspNetCore.Authorization;

namespace SamaEcole.Web.Authorization;

/// <summary>
/// Protège DELETE /finance/fee-categories/{id} et DELETE /finance/fees/{id}. Le Directeur passe
/// toujours ; la Finance ne passe que si SON école a activé la délégation
/// (SchoolSettings.AllowFinanceToDeleteFees) — voir CanDeleteFeesHandler pour la règle.
///
/// Distincte de CanModifyFeesRequirement : supprimer une catégorie entière est un geste plus lourd
/// de conséquences (elle disparaît de toute l'interface) qu'ajuster un montant, donc un commutateur
/// de délégation séparé.
/// </summary>
public class CanDeleteFeesRequirement : IAuthorizationRequirement;
