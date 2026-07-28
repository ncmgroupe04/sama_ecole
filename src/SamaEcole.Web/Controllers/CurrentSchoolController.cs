using System.IO;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Schools;
using SamaEcole.Application.Schools.Commands.UpdateCurrentSchool;
using SamaEcole.Application.Schools.Queries.GetCurrentSchool;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Identité de l'établissement COURANT (openapi.yaml /schools/current).
///
/// À NE PAS confondre avec <see cref="SchoolsController"/> (api/v1/schools), réservé au Super Admin
/// pour créer et lister les écoles : ici c'est le DIRECTEUR qui entretient SA fiche établissement —
/// nom, coordonnées, logo — reprise sur le reçu d'inscription. « current » vient du claim JWT, jamais
/// d'un paramètre de route (AGENTS.md règle #10).
/// </summary>
[ApiController]
[Route("api/v1/schools/current")]
[Authorize]
public class CurrentSchoolController(ISender mediator) : ControllerBase
{
    public record UpdateSchoolProfileRequest(
        string Name,
        string? Address,
        string? Phone,
        string? LogoUrl,
        string? InspectionAcademie,
        string? InspectionEducationFormation,
        string? NomLycee,

        // Coordonnées et mentions légales imprimées sur le reçu (NINEA / RCCM).
        string? Email,
        string? Ninea,
        string? RegistreCommerce);

    /// <summary>
    /// LECTURE ouverte à tout utilisateur de l'école : le nom et les coordonnées s'affichent sur le
    /// reçu et les écrans, quel que soit le rôle.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<SchoolProfileDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetCurrentSchoolQuery(), cancellationToken));

    /// <summary>ÉCRITURE réservée au Directeur (docs/Volume_7_Security.md §15 : configuration de l'établissement).</summary>
    [HttpPut]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType<SchoolProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        [FromBody] UpdateSchoolProfileRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new UpdateCurrentSchoolCommand(
                request.Name, request.Address, request.Phone, request.LogoUrl,
                request.InspectionAcademie, request.InspectionEducationFormation, request.NomLycee,
                request.Email, request.Ninea, request.RegistreCommerce),
            cancellationToken));

    /// <summary>Upload local d'un fichier image (logo) par le Directeur.</summary>
    [HttpPost("logo")]
    [HttpPost("/api/settings/upload-logo")]
    [HttpPost("/api/v1/settings/upload-logo")]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UploadLogo(
        IFormFile? file,
        [FromServices] ITenantProvider tenantProvider,
        [FromServices] IWebHostEnvironment env,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "Aucun fichier fourni.", code = "FILE_MISSING" });
        }

        if (file.Length > 2 * 1024 * 1024)
        {
            return BadRequest(new { message = "Le fichier dépasse la taille maximale autorisée (2 Mo).", code = "FILE_TOO_LARGE" });
        }

        var allowedContentTypes = new[] { "image/png", "image/jpeg", "image/webp" };
        if (!allowedContentTypes.Contains(file.ContentType.ToLowerInvariant()))
        {
            return BadRequest(new { message = "Format de fichier non supporté. Seuls PNG, JPEG et WEBP sont autorisés.", code = "INVALID_FILE_FORMAT" });
        }

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext) || !new[] { ".png", ".jpg", ".jpeg", ".webp" }.Contains(ext))
        {
            ext = file.ContentType.ToLowerInvariant() switch
            {
                "image/png" => ".png",
                "image/jpeg" => ".jpg",
                "image/webp" => ".webp",
                _ => ".png"
            };
        }

        var schoolId = tenantProvider.CurrentSchoolId?.ToString() ?? "unknown";
        var fileName = $"logo-{schoolId}-{Guid.NewGuid():N}{ext}";
        var uploadDir = Path.Combine(env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "uploads", "logos");

        if (!Directory.Exists(uploadDir))
        {
            Directory.CreateDirectory(uploadDir);
        }

        var filePath = Path.Combine(uploadDir, fileName);
        await using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        var relativeUrl = $"/uploads/logos/{fileName}";
        return Ok(new { url = relativeUrl });
    }
}
