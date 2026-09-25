using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Institutional;
using SamaEcole.Application.Institutional.Commands;
using SamaEcole.Application.Institutional.Queries;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.Authorization;
using SamaEcole.Web.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Rapports institutionnels et normes du Ministère (Évolution N°7 — cartographie IEF). Contrôleur mince : aucune
/// logique métier ici (règle #8). L'école vient du JWT (règle #10), la RLS isole.
///
/// Rapport de rentrée IEF et normes d'âge : lecture Directeur + Secrétariat (qui prépare la rentrée), réglage des
/// normes Directeur seul. Contrôle d'âge à l'inscription : Directeur + Secrétariat.
/// </summary>
[ApiController]
[Route("api/v1/institutional")]
[Authorize(Roles = StaffRoles)]
[RequireModule(SchoolModule.Pedagogy)]
public class InstitutionalController(ISender mediator) : ControllerBase
{
    private const string StaffRoles = $"{nameof(Role.Directeur)},{nameof(Role.Secretariat)}";

    /// <summary>Rapport de rentrée IEF (JSON), pour l'écran.</summary>
    [HttpGet("ief-report")]
    [ProducesResponseType<IefReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> IefReport(
        [FromQuery] Guid schoolYearId, [FromQuery] DateOnly? ageReferenceDate, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetIefReportQuery(schoolYearId, ageReferenceDate), cancellationToken));

    /// <summary>Rapport de rentrée IEF en PDF (A4 paysage), ouvert dans l'aperçu partagé.</summary>
    [HttpGet("ief-report/pdf")]
    [Produces("application/pdf")]
    public async Task<IActionResult> IefReportPdf(
        [FromQuery] Guid schoolYearId, [FromQuery] DateOnly? ageReferenceDate,
        [FromServices] IIefReportPdfGenerator generator, CancellationToken cancellationToken)
    {
        var report = await mediator.Send(new GetIefReportQuery(schoolYearId, ageReferenceDate), cancellationToken);
        return this.InlinePdf(generator.Generate(report), FileName(report, "pdf"));
    }

    /// <summary>Rapport de rentrée IEF en classeur .xlsx.</summary>
    [HttpGet("ief-report/excel")]
    public async Task<IActionResult> IefReportExcel(
        [FromQuery] Guid schoolYearId, [FromQuery] DateOnly? ageReferenceDate,
        [FromServices] IIefReportExcelGenerator generator, CancellationToken cancellationToken)
    {
        var report = await mediator.Send(new GetIefReportQuery(schoolYearId, ageReferenceDate), cancellationToken);
        return File(generator.Generate(report),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", FileName(report, "xlsx"));
    }

    /// <summary>Tranches d'âge normales par niveau : modèle national et réglages de l'école.</summary>
    [HttpGet("age-norms")]
    [ProducesResponseType<IReadOnlyList<AgeNormRowDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AgeNorms(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetAgeNormsQuery(), cancellationToken));

    /// <summary>Règle la tranche d'âge d'un niveau (Directeur seul).</summary>
    [HttpPut("age-norms/{gradeLevel}")]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpsertAgeNorm(
        string gradeLevel, [FromBody] AgeNormRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new UpsertAgeNormCommand(gradeLevel, request.MinAge, request.MaxAge), cancellationToken);
        return NoContent();
    }

    /// <summary>« Revenir au modèle » national pour un niveau (Directeur seul).</summary>
    [HttpDelete("age-norms/{gradeLevel}")]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetAgeNorm(string gradeLevel, CancellationToken cancellationToken)
    {
        await mediator.Send(new ResetAgeNormCommand(gradeLevel), cancellationToken);
        return NoContent();
    }

    /// <summary>Contrôle de la tranche d'âge à l'inscription — un avertissement, jamais un blocage.</summary>
    [HttpGet("age-check")]
    [ProducesResponseType<AgeCheckDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AgeCheck(
        [FromQuery] Guid classroomId, [FromQuery] DateOnly birthDate, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new CheckEnrollmentAgeQuery(classroomId, birthDate), cancellationToken));

    public record AgeNormRequest(int MinAge, int MaxAge);

    private static string FileName(IefReportDto report, string extension) =>
        $"Rapport_rentree_IEF_{report.SchoolYearLabel.Replace('/', '-').Replace(' ', '_')}.{extension}";
}
