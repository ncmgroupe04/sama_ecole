using SamaEcole.Application.Platform.Commands.CreatePromoCode;
using SamaEcole.Application.Platform.Commands.DeactivatePromoCode;
using SamaEcole.Application.Platform.Queries.GetPromoCodes;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Module Tarification, Réductions &amp; Offres Promotionnelles — écran /admin/tarification. RÉSERVÉ
/// AU SUPER ADMIN, comme PlatformController/SchoolsController : `promo_codes` est une table
/// plateforme sans SchoolId, aucun autre rôle n'a de raison d'y accéder.
/// </summary>
[ApiController]
[Route("api/v1/admin/promo-codes")]
[Authorize(Roles = nameof(Role.SuperAdmin))]
public class PromoCodesController(ISender mediator) : ControllerBase
{
    public record CreatePromoCodeRequest(
        string Code,
        PromoDiscountType DiscountType,
        decimal DiscountValue,
        int? DurationMonths,
        int? MaxUses,
        DateTime StartDateUtc,
        DateTime EndDateUtc);

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PromoCodeDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetPromoCodesQuery(), cancellationToken));

    [HttpPost]
    [ProducesResponseType<CreatePromoCodeResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromBody] CreatePromoCodeRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new CreatePromoCodeCommand
            {
                Code = request.Code,
                DiscountType = request.DiscountType,
                DiscountValue = request.DiscountValue,
                DurationMonths = request.DurationMonths,
                MaxUses = request.MaxUses,
                StartDateUtc = request.StartDateUtc,
                EndDateUtc = request.EndDateUtc
            },
            cancellationToken);

        return CreatedAtAction(nameof(GetAll), result);
    }

    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeactivatePromoCodeCommand(id), cancellationToken);
        return NoContent();
    }
}
