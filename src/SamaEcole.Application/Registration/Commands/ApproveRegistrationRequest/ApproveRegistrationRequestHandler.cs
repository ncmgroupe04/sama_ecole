using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Registration.Commands.ApproveRegistrationRequest;

/// <summary>
/// Ticket JGK-I03. ATOMICITÉ (critère du ticket) : établissement + Directeur + abonnement naissent dans
/// UNE seule transaction (ExecuteInTransactionAsync). Si l'un des trois échoue, la transaction est
/// annulée et AUCUN n'est créé — la demande reste Pending, réapprouvable.
///
/// Deux des trois écritures ne peuvent PAS passer par un INSERT EF classique : `users` et
/// `subscriptions` sont sous RLS, et le Super Admin n'a aucun schoolId à présenter au WITH CHECK
/// (« new row violates row-level security policy »). Elles passent donc par les fonctions SECURITY
/// DEFINER provision_school_director / provision_subscription, dans la MÊME transaction que l'INSERT EF
/// de l'école (SchoolProvisioningStore rattache la commande à la transaction courante). `schools`
/// n'étant pas sous RLS, elle s'insère normalement via EF.
///
/// Journal d'audit écrit ICI, à la main (comme CreateSchoolCommandHandler) : l'acteur (Super Admin) n'a
/// pas de schoolId propre, le mécanisme générique AuditLoggingBehavior ne saurait à qui imputer l'entrée
/// — on l'attribue à l'école NOUVELLEMENT créée, connue après le commit.
/// </summary>
public class ApproveRegistrationRequestHandler(
    IApplicationDbContext dbContext,
    ISchoolProvisioningStore provisioningStore,
    IAuditLogStore auditLogStore,
    ICurrentUserService currentUser,
    IEmailSender emailSender,
    TimeProvider timeProvider,
    ILogger<ApproveRegistrationRequestHandler> logger)
    : IRequestHandler<ApproveRegistrationRequestCommand, ApproveRegistrationRequestResult>
{
    public async Task<ApproveRegistrationRequestResult> Handle(
        ApproveRegistrationRequestCommand request, CancellationToken cancellationToken)
    {
        var reviewerId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Aucun utilisateur associé à la session courante.");

        // Validation en lecture seule AVANT d'ouvrir une transaction : inutile d'en payer le coût pour
        // une demande introuvable, déjà traitée, ou dont l'e-mail est désormais pris.
        var snapshot = await dbContext.SchoolRegistrationRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Demande d'inscription introuvable.");

        if (snapshot.Status != RegistrationRequestStatus.Pending)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.Id), "Cette demande a déjà été traitée.")
            ]);
        }

        // Un e-mail identifie un compte sur TOUTE la plateforme. Entre la soumission (I01) et
        // maintenant, il a pu être attribué : sans ce contrôle, l'INSERT du Directeur violerait la
        // contrainte d'unicité et remonterait, cette fois, en erreur brute non traduite (raw SQL).
        if (await provisioningStore.EmailExistsAsync(snapshot.DirectorEmail, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(snapshot.DirectorEmail), "Un compte utilise déjà l'e-mail de cette demande.")
            ]);
        }

        var (schoolId, directorId, subscriptionId) = await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            // Rechargé SOUS transaction, en suivi : garde contre une double approbation concurrente
            // (TOCTOU) et rend l'opération sûre au rejeu de la stratégie d'exécution.
            var reg = await dbContext.SchoolRegistrationRequests
                .SingleAsync(r => r.Id == request.Id, ct);

            if (reg.Status != RegistrationRequestStatus.Pending)
            {
                throw new ValidationException([
                    new ValidationFailure(nameof(request.Id), "Cette demande a déjà été traitée.")
                ]);
            }

            var school = new School
            {
                Name = reg.SchoolName,
                Address = reg.SchoolAddress,
                // Seul numéro de contact recueilli au formulaire : sert de téléphone initial de l'école,
                // que le Directeur pourra corriger dans les Paramètres (JGK-B02).
                Phone = reg.DirectorPhone,
                Status = EntityStatus.Active
            };

            // `schools` n'est PAS sous RLS : un Super Admin sans schoolId l'insère normalement via EF.
            dbContext.Schools.Add(school);
            await dbContext.SaveChangesAsync(ct);

            // `users` EST sous RLS : cet INSERT passe par la porte étroite SECURITY DEFINER.
            var newDirectorId = await provisioningStore.CreateInitialDirectorAsync(
                school.Id, reg.DirectorEmail, reg.DirectorPasswordHash, reg.DirectorFullName, Role.Directeur, ct)
                ?? throw new InvalidOperationException(
                    $"L'établissement {school.Id} possède déjà un utilisateur : il n'est pas à provisionner.");

            // `subscriptions` aussi : même mécanisme, abonnement en AwaitingPayment sans date d'expiration.
            var newSubscriptionId = await provisioningStore.CreateInitialSubscriptionAsync(
                school.Id, reg.RequestedPlan, SubscriptionStatus.AwaitingPayment, ct)
                ?? throw new InvalidOperationException(
                    $"L'établissement {school.Id} possède déjà un abonnement : il n'est pas à provisionner.");

            // La demande n'est PAS transformée : elle reste, seul son statut avance et elle pointe
            // désormais vers l'école née de l'approbation.
            reg.Status = RegistrationRequestStatus.Approved;
            reg.ReviewedBy = reviewerId;
            reg.ReviewedAt = timeProvider.GetUtcNow();
            reg.CreatedSchoolId = school.Id;
            await dbContext.SaveChangesAsync(ct);

            return (school.Id, newDirectorId, newSubscriptionId);
        }, cancellationToken);

        // Après le commit : un e-mail parti ne se rembobine pas. Aucun mot de passe dedans — le Directeur
        // se connecte avec celui qu'il a choisi à la soumission (I01).
        await SendApprovalEmailAsync(
            snapshot.DirectorEmail, snapshot.DirectorFullName, snapshot.SchoolName, cancellationToken);

        // L'école existe désormais : première occasion d'imputer l'entrée d'audit à un SchoolId réel.
        await auditLogStore.AppendAsync(
            schoolId, reviewerId, "RegistrationRequests", "ApproveRegistrationRequest",
            success: true, failureReason: null, currentUser.IpAddress, timeProvider.GetUtcNow(), cancellationToken);

        logger.LogInformation(
            "Demande {RequestId} approuvée : école {SchoolId}, Directeur {DirectorId}, abonnement {SubscriptionId} (AwaitingPayment).",
            request.Id, schoolId, directorId, subscriptionId);

        return new ApproveRegistrationRequestResult(
            schoolId, directorId, subscriptionId, SubscriptionStatus.AwaitingPayment);
    }

    private async Task SendApprovalEmailAsync(
        string email, string fullName, string schoolName, CancellationToken cancellationToken)
    {
        var body =
            $"""
             Bonjour {fullName},

             Bonne nouvelle : la demande d'inscription de l'établissement « {schoolName} » sur Sama Ecole
             a été validée.

             Vous pouvez dès à présent vous connecter avec l'adresse e-mail de votre demande et le mot de
             passe que vous avez choisi lors de votre inscription.

             Une dernière étape vous attend : l'activation de votre abonnement par un premier paiement,
             directement depuis votre espace.
             """;

        await emailSender.SendAsync(
            new EmailMessage(email, "Votre inscription Sama Ecole est validée", body),
            cancellationToken);
    }
}
