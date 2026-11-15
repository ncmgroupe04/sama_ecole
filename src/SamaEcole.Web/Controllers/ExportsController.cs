using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SamaEcole.Application.Exports.Queries.ExportSchoolData;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.RateLimiting;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// « Exporter mes données » : archive ZIP de toutes les données de l'établissement courant (docs/Volume_4_API_Design.md §11).
///
/// DIRECTEUR SEUL : c'est toute la base d'une école, données personnelles d'élèves comprises. Pas de [RequireModule] : l'export
/// appartient à l'établissement, quelle que soit sa formule, ses modules activés ou son état d'abonnement (il doit rester possible de
/// récupérer ses données, y compris en lecture seule après expiration). POST et non GET : il exige la confirmation du mot de passe
/// dans le corps, jamais dans l'adresse (qui se retrouverait dans les journaux).
/// </summary>
[ApiController]
[Route("api/v1/exports")]
[Authorize(Roles = nameof(Role.Directeur))]
public class ExportsController(ISender mediator) : ControllerBase
{
    public record ExportSchoolDataRequest(string? Password);

    [HttpPost("school-data")]
    [EnableRateLimiting(SensitiveEndpointRateLimiting.SchoolDataExportPolicyName)]
    [Produces("application/zip")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ExportSchoolData([FromBody] ExportSchoolDataRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ExportSchoolDataQuery(request.Password ?? string.Empty), cancellationToken);

        // Le flux (fichier temporaire, DeleteOnClose) est fermé par ASP.NET Core après l'envoi.
        return File(result.Content, "application/zip", result.FileName);
    }
}
