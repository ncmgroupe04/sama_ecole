using SamaEcole.Application.Notifications.Commands.SendDuesReminderSms;
using SamaEcole.Application.Notifications.Queries.GetSmsHistory;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Notifications SMS aux parents — offre Premium.
///
/// <see cref="RequireFeatureAttribute"/> au niveau de la CLASSE : tout ce contrôleur est hors
/// d'atteinte d'une école dont la formule n'inclut pas les SMS, historique compris. Le refus prend
/// la forme normalisée FEATURE_NOT_IN_PLAN (voir FeatureAuthorizationResultHandler), que
/// wwwroot/js/features.js traduit en incitation à monter en gamme.
///
/// Les ENVOIS AUTOMATIQUES (assiduité) ne passent pas par ici : ils naissent d'un événement métier
/// et sont gardés par SmsDispatcher, qui revérifie la formule pour cette raison précise.
/// </summary>
[ApiController]
[Route("api/v1/sms")]
[Authorize(Roles = $"{nameof(Role.Directeur)},{nameof(Role.Finance)}")]
[RequireFeature(Feature.SmsNotifications)]
public class SmsController(ISender mediator) : ControllerBase
{
    /// <summary>Historique des envois et solde restant — écran « SMS » des paramètres.</summary>
    [HttpGet("history")]
    [ProducesResponseType<PaginatedSmsHistory>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetHistory(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(new GetSmsHistoryQuery { Page = page, PageSize = pageSize }, cancellationToken));

    public record SendDuesRemindersRequest(Guid? ClassroomId);

    /// <summary>
    /// Relance des impayés. Action explicite de l'utilisateur, jamais programmée : un envoi de masse
    /// aux familles doit rester une décision, pas un automatisme.
    /// </summary>
    [HttpPost("dues-reminders")]
    [ProducesResponseType<DuesReminderSmsResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SendDuesReminders(
        [FromBody] SendDuesRemindersRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new SendDuesReminderSmsCommand { ClassroomId = request.ClassroomId }, cancellationToken));
}
