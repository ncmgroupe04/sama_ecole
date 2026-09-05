using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Registration.Commands.RejectRegistrationRequest;

/// <summary>
/// Ticket JGK-I03. Un simple UPDATE de la demande (table plateforme, hors RLS) : ni transaction, ni
/// fonction SECURITY DEFINER — rien n'est créé, aucune table sous RLS n'est touchée.
///
/// Pas d'entrée dans le journal d'audit tenant (JGK-H01) : ce journal exige un SchoolId, or un rejet
/// n'en produit aucun (aucune école n'est créée). C'est le même choix assumé que pour les tentatives
/// de connexion à e-mail inconnu — rien à quoi rattacher l'entrée dans cette table par école.
/// </summary>
public class RejectRegistrationRequestHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser,
    IEmailSender emailSender,
    TimeProvider timeProvider,
    ILogger<RejectRegistrationRequestHandler> logger)
    : IRequestHandler<RejectRegistrationRequestCommand, Unit>
{
    public async Task<Unit> Handle(RejectRegistrationRequestCommand request, CancellationToken cancellationToken)
    {
        var reviewerId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Aucun utilisateur associé à la session courante.");

        var registrationRequest = await dbContext.SchoolRegistrationRequests
            .SingleOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Demande d'inscription introuvable.");

        if (registrationRequest.Status != RegistrationRequestStatus.Pending)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.Id), "Cette demande a déjà été traitée.")
            ]);
        }

        registrationRequest.Status = RegistrationRequestStatus.Rejected;
        registrationRequest.RejectionReason = request.Reason.Trim();
        registrationRequest.ReviewedBy = reviewerId;
        registrationRequest.ReviewedAt = timeProvider.GetUtcNow();

        await dbContext.SaveChangesAsync(cancellationToken);

        // Le rejet est déjà écrit : un échec d'ENVOI (ex. déploiement sans SMTP configuré) ne doit pas
        // le remettre en cause — voir SubmitRegistrationRequestHandler pour le même raisonnement.
        try
        {
            await SendRejectionEmailAsync(
                registrationRequest.DirectorEmail, registrationRequest.DirectorFullName,
                registrationRequest.SchoolName, registrationRequest.TrackingReference,
                registrationRequest.RejectionReason, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Demande {RequestId} rejetée, mais l'e-mail de notification n'a pas pu être envoyé.", request.Id);
        }

        logger.LogInformation("Demande {RequestId} rejetée par {ReviewerId}.", request.Id, reviewerId);

        return Unit.Value;
    }

    private async Task SendRejectionEmailAsync(
        string email, string fullName, string schoolName, string trackingReference, string reason,
        CancellationToken cancellationToken)
    {
        var body =
            $"""
             Bonjour {fullName},

             Après examen, la demande d'inscription de l'établissement « {schoolName} » (référence
             {trackingReference}) n'a pas pu être validée pour le motif suivant :

             {reason}

             Vous pouvez soumettre une nouvelle demande en tenant compte de ces éléments.
             """;

        await emailSender.SendAsync(
            new EmailMessage(email, "Votre demande d'inscription Unikol", body),
            cancellationToken);
    }
}
