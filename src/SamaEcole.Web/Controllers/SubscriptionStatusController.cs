using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Statut d'abonnement (facturation) de l'établissement courant — lecture pour TOUS les rôles de
/// l'école, utilisée par la bannière d'alerte d'expiration (tous les écrans). Distinct de
/// /subscriptions/{schoolId}/payments (réservé au Directeur) et de /onboarding/subscription
/// (TenantSubscription, configuration produit).
/// </summary>
[ApiController]
[Route("api/v1/subscription-status")]
[Authorize]
public class SubscriptionStatusController(IApplicationDbContext dbContext, ITenantProvider tenantProvider) : ControllerBase
{
    public record SubscriptionStatusDto(string Status, DateOnly? ExpiresAt);

    [HttpGet]
    [ProducesResponseType<SubscriptionStatusDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var sub = await dbContext.Subscriptions.AsNoTracking()
            .Where(s => s.SchoolId == schoolId)
            .Select(s => new { s.Status, s.ExpiresAt })
            .FirstOrDefaultAsync(cancellationToken);

        if (sub is null)
        {
            return NotFound();
        }

        return Ok(new SubscriptionStatusDto(sub.Status.ToString(), sub.ExpiresAt));
    }
}