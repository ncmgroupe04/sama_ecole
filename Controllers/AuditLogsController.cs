using SamaEcole.Application.AuditLogs.Queries.GetAuditLogs;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Ticket JGK-H01 — journal d'audit centralisé (docs/Volume_7_Security.md §7). LECTURE SEULE : les
/// entrées sont écrites automatiquement par AuditLoggingBehavior, jamais par un contrôleur (aucune
/// route POST/PUT/DELETE ici — append-only imposé jusqu'au niveau des GRANT PostgreSQL, voir la migration).
///
/// Réservé au Directeur : c'est le journal de sécurité de TOUT l'établissement (connexions, paiements,
/// changements de statut d'AUTRES comptes…), pas une donnée que le Secrétariat ou la Finance ont
/// besoin de consulter dans le cadre de leur propre rôle.
/// </summary>
[ApiController]
[Route("api/v1/audit-logs")]
[Authorize(Roles = nameof(Role.Directeur))]
public class AuditLogsController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PaginatedAuditLogs>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List([FromQuery] GetAuditLogsQuery query, CancellationToken cancellationToken)
        => Ok(await mediator.Send(query, cancellationToken));
}
