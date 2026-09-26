using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Registration.Commands.SubmitRegistrationRequest;
using SamaEcole.Application.Registration.Queries.GetRegistrationRequestStatus;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.RateLimiting;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Tickets JGK-I01/I02 — formulaire PUBLIC d'inscription self-service et suivi de son état
/// (docs/Volume_4_API_Design.md §2bis). Entièrement ANONYME : c'est le point d'entrée d'une école qui
/// n'existe pas encore dans le système, et le Directeur n'a encore ni compte ni session pour la suivre.
///
/// Contrôleur mince (AGENTS.md règle #8) : la logique (hachage du mot de passe, génération de la
/// référence de suivi, envoi de l'e-mail, projection minimale du statut) vit dans les Handlers. Les
/// deux routes sont protégées par limitation de débit par IP ([EnableRateLimiting]) ; POST cumule un
/// second garde-fou, le honeypot ci-dessous (docs/Volume_7_Security.md §Paiements).
/// </summary>
[ApiController]
[Route("api/v1/registration-requests")]
[AllowAnonymous]
public class RegistrationRequestsController(
    ISender mediator,
    IRegistrationReferenceGenerator referenceGenerator,
    ILogger<RegistrationRequestsController> logger) : ControllerBase
{
    /// <summary>
    /// <paramref name="Website"/> est le HONEYPOT : un champ invisible pour un humain (masqué en CSS,
    /// hors du flux de tabulation), qu'un bot de remplissage automatique renseigne pourtant. Rempli, il
    /// trahit un robot. Il ne fait PAS partie de la commande applicative — c'est une préoccupation web.
    /// </summary>
    public record SubmitRegistrationRequest(
        string DirectorFullName,
        string DirectorEmail,
        string DirectorPhone,
        string DirectorPassword,
        string SchoolName,
        string? SchoolAddress,
        string? City,
        string? Region,
        int? EstimatedStudentCount,
        SchoolOwnership? Ownership,
        SchoolCycleProfile? CycleProfile,
        SchoolSizeTier? SizeTier,
        string? Website);

    [HttpPost]
    [EnableRateLimiting(RegistrationRateLimiting.PolicyName)]
    [ProducesResponseType<SubmitRegistrationRequestResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Submit(
        [FromBody] SubmitRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        // Honeypot rempli -> soumission robotisée. On répond comme un succès (même statut, même forme de
        // réponse) SANS rien enregistrer : ne donner aucun signal exploitable pour contourner le piège.
        if (!string.IsNullOrWhiteSpace(request.Website))
        {
            logger.LogWarning(
                "Demande d'inscription ignorée (honeypot rempli) depuis {IpAddress}.",
                HttpContext.Connection.RemoteIpAddress);

            return Ok(new SubmitRegistrationRequestResult(referenceGenerator.Generate()));
        }

        var result = await mediator.Send(
            new SubmitRegistrationRequestCommand
            {
                DirectorFullName = request.DirectorFullName,
                DirectorEmail = request.DirectorEmail,
                DirectorPhone = request.DirectorPhone,
                DirectorPassword = request.DirectorPassword,
                SchoolName = request.SchoolName,
                SchoolAddress = request.SchoolAddress,
                City = request.City,
                Region = request.Region,
                EstimatedStudentCount = request.EstimatedStudentCount,
                Ownership = request.Ownership,
                CycleProfile = request.CycleProfile,
                SizeTier = request.SizeTier
            },
            cancellationToken);

        // 200 (et non 201) : identique en tout point à la branche honeypot, pour qu'un bot ne puisse
        // pas distinguer un succès réel d'un piège au seul code de statut. Aucune ressource GET publique
        // n'est exposée par identifiant de toute façon — le suivi se fait par référence (ticket JGK-I02).
        return Ok(result);
    }

    /// <summary>
    /// Ticket JGK-I02. La référence de suivi fait ici office de secret d'accès de fait (aucune autre
    /// authentification n'existe encore pour cette demande) : le Handler ne projette QUE le strict
    /// minimum utile au Directeur (statut, nom d'école, date, motif de rejet) — voir
    /// GetRegistrationRequestStatusResult pour la liste explicite de ce qui n'est jamais renvoyé.
    /// </summary>
    [HttpGet("{trackingReference}/status")]
    [EnableRateLimiting(RegistrationRateLimiting.StatusPolicyName)]
    [ProducesResponseType<GetRegistrationRequestStatusResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> GetStatus(string trackingReference, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetRegistrationRequestStatusQuery(trackingReference), cancellationToken));
}
