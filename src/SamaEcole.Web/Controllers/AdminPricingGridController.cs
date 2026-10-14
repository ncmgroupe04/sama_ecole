using SamaEcole.Application.Subscriptions.Pricing;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Gestion de la grille tarifaire — écran /admin/tarification. RÉSERVÉ AU SUPER ADMIN : `quota_tier_prices`
/// est une table plateforme sans SchoolId.
/// </summary>
[ApiController]
[Route("api/v1/admin/pricing-grid")]
[Authorize(Roles = nameof(Role.SuperAdmin))]
public class AdminPricingGridController(ISender mediator) : ControllerBase
{
    public record UpdateQuotaTierPriceRequest(
        decimal MonthlyXof,
        decimal AnnualXof,
        decimal BoardingSurchargeMonthlyXof,
        decimal BoardingSurchargeAnnualXof);

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AdminPricingGridTierDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetAdminPricingGridQuery(), cancellationToken));

    [HttpPut("{tier}")]
    [ProducesResponseType<AdminPricingGridTierDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        StudentQuotaTier tier, [FromBody] UpdateQuotaTierPriceRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new UpdateQuotaTierPriceCommand
            {
                Tier = tier,
                MonthlyXof = request.MonthlyXof,
                AnnualXof = request.AnnualXof,
                BoardingSurchargeMonthlyXof = request.BoardingSurchargeMonthlyXof,
                BoardingSurchargeAnnualXof = request.BoardingSurchargeAnnualXof
            },
            cancellationToken));
}
