using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.StateIntegration;
using SamaEcole.Application.StateIntegration.Commands.AssignStudentIen;
using SamaEcole.Application.StateIntegration.Commands.GenerateStudentMutationCertificate;
using SamaEcole.Application.StateIntegration.Commands.RevokeStudentMutationCertificate;
using SamaEcole.Application.StateIntegration.Queries.GetMutationCertificates;
using SamaEcole.Application.StateIntegration.Queries.GetPlaneteExport;
using SamaEcole.Application.StateIntegration.Queries.GetSkillsBookletPdf;
using SamaEcole.Application.StateIntegration.Queries.GetStateducReport;
using SamaEcole.Application.StateIntegration.Queries.VerifyMutationCertificate;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Module Intégration étatique (SIMEN / Planète / STATEDUC) — /api/v1/state-integration.
/// Contrôleur mince : aucune logique métier ici (AGENTS.md règle #8). L'école n'est jamais un
/// paramètre de requête, elle vient du JWT (règle #10).
///
/// DROITS (docs/Volume_4_API_Design.md §23) : `Directeur` sur tout le module, `Secretariat` sur la
/// saisie d'IEN et les certificats de mutation. Le rapport STATEDUC et l'export Planète restent au
/// seul Directeur — ce sont des DÉCLARATIONS ENGAGEANT L'ÉTABLISSEMENT devant le ministère, signées
/// par lui, et l'export sort l'état civil de tous les élèves en un fichier.
///
/// Aucune route de ce contrôleur ne marque un lot « Transmis » : cet état ne peut venir que d'un
/// webhook signé du SIMEN, par symétrie avec les paiements d'abonnement (AGENTS.md règle #11). Voir
/// <see cref="ISimenBridgeService"/> — le relais n'existe pas encore.
/// </summary>
[ApiController]
[Route("api/v1/state-integration")]
[Authorize(Roles = DirectorOnly)]
public class StateIntegrationController(ISender mediator, ISimenBridgeService simenBridge) : ControllerBase
{
    private const string DirectorOnly = "Directeur";
    private const string DirectorAndSecretariat = "Directeur,Secretariat";

    // ------------------------------------------------------------------- Export « Planète Ready »

    /// <summary>
    /// GET /planete/export — matrice élèves au format d'échange du SIMEN (JSON ou CSV).
    ///
    /// Renvoie un FICHIER, pas du JSON d'API, même au format Json : c'est un livrable destiné à être
    /// déposé sur le portail du ministère ou envoyé par courriel, et un navigateur qui l'afficherait
    /// à l'écran obligerait l'utilisateur à faire un copier-coller pour le récupérer.
    /// </summary>
    [HttpGet("planete/export")]
    public async Task<IActionResult> GetPlaneteExport(
        [FromQuery] Guid schoolYearId,
        [FromQuery] StateExportFormat format = StateExportFormat.Csv,
        [FromQuery] Guid? classroomId = null,
        CancellationToken cancellationToken = default)
    {
        var file = await mediator.Send(
            new GetPlaneteExportQuery(schoolYearId, format, classroomId), cancellationToken);

        return File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary>
    /// GET /simen/status — indique si le relais API du ministère est configuré.
    ///
    /// Consommé par l'écran AVANT d'afficher l'action « Transmettre au SIMEN » : proposer un bouton
    /// dont on sait qu'il échouera n'est pas une fonctionnalité. Aucune donnée d'établissement ne
    /// transite par cette route — elle ne décrit que l'état de la plateforme.
    /// </summary>
    [HttpGet("simen/status")]
    public IActionResult GetSimenStatus() => Ok(new
    {
        isConfigured = simenBridge.IsConfigured,
        message = simenBridge.IsConfigured
            ? "Le relais SIMEN est configuré."
            : "Aucune API publique du SIMEN n'est ouverte à ce jour : utilisez l'export Planète "
              + "(.csv / .json) et transmettez le fichier par la voie habituelle."
    });

    // ------------------------------------------------------------------- Rapport annuel STATEDUC

    /// <summary>GET /stateduc — le rapport agrégé, en JSON, pour l'écran de consultation.</summary>
    [HttpGet("stateduc")]
    public async Task<ActionResult<StateducReportDto>> GetStateducReport(
        [FromQuery] Guid schoolYearId,
        [FromQuery] DateOnly? observationDate = null,
        CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new GetStateducReportQuery(schoolYearId, observationDate), cancellationToken));

    /// <summary>
    /// GET /stateduc/pdf — le formulaire officiel, A4 paysage, prêt à signer et déposer.
    ///
    /// Le rendu réutilise le MÊME Handler d'agrégation que la route JSON et que l'export Excel : une
    /// seule définition des chiffres. Un second calcul « pour le PDF » finirait par diverger, et
    /// l'école déposerait un formulaire que son propre écran contredit.
    /// </summary>
    [HttpGet("stateduc/pdf")]
    public async Task<IActionResult> GetStateducReportPdf(
        [FromQuery] Guid schoolYearId,
        [FromQuery] DateOnly? observationDate,
        [FromServices] IStateducReportPdfGenerator pdfGenerator,
        CancellationToken cancellationToken)
    {
        var report = await mediator.Send(
            new GetStateducReportQuery(schoolYearId, observationDate), cancellationToken);

        // `inline` : le formulaire s'ouvre dans la modale d'aperçu partagée (_PdfPreviewModal) — le
        // Directeur le relit avant d'imprimer ou de télécharger, jamais un téléchargement forcé.
        Response.Headers["Content-Disposition"] = $"inline; filename=\"{BuildStateducFileName(report, "pdf")}\"";
        return File(pdfGenerator.Generate(report), "application/pdf");
    }

    /// <summary>GET /stateduc/excel — le même rapport en classeur .xlsx, pour consolidation à l'IEF.</summary>
    [HttpGet("stateduc/excel")]
    public async Task<IActionResult> GetStateducReportExcel(
        [FromQuery] Guid schoolYearId,
        [FromQuery] DateOnly? observationDate,
        [FromServices] IStateducReportExcelGenerator excelGenerator,
        CancellationToken cancellationToken)
    {
        var report = await mediator.Send(
            new GetStateducReportQuery(schoolYearId, observationDate), cancellationToken);

        return File(
            excelGenerator.Generate(report),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            BuildStateducFileName(report, "xlsx"));
    }

    // ------------------------------------------------------------------------------------- IEN

    /// <summary>
    /// PUT /students/{studentId}/ien — enregistre l'IEN officiel reçu du ministère, ou génère un
    /// numéro provisoire de secours quand le corps de la requête ne porte aucun numéro.
    ///
    /// Ouvert au Secrétariat : c'est lui qui reçoit et ressaisit les listes d'IEN transmises par
    /// l'IEF. Le refus d'écraser un IEN officiel par un provisoire est porté par le Handler, pas par
    /// le rôle — un Directeur n'a pas plus le droit de perdre un numéro officiel qu'un secrétaire.
    /// </summary>
    [HttpPut("students/{studentId:guid}/ien")]
    [Authorize(Roles = DirectorAndSecretariat)]
    public async Task<ActionResult<AssignStudentIenResult>> AssignStudentIen(
        Guid studentId,
        [FromBody] AssignStudentIenRequest request,
        CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new AssignStudentIenCommand(studentId, request.IenNumber), cancellationToken));

    // --------------------------------------------------------------- Certificat de mutation

    /// <summary>
    /// POST /students/{studentId}/mutation-certificate — délivre le certificat et renvoie le PDF.
    ///
    /// POST et non GET, bien qu'elle renvoie un document : elle ÉCRIT (numéro officiel séquentiel,
    /// ligne en base, code de vérification). Deux appels produisent deux certificats distincts —
    /// la ranger en GET laisserait croire qu'on peut la rejouer sans conséquence.
    /// </summary>
    [HttpPost("students/{studentId:guid}/mutation-certificate")]
    [Authorize(Roles = DirectorAndSecretariat)]
    public async Task<IActionResult> GenerateMutationCertificate(
        Guid studentId,
        [FromBody] MutationCertificateRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new GenerateStudentMutationCertificateCommand(
                studentId,
                request.SchoolYearId,
                request.Reason,
                request.ReasonDetails,
                request.DestinationSchoolName,
                request.DestinationCity),
            cancellationToken);

        // Le numéro et la situation financière voyagent en EN-TÊTES : le corps est le PDF lui-même,
        // et l'écran a besoin des deux pour afficher sa confirmation sans redemander la fiche.
        Response.Headers["X-Certificate-Number"] = result.CertificateNumber;
        Response.Headers["X-Financially-Clear"] = result.WasFinanciallyClear ? "true" : "false";

        // `inline` : le certificat s'ouvre dans la modale d'aperçu (impression / téléchargement au
        // choix), au lieu d'un download forcé dès la délivrance.
        Response.Headers["Content-Disposition"] = $"inline; filename=\"{result.FileName}\"";
        return File(result.Content, "application/pdf");
    }

    /// <summary>
    /// GET /certificates — les certificats de mutation délivrés (réimpression, révocation). Les
    /// révoqués restent dans la liste, marqués. `studentId` restreint à un élève (fiche élève).
    /// </summary>
    [HttpGet("certificates")]
    [Authorize(Roles = DirectorAndSecretariat)]
    public async Task<ActionResult<PaginatedMutationCertificates>> GetMutationCertificates(
        [FromQuery] Guid? studentId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(
            new GetMutationCertificatesQuery { StudentId = studentId, Page = page, PageSize = pageSize },
            cancellationToken));

    /// <summary>
    /// POST /certificates/{id}/revoke — révoque un certificat. La révocation est le SEUL moyen de
    /// corriger une pièce délivrée : un certificat n'est jamais modifié (Volume 1 §23.5). Motif
    /// obligatoire. Effet immédiat sur le point de vérification publique.
    /// </summary>
    [HttpPost("certificates/{id:guid}/revoke")]
    [Authorize(Roles = DirectorAndSecretariat)]
    public async Task<IActionResult> RevokeMutationCertificate(
        Guid id,
        [FromBody] RevokeCertificateRequest request,
        CancellationToken cancellationToken)
    {
        await mediator.Send(
            new RevokeStudentMutationCertificateCommand(id, request.Reason), cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// GET /certificates/verify/{token} — vérification PUBLIQUE d'un certificat de mutation depuis son
    /// QR code. ANONYME : c'est l'école d'accueil, extérieure à la plateforme, qui scanne.
    ///
    /// La réponse ne porte AUCUNE donnée de l'élève — statut, numéro, date, établissement émetteur, de
    /// quoi rapprocher le papier présenté (Volume 1 §23.5). Un token inconnu répond « unknown » avec
    /// un HTTP 200, jamais un 404 : distinguer les deux permettrait de sonder les numéros voisins.
    ///
    /// Rate-limité par IP via la même politique que l'annuaire public (profil de menace identique :
    /// endpoint anonyme, lecture seule).
    /// </summary>
    [HttpGet("certificates/verify/{token}")]
    [AllowAnonymous]
    [EnableRateLimiting(SensitiveEndpointRateLimiting.PublicDirectoryPolicyName)]
    public async Task<ActionResult<MutationCertificateVerificationResult>> VerifyCertificate(
        string token, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new VerifyMutationCertificateQuery(token), cancellationToken));

    // ------------------------------------------------------------------ Livret de compétences

    /// <summary>
    /// GET /students/{studentId}/skills-booklet?schoolYearId= — le livret de compétences en PDF.
    ///
    /// Document APC pluri-trimestres remis avec le certificat lors d'une mutation. Renvoie 409 si le
    /// niveau de la classe n'a pas de grille de compétences configurée : le livret ne s'applique pas
    /// à des matières plates.
    /// </summary>
    [HttpGet("students/{studentId:guid}/skills-booklet")]
    [Authorize(Roles = DirectorAndSecretariat)]
    public async Task<IActionResult> GetSkillsBooklet(
        Guid studentId,
        [FromQuery] Guid schoolYearId,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new GetSkillsBookletPdfQuery(studentId, schoolYearId), cancellationToken);

        // `inline` : le livret s'ouvre dans la modale d'aperçu partagée avant impression/téléchargement.
        Response.Headers["Content-Disposition"] = $"inline; filename=\"{result.FileName}\"";
        return File(result.Content, "application/pdf");
    }

    private static string BuildStateducFileName(StateducReportDto report, string extension)
    {
        // L'année scolaire porte un « / » (« 2026/2027 ») qui n'est pas un caractère de nom de fichier.
        var year = report.SchoolYearLabel.Replace('/', '-').Replace(' ', '-');
        return $"STATEDUC-{report.NationalSchoolCode ?? "sans-code"}-{year}.{extension}";
    }

    /// <summary>
    /// Corps de la requête d'attribution d'IEN. <c>IenNumber</c> ABSENT ou null = demande de
    /// génération provisoire ; une chaîne vide est refusée par le validateur, pour que « champ laissé
    /// vide » ne déclenche jamais une génération que l'utilisateur n'a pas demandée.
    /// </summary>
    public record AssignStudentIenRequest(string? IenNumber);

    public record MutationCertificateRequest(
        Guid SchoolYearId,
        StudentMutationReason Reason,
        string? ReasonDetails,
        string? DestinationSchoolName,
        string? DestinationCity);

    public record RevokeCertificateRequest(string Reason);
}
