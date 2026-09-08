using SamaEcole.Application.Common;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Schools.Commands.CreateSchool;

/// <summary>
/// Ticket JGK-B01 — création d'un établissement AVEC son compte Directeur initial.
///
/// L'école et son Directeur naissent dans UNE transaction : une école sans Directeur serait une
/// coquille inaccessible (personne ne peut s'y connecter, et aucun endpoint ne permet encore de
/// créer un compte), et un Directeur sans école violerait la clé étrangère.
///
/// Le mot de passe est GÉNÉRÉ ici, jamais fourni par l'appelant, et ne sort que par l'e-mail du
/// Directeur — pas par la réponse HTTP (voir CreateSchoolResult).
///
/// Journal d'audit (JGK-H01) : écrit ICI, à la main, plutôt que via AuditLoggingBehavior. L'ACTEUR
/// (Super Admin) n'a lui-même aucune école — le mécanisme générique, qui lit
/// tenantProvider.CurrentSchoolId, ne trouverait donc rien à qui imputer l'entrée. IAuditLogStore
/// contourne la RLS avec l'école NOUVELLEMENT CRÉÉE, désormais connue après la transaction — même
/// raisonnement que pour la connexion (LoginCommandHandler). Un échec (ex. e-mail déjà utilisé) ne
/// crée aucune école : rien à journaliser dans cette table tenant pour cette branche.
/// </summary>
public class CreateSchoolCommandHandler(
    IApplicationDbContext dbContext,
    ISchoolProvisioningStore provisioningStore,
    IAuditLogStore auditLogStore,
    ICurrentUserService currentUser,
    IPasswordGenerator passwordGenerator,
    IPasswordHasher passwordHasher,
    IEmailSender emailSender,
    TimeProvider timeProvider,
    ILogger<CreateSchoolCommandHandler> logger)
    : IRequestHandler<CreateSchoolCommand, CreateSchoolResult>
{
    public async Task<CreateSchoolResult> Handle(CreateSchoolCommand request, CancellationToken cancellationToken)
    {
        // Forme canonique (minuscules, sans espaces de bord) : la MÊME que tous les autres chemins de
        // création de compte, sans quoi une simple différence de casse crée un second compte que
        // l'index citext de `users.Email` ne rattrape plus (docs/Volume_3_DDS.md §5.2, EmailNormalizer).
        var email = EmailNormalizer.Normalize(request.DirectorEmail);

        // Un e-mail identifie un compte sur toute la plateforme : sans ce contrôle, l'insertion
        // échouerait sur la contrainte d'unicité et remonterait en 409 illisible.
        if (await provisioningStore.EmailExistsAsync(email, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.DirectorEmail), "Un compte utilise déjà cet e-mail.")
            ]);
        }

        var password = passwordGenerator.Generate();
        var passwordHash = passwordHasher.Hash(password);
        var fullName = string.IsNullOrWhiteSpace(request.DirectorFullName)
            ? $"Directeur — {request.Name}"
            : request.DirectorFullName.Trim();

        var (schoolId, directorId) = await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            var school = new School
            {
                Name = request.Name.Trim(),
                Address = request.Address.Trim(),
                Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
                Status = EntityStatus.Active
            };

            // `schools` n'est PAS une table tenant (elle définit le tenant) : aucune policy RLS ne s'y
            // applique, un Super Admin sans schoolId peut donc l'insérer normalement via EF.
            dbContext.Schools.Add(school);
            await dbContext.SaveChangesAsync(ct);

            // `users`, en revanche, est sous RLS : cet INSERT-là doit passer par la porte étroite.
            var newDirectorId = await provisioningStore.CreateInitialDirectorAsync(
                school.Id, email, passwordHash, fullName, Role.Directeur, ct)
                ?? throw new InvalidOperationException(
                    $"L'établissement {school.Id} possède déjà un utilisateur : il n'est pas à provisionner.");

            // `subscriptions` aussi est sous RLS : même porte étroite que ApproveRegistrationRequestHandler.
            // Comble un trou de ce chemin de création directe (Super Admin, module Tarification &
            // Promotions) : sans cet appel, aucun abonnement n'existait pour une école créée ici — la
            // seule voie qui en amorçait un jusqu'ici était l'approbation d'une demande self-service.
            // AwaitingPayment, comme le parcours self-service : aucune date d'expiration tant que le
            // premier paiement n'est pas confirmé (JGK-I06) — une offre gratuite se fait ensuite via
            // « Offrir un accès » (GrantComplimentaryAccessCommand), pas ici.
            _ = await provisioningStore.CreateInitialSubscriptionAsync(
                school.Id, request.Plan, SubscriptionStatus.AwaitingPayment, ct)
                ?? throw new InvalidOperationException(
                    $"L'établissement {school.Id} possède déjà un abonnement : il n'est pas à provisionner.");

            return (school.Id, newDirectorId);
        }, cancellationToken);

        // Après le commit : un e-mail parti ne se rembobine pas. L'envoyer dans la transaction
        // risquerait d'annoncer au Directeur des identifiants qui n'existent finalement pas.
        //
        // Un échec D'ENVOI ne doit PAS faire échouer la création (école + Directeur + abonnement sont
        // déjà écrits) — mais ici, contrairement aux autres Handlers de ce fichier, c'est CRITIQUE :
        // le mot de passe généré ne transite QUE par cet e-mail (voir CreateSchoolResult.EmailSent).
        var emailSent = true;
        try
        {
            await SendCredentialsAsync(email, fullName, request.Name, password, cancellationToken);
        }
        catch (Exception ex)
        {
            emailSent = false;
            logger.LogWarning(ex,
                "Établissement {SchoolId} créé, mais l'e-mail d'identifiants n'a pas pu être envoyé à {Email} " +
                "— le Directeur n'a AUCUN moyen de connaître son mot de passe tant qu'il n'est pas réinitialisé.",
                schoolId, email);
        }

        // L'école n'existe qu'à partir d'ici : c'est la première occasion d'attribuer l'entrée
        // d'audit à un SchoolId réel (voir la remarque de classe — l'acteur, Super Admin, n'en a pas).
        await auditLogStore.AppendAsync(
            schoolId, currentUser.UserId!.Value, "Schools", "CreateSchool",
            success: true, failureReason: null, currentUser.IpAddress, timeProvider.GetUtcNow(), cancellationToken);

        logger.LogInformation(
            "Établissement {SchoolId} créé avec son Directeur {DirectorId}.", schoolId, directorId);

        return new CreateSchoolResult(schoolId, request.Name.Trim(), directorId, email, emailSent);
    }

    private async Task SendCredentialsAsync(
        string email, string fullName, string schoolName, string password, CancellationToken cancellationToken)
    {
        var body =
            $"""
             Bonjour {fullName},

             Votre établissement « {schoolName} » vient d'être créé sur Unikol.

             Voici vos identifiants de connexion :
               E-mail       : {email}
               Mot de passe : {password}

             Ce mot de passe est provisoire : changez-le dès votre première connexion.
             """;

        await emailSender.SendAsync(
            new EmailMessage(email, $"Vos identifiants Unikol — {schoolName}", body),
            cancellationToken);
    }
}
