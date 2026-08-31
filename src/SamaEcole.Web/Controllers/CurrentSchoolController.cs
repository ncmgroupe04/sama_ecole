using System.IO;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Schools;
using SamaEcole.Application.Schools.Commands.GoLive;
using SamaEcole.Application.Schools.Commands.ResetSchoolData;
using SamaEcole.Application.Schools.Commands.UpdateCurrentSchool;
using SamaEcole.Application.Schools.Queries.GetCurrentSchool;
using SamaEcole.Application.Schools.Queries.GetSchoolMode;
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
    /// <summary>
    /// Garde de la « Zone de danger » : le mot-clé PURGER, ou le nom de l'établissement. Revérifiée
    /// côté serveur — la modale ne protège que les appelants qui passent par l'interface.
    /// </summary>
    public record ResetSchoolDataRequest(string Confirmation);

    /// <summary>Garde du passage en mode réel : le mot-clé CONFIRMER, ou le nom de l'établissement.</summary>
    public record GoLiveRequest(string Confirmation);

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
        string? RegistreCommerce,

        // Annuaire public (B2C). Défauts explicites : ce type est mappé À LA MAIN vers la commande
        // ci-dessous — tout champ ajouté ici DOIT être répercuté dans l'appel, sans quoi il serait
        // accepté par l'API puis silencieusement perdu (l'écueil déjà rencontré sur les réglages SMS).
        bool IsPubliclyListed = false,
        string? City = null,
        string? Region = null,
        string? PublicDescription = null,

        // Intégration étatique (SIMEN, JGK-M05). Même remarque : mappé à la main ci-dessous.
        string? NationalSchoolCode = null,
        string? MinistryAuthorizationNumber = null,
        string? SchoolDistrictCode = null,
        decimal? GpsLatitude = null,
        decimal? GpsLongitude = null);

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
                request.Email, request.Ninea, request.RegistreCommerce,
                request.IsPubliclyListed, request.City, request.Region, request.PublicDescription,
                request.NationalSchoolCode, request.MinistryAuthorizationNumber, request.SchoolDistrictCode,
                request.GpsLatitude, request.GpsLongitude),
            cancellationToken));

    /// <summary>
    /// « Zone de danger » de l'écran Paramètres — remet l'établissement COURANT à neuf : les élèves,
    /// inscriptions, notes, bulletins et transactions saisis pendant la phase d'essai sont effacés
    /// DÉFINITIVEMENT ; le compte du Directeur, les réglages de l'école et les années scolaires
    /// restent. Réservé au Directeur, et cantonné à SON école : le SchoolId vient du JWT, jamais du
    /// corps de la requête (AGENTS.md règle #10).
    ///
    /// POST plutôt que DELETE : la ressource visée n'est pas une entité identifiable mais une ACTION
    /// sur l'école, et le mot de confirmation voyage dans le corps.
    /// </summary>
    [HttpPost("reset-data")]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType<SchoolDataResetSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ResetData(
        [FromBody] ResetSchoolDataRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new ResetSchoolDataCommand(request.Confirmation), cancellationToken));

    /// <summary>
    /// État « bac à sable / mode réel » de l'établissement courant, plus le drapeau d'environnement
    /// qui pilote le bouton « Repasser en mode test ». Lecture ouverte à tout rôle de l'école : la
    /// pastille « Mode test » de la barre supérieure s'affiche pour tous.
    /// </summary>
    [HttpGet("mode")]
    [ProducesResponseType<SchoolModeDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMode(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetSchoolModeQuery(), cancellationToken));

    /// <summary>
    /// Fait passer l'établissement COURANT du mode test (bac à sable) au mode réel (exploitation).
    /// Action DÉLIBÉRÉE, réservée au Directeur, confirmée par saisie de « CONFIRMER » (ou du nom de
    /// l'école). Conséquence : POST reset-data devient indisponible (409 RESET_UNAVAILABLE_LIVE_MODE).
    /// Non rejouable : un second appel renvoie 409 ALREADY_LIVE. En vraie production, la bascule est
    /// définitive — voir la porte de recette /schools/current/dev/revert-to-test (Program.cs).
    /// </summary>
    [HttpPost("go-live")]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType<GoLiveResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> GoLive(
        [FromBody] GoLiveRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GoLiveCommand(request.Confirmation), cancellationToken));

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
