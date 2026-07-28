using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Registration.Commands.SubmitRegistrationRequest;

/// <summary>
/// Ticket JGK-I01. Écrit dans <c>school_registration_requests</c> — table PLATEFORME hors RLS : l'INSERT
/// passe donc par EF normalement, sans tenant (l'appelant est anonyme, il n'en a pas), exactement comme
/// l'insertion d'une <see cref="School"/> par le Super Admin.
///
/// Le mot de passe est haché AVANT toute écriture et n'est JAMAIS journalisé (docs/Volume_7_Security.md
/// §Paiements) : aucun log de ce Handler ne référence <c>request.DirectorPassword</c>. L'e-mail de
/// confirmation ne transmet que la référence de suivi — jamais d'écho du mot de passe choisi.
/// </summary>
public class SubmitRegistrationRequestHandler(
    IApplicationDbContext dbContext,
    IPasswordHasher passwordHasher,
    IRegistrationReferenceGenerator referenceGenerator,
    IEmailSender emailSender,
    ILogger<SubmitRegistrationRequestHandler> logger)
    : IRequestHandler<SubmitRegistrationRequestCommand, SubmitRegistrationRequestResult>
{
    /// <summary>
    /// Deux soumissions avec le même e-mail sont AUTORISÉES (« une école peut retenter », critère du
    /// ticket) : on ne contrôle donc pas l'unicité de l'e-mail, seulement celle de la référence de suivi.
    /// </summary>
    public async Task<SubmitRegistrationRequestResult> Handle(
        SubmitRegistrationRequestCommand request, CancellationToken cancellationToken)
    {
        var email = request.DirectorEmail.Trim();

        // Haché DÈS que possible, avant toute écriture : le mot de passe en clair ne survit pas à cette
        // ligne et n'atteint jamais ni la base ni un log (docs/Volume_7_Security.md §Paiements).
        var passwordHash = passwordHasher.Hash(request.DirectorPassword);

        var trackingReference = await GenerateUniqueReferenceAsync(cancellationToken);

        var registrationRequest = new SchoolRegistrationRequest
        {
            TrackingReference = trackingReference,
            DirectorFullName = request.DirectorFullName.Trim(),
            DirectorEmail = email,
            DirectorPhone = request.DirectorPhone.Trim(),
            DirectorPasswordHash = passwordHash,
            SchoolName = request.SchoolName.Trim(),
            SchoolAddress = string.IsNullOrWhiteSpace(request.SchoolAddress) ? null : request.SchoolAddress.Trim(),
            City = string.IsNullOrWhiteSpace(request.City) ? null : request.City.Trim(),
            Region = string.IsNullOrWhiteSpace(request.Region) ? null : request.Region.Trim(),
            EstimatedStudentCount = request.EstimatedStudentCount,
            RequestedPlan = request.RequestedPlan
        };

        dbContext.SchoolRegistrationRequests.Add(registrationRequest);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Après le commit : un e-mail parti ne se rembobine pas. L'envoyer avant risquerait d'annoncer une
        // référence de suivi qui n'existerait finalement pas si l'écriture échouait.
        await SendConfirmationAsync(email, registrationRequest.DirectorFullName, request.SchoolName.Trim(),
            trackingReference, cancellationToken);

        // Aucun mot de passe ici — seulement des identifiants non sensibles.
        logger.LogInformation(
            "Demande d'inscription {TrackingReference} enregistrée pour l'établissement « {SchoolName} ».",
            trackingReference, registrationRequest.SchoolName);

        return new SubmitRegistrationRequestResult(trackingReference);
    }

    /// <summary>
    /// Boucle de génération : l'index unique de la base est le garde-fou définitif, mais un pré-contrôle
    /// évite de compter sur un 409 pour un cas qui a une réponse déterministe. IgnoreQueryFilters : une
    /// référence déjà attribuée à une demande soft-deleted reste occupée côté index unique.
    /// </summary>
    private async Task<string> GenerateUniqueReferenceAsync(CancellationToken cancellationToken)
    {
        const int maxAttempts = 5;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var candidate = referenceGenerator.Generate();

            var alreadyUsed = await dbContext.SchoolRegistrationRequests
                .IgnoreQueryFilters()
                .AnyAsync(r => r.TrackingReference == candidate, cancellationToken);

            if (!alreadyUsed)
            {
                return candidate;
            }
        }

        // 8 caractères sur 31 symboles : atteindre 5 collisions d'affilée est statistiquement impossible
        // sans un volume de demandes qui relèverait d'un abus — mieux vaut échouer franchement.
        throw new InvalidOperationException(
            "Impossible de générer une référence de suivi unique après plusieurs tentatives.");
    }

    private async Task SendConfirmationAsync(
        string email, string fullName, string schoolName, string trackingReference, CancellationToken cancellationToken)
    {
        var body =
            $"""
             Bonjour {fullName},

             Votre demande d'inscription de l'établissement « {schoolName} » sur Sama Ecole a bien été reçue.

             Votre référence de suivi est : {trackingReference}

             Conservez-la : elle vous permet de suivre l'état de votre demande à tout moment, sans avoir à
             vous connecter. Notre équipe examine votre dossier et vous recontactera après validation.
             """;

        await emailSender.SendAsync(
            new EmailMessage(email, "Votre demande d'inscription Sama Ecole", body),
            cancellationToken);
    }
}
