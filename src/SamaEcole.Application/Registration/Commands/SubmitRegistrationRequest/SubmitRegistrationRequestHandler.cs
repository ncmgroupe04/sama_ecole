using SamaEcole.Application.Common;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using FluentValidation.Results;
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
/// confirmation ne transmet que la référence de suivi ; le Super Admin est alerté séparément. La demande
/// reste <c>Pending</c> (aucun compte n'existe) jusqu'à sa décision.
/// </summary>
public class SubmitRegistrationRequestHandler(
    IApplicationDbContext dbContext,
    IPasswordHasher passwordHasher,
    IRegistrationReferenceGenerator referenceGenerator,
    ISchoolProvisioningStore provisioningStore,
    IEmailSender emailSender,
    RegistrationSettings registrationSettings,
    ILogger<SubmitRegistrationRequestHandler> logger)
    : IRequestHandler<SubmitRegistrationRequestCommand, SubmitRegistrationRequestResult>
{
    /// <summary>
    /// Deux demandes EN ATTENTE avec le même e-mail restent autorisées (« une école peut retenter »,
    /// critère JGK-I01) : on ne contrôle pas l'unicité entre demandes, seulement celle de la référence
    /// de suivi. En revanche, si l'e-mail identifie DÉJÀ un compte (docs/Volume_3_DDS.md §5.2), la
    /// demande n'aboutira jamais — l'approbation la refuse — autant le dire tout de suite à
    /// l'inscrivant plutôt que de laisser le Super Admin buter dessus plus tard.
    /// </summary>
    public async Task<SubmitRegistrationRequestResult> Handle(
        SubmitRegistrationRequestCommand request, CancellationToken cancellationToken)
    {
        // Forme canonique, identique à tous les chemins de création de compte (EmailNormalizer) :
        // stockée telle quelle, elle est aussi la valeur que l'approbation posera dans `users.Email`.
        var email = EmailNormalizer.Normalize(request.DirectorEmail);

        // « Retenter » vise une école SANS compte. Un e-mail déjà rattaché à un compte n'ouvre pas un
        // second établissement par une nouvelle inscription : le Super Admin RATTACHE l'école au
        // compte existant (AttachSchoolToUserCommand). Voir docs/Volume_3_DDS.md §5.2.
        if (await provisioningStore.EmailExistsAsync(email, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.DirectorEmail),
                    "Cette adresse e-mail est déjà associée à un compte Unikol. Connectez-vous, ou "
                    + "demandez au support le rattachement d'un nouvel établissement à votre compte.")
            ]);
        }

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

        // Deux e-mails, indépendants : la confirmation au DEMANDEUR (sa référence de suivi, à ne pas
        // perdre — décision produit du 24/09/2026, rétablie) et l'alerte au SUPER ADMIN. Aucun e-mail
        // n'est adressé au personnel d'un établissement existant.
        //
        // Après le commit : un e-mail parti ne se rembobine pas. L'envoyer avant risquerait d'annoncer une
        // référence de suivi qui n'existerait finalement pas si l'écriture échouait.
        //
        // Un échec D'ENVOI ne doit en revanche PAS faire échouer la demande : la ligne est déjà écrite,
        // la référence de suivi est valide et consultable via /suivi-demande sans compte. Sur un
        // déploiement sans SMTP configuré (Smtp:AllowUnconfigured=true), UnconfiguredEmailSender échoue
        // bruyamment PAR CONCEPTION (EmailSenderGuard) — bruyamment dans les journaux, pas dans la
        // réponse HTTP d'une opération déjà réussie.
        try
        {
            await SendConfirmationAsync(email, registrationRequest.DirectorFullName, request.SchoolName.Trim(),
                trackingReference, cancellationToken);
        }
        catch (Exception ex)
        {
            // Warning, pas Error : UnconfiguredEmailSender/SmtpEmailSender ont déjà journalisé l'échec
            // en Error côté envoi — ici, l'opération elle-même a réussi, seul un effet secondaire a raté.
            logger.LogWarning(ex,
                "Demande d'inscription {TrackingReference} enregistrée, mais l'e-mail de confirmation n'a pas pu être envoyé.",
                trackingReference);
        }

        // Alerte Super Admin, isolée dans son propre try/catch : un échec ici ne masque jamais le
        // résultat de la confirmation ci-dessus. Sans adresse configurée
        // (Registration__AdminNotificationEmail), on le dit franchement dans les journaux plutôt que de
        // laisser croire qu'une alerte est partie.
        if (string.IsNullOrWhiteSpace(registrationSettings.AdminNotificationEmail))
        {
            logger.LogWarning(
                "Demande d'inscription {TrackingReference} enregistrée, mais Registration:AdminNotificationEmail " +
                "n'est pas configuré : le Super Admin n'a reçu aucune alerte.",
                trackingReference);
        }
        else
        {
            try
            {
                await SendAdminAlertAsync(registrationRequest, cancellationToken);
            }
            catch (Exception ex)
            {
                // Warning, pas Error : SmtpEmailSender/UnconfiguredEmailSender ont déjà journalisé l'échec
                // en Error côté envoi — ici, l'opération elle-même a réussi, seul un effet secondaire a raté.
                logger.LogWarning(ex,
                    "Demande d'inscription {TrackingReference} enregistrée, mais l'alerte Super Admin n'a pas pu être envoyée.",
                    trackingReference);
            }
        }

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

    /// <summary>
    /// Ne transmet que la référence de suivi — jamais d'écho du mot de passe choisi. C'est le moyen
    /// de ne pas perdre cette référence, seul accès du demandeur à sa demande avant qu'un compte existe.
    /// </summary>
    private async Task SendConfirmationAsync(
        string email, string fullName, string schoolName, string trackingReference, CancellationToken cancellationToken)
    {
        var body =
            $"""
             Bonjour {fullName},

             Votre demande d'ouverture et d'activation de compte pour l'établissement « {schoolName} » sur
             Unikol a bien été reçue.

             Votre référence de suivi est : {trackingReference}

             Conservez-la : elle vous permet de suivre l'état de votre demande à tout moment, sans avoir à
             vous connecter. Notre équipe examine votre dossier ; vous recevrez un nouvel e-mail dès que
             votre compte sera validé.
             """;

        await emailSender.SendAsync(
            new EmailMessage(email, "Votre demande d'inscription Unikol", body),
            cancellationToken);
    }

    /// <summary>
    /// Ne transmet ni mot de passe ni hash — seulement les champs déjà visibles depuis
    /// <c>GET /admin/registration-requests</c>. Le lien mène au tableau de bord Super Admin
    /// (authentifié) : jamais un lien d'approbation à usage unique, qui ferait d'un simple GET depuis
    /// une messagerie une action d'écriture.
    /// </summary>
    private async Task SendAdminAlertAsync(
        SchoolRegistrationRequest registrationRequest, CancellationToken cancellationToken)
    {
        var reviewUrl = $"{registrationSettings.PublicBaseUrl.TrimEnd('/')}/admin/inscriptions";

        var body =
            $"""
             Nouvelle demande d'inscription reçue sur Unikol.

             Établissement : {registrationRequest.SchoolName}
             Ville / région : {registrationRequest.City ?? "—"} / {registrationRequest.Region ?? "—"}
             Effectif estimé : {registrationRequest.EstimatedStudentCount?.ToString() ?? "—"}
             Plan souhaité : {registrationRequest.RequestedPlan}

             Directeur : {registrationRequest.DirectorFullName}
             E-mail : {registrationRequest.DirectorEmail}
             Téléphone : {registrationRequest.DirectorPhone}

             Référence de suivi : {registrationRequest.TrackingReference}

             Approuver ou rejeter la demande : {reviewUrl}
             """;

        await emailSender.SendAsync(
            new EmailMessage(
                registrationSettings.AdminNotificationEmail!,
                $"Nouvelle demande d'inscription — {registrationRequest.SchoolName}",
                body),
            cancellationToken);
    }
}
