using System.IO;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Schools;
using SamaEcole.Application.Schools.Commands.SetMatriculeSequenceStart;
using SamaEcole.Application.Schools.Commands.UpdateGradingScale;
using SamaEcole.Application.Schools.Commands.UpdateSchoolSettings;
using SamaEcole.Application.Schools.Queries.GetMatriculeSequences;
using SamaEcole.Application.Schools.Queries.GetSchoolSettings;
using SamaEcole.Application.Common.Exceptions;
using FluentValidation.Results;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Ticket JGK-B02 — paramètres de l'établissement COURANT (openapi.yaml /schools/current/settings).
///
/// « current » vient du claim JWT, jamais d'un paramètre de route : aucun Directeur ne peut désigner
/// l'établissement d'un autre (AGENTS.md règle #10).
/// </summary>
[ApiController]
[Route("api/v1/schools/current/settings")]
[Authorize]
public class SchoolSettingsController(ISender mediator) : ControllerBase
{
    public record UpdateSettingsRequest(
        string GradingScale,
        string StudentMatriculeFormat,
        string TeacherMatriculeFormat,
        int AutoLogoutMinutes,
        string DateFormat,
        int TuitionMonthsPerYear,
        bool AllowSecretaryToManageGrading,
        bool AllowFinanceToModifyFees,
        bool AllowFinanceToDeleteFees,
        string? DirectorSignatureUrl = null,
        string? SecretarySignatureUrl = null,
        string? CashierSignatureUrl = null,
        string? OfficialStampUrl = null,
        string? SurveillantSignatureUrl = null,
        string TypeEtablissement = "Prive",
        bool SmsOnAttendanceAlert = false,
        bool SmsOnDuesReminder = false,
        bool SmsOnPaymentReceipt = false,
        int DebtorReminderThresholdDays = 7,
        bool IsPedagogyEnabled = true,
        bool IsFinanceEnabled = true,
        bool IsInternatEnabled = false,
        bool IsCoranModuleEnabled = false,
        int GradeEditWindowDays = 7);

    public record UpdateGradingScaleRequest(string GradingScale);

    /// <summary>Corps du PUT matricule-sequences : le type visé (« Student » / « Teacher ») et le prochain numéro.</summary>
    public record SetMatriculeSequenceStartRequest(string Kind, int NextValue);

    /// <summary>
    /// LECTURE ouverte à tout utilisateur de l'école : le format de date et le barème pilotent
    /// l'affichage de TOUS les écrans (Secrétariat, Finance, Enseignant). Les réserver au Directeur
    /// obligerait chaque autre rôle à afficher des dates au mauvais format.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<SchoolSettingsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetSchoolSettingsQuery(), cancellationToken));

    /// <summary>ÉCRITURE réservée au Directeur (critère du ticket JGK-B02).</summary>
    [HttpPut]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType<SchoolSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        [FromBody] UpdateSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new UpdateSchoolSettingsCommand(
                request.GradingScale,
                request.StudentMatriculeFormat,
                request.TeacherMatriculeFormat,
                request.AutoLogoutMinutes,
                request.DateFormat,
                request.TuitionMonthsPerYear,
                request.AllowSecretaryToManageGrading,
                request.AllowFinanceToModifyFees,
                request.AllowFinanceToDeleteFees,
                request.DirectorSignatureUrl,
                request.SecretarySignatureUrl,
                request.CashierSignatureUrl,
                request.OfficialStampUrl,
                request.SurveillantSignatureUrl,
                request.TypeEtablissement,
                request.SmsOnAttendanceAlert,
                request.SmsOnDuesReminder,
                request.SmsOnPaymentReceipt,
                request.DebtorReminderThresholdDays,
                request.IsPedagogyEnabled,
                request.IsFinanceEnabled,
                request.IsInternatEnabled,
                request.IsCoranModuleEnabled,
                request.GradeEditWindowDays),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// État des compteurs de matricules (élèves, enseignants) pour l'année scolaire en cours :
    /// dernier numéro attribué et prochain à venir. Réservé au Directeur, comme le réglage.
    /// </summary>
    [HttpGet("matricule-sequences")]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType<MatriculeSequencesDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMatriculeSequences(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetMatriculeSequencesQuery(), cancellationToken));

    /// <summary>
    /// Fixe le NUMÉRO DE DÉPART de la numérotation des matricules pour l'année scolaire en cours
    /// (Option 1). Réservé au Directeur. 409 si la valeur demandée est inférieure ou égale au dernier
    /// numéro déjà attribué — rétrograder le compteur réémettrait des numéros en circulation.
    /// </summary>
    [HttpPut("matricule-sequences")]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType<MatriculeSequenceInfo>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetMatriculeSequenceStart(
        [FromBody] SetMatriculeSequenceStartRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<MatriculeKind>(request.Kind, ignoreCase: true, out var kind))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.Kind), "Type de compteur inconnu : attendez « Student » ou « Teacher ».")
            ]);
        }

        var result = await mediator.Send(
            new SetMatriculeSequenceStartCommand(kind, request.NextValue), cancellationToken);

        return Ok(result);
    }

    /// <summary>Upload local de l'image de la signature du directeur par le Directeur.</summary>
    [HttpPost("director-signature")]
    [HttpPost("/api/settings/upload-director-signature")]
    [HttpPost("/api/v1/settings/upload-director-signature")]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> UploadDirectorSignature(
        IFormFile? file,
        [FromServices] ITenantProvider tenantProvider,
        [FromServices] IWebHostEnvironment env,
        CancellationToken cancellationToken)
        => UploadSettingImageAsync(file, "signatures", "director-sig", tenantProvider, env, cancellationToken);

    /// <summary>Upload local de l'image de la signature du secrétariat par le Directeur.</summary>
    [HttpPost("secretary-signature")]
    [HttpPost("/api/settings/upload-secretary-signature")]
    [HttpPost("/api/v1/settings/upload-secretary-signature")]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> UploadSecretarySignature(
        IFormFile? file,
        [FromServices] ITenantProvider tenantProvider,
        [FromServices] IWebHostEnvironment env,
        CancellationToken cancellationToken)
        => UploadSettingImageAsync(file, "signatures", "secretary-sig", tenantProvider, env, cancellationToken);

    /// <summary>Upload local de l'image de la signature du caissier/service financier par le Directeur.</summary>
    [HttpPost("cashier-signature")]
    [HttpPost("/api/settings/upload-cashier-signature")]
    [HttpPost("/api/v1/settings/upload-cashier-signature")]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> UploadCashierSignature(
        IFormFile? file,
        [FromServices] ITenantProvider tenantProvider,
        [FromServices] IWebHostEnvironment env,
        CancellationToken cancellationToken)
        => UploadSettingImageAsync(file, "signatures", "cashier-sig", tenantProvider, env, cancellationToken);

    /// <summary>Upload local de l'image de la signature du Surveillant Général par le Directeur.</summary>
    [HttpPost("surveillant-signature")]
    [HttpPost("/api/settings/upload-surveillant-signature")]
    [HttpPost("/api/v1/settings/upload-surveillant-signature")]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> UploadSurveillantSignature(
        IFormFile? file,
        [FromServices] ITenantProvider tenantProvider,
        [FromServices] IWebHostEnvironment env,
        CancellationToken cancellationToken)
        => UploadSettingImageAsync(file, "signatures", "surveillant-sig", tenantProvider, env, cancellationToken);

    /// <summary>Upload local de l'image du cachet officiel par le Directeur.</summary>
    [HttpPost("official-stamp")]
    [HttpPost("/api/settings/upload-official-stamp")]
    [HttpPost("/api/v1/settings/upload-official-stamp")]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IActionResult> UploadOfficialStamp(
        IFormFile? file,
        [FromServices] ITenantProvider tenantProvider,
        [FromServices] IWebHostEnvironment env,
        CancellationToken cancellationToken)
        => UploadSettingImageAsync(file, "stamps", "stamp", tenantProvider, env, cancellationToken);

    private static async Task<IActionResult> UploadSettingImageAsync(
        IFormFile? file,
        string subFolder,
        string prefix,
        ITenantProvider tenantProvider,
        IWebHostEnvironment env,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return new BadRequestObjectResult(new { message = "Aucun fichier fourni.", code = "FILE_MISSING" });
        }

        if (file.Length > 2 * 1024 * 1024)
        {
            return new BadRequestObjectResult(new { message = "Le fichier dépasse la taille maximale autorisée (2 Mo).", code = "FILE_TOO_LARGE" });
        }

        var allowedContentTypes = new[] { "image/png", "image/jpeg", "image/webp" };
        if (!allowedContentTypes.Contains(file.ContentType.ToLowerInvariant()))
        {
            return new BadRequestObjectResult(new { message = "Format de fichier non supporté. Seuls PNG, JPEG et WEBP sont autorisés.", code = "INVALID_FILE_FORMAT" });
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
        var fileName = $"{prefix}-{schoolId}-{Guid.NewGuid():N}{ext}";
        var uploadDir = Path.Combine(env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "uploads", subFolder);

        if (!Directory.Exists(uploadDir))
        {
            Directory.CreateDirectory(uploadDir);
        }

        var filePath = Path.Combine(uploadDir, fileName);
        await using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        var relativeUrl = $"/uploads/{subFolder}/{fileName}";
        return new OkObjectResult(new { url = relativeUrl });
    }

    /// <summary>
    /// Ticket JGK-G02 — le barème est délégable au Secrétariat (à la guise du Directeur de CHAQUE
    /// école, voir GradingPolicies.CanManageGradingScale), contrairement au reste des réglages
    /// (matricules, déconnexion automatique, mensualités) qui restent réservés au Directeur sur
    /// PUT / ci-dessus. D'où un endpoint dédié plutôt qu'un élargissement du PUT / entier, qui aurait
    /// ouvert ces autres réglages au Secrétariat aussi.
    /// </summary>
    [HttpPut("grading-scale")]
    [Authorize(Policy = GradingPolicies.CanManageGradingScale)]
    [ProducesResponseType<SchoolSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateGradingScale(
        [FromBody] UpdateGradingScaleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new UpdateGradingScaleCommand(request.GradingScale), cancellationToken);

        return Ok(result);
    }
}
