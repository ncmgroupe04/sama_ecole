using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Platform.Commands.SendSubscriptionReminder;

/// <summary>
/// POST /admin/platform/subscriptions/{schoolId}/remind — bouton « Relancer » de l'écran Abonnements
/// &amp; Facturation. Réservé au Super Admin. N'écrit AUCUN paiement ni statut d'abonnement (AGENTS.md
/// règle #11 : seul un webhook signé confirme un paiement) — se contente d'adresser un rappel par
/// e-mail au Directeur de l'établissement, comme SubmitRegistrationRequest/ApproveRegistrationRequest
/// le font déjà pour d'autres notifications transactionnelles.
/// </summary>
public record SendSubscriptionReminderCommand(Guid SchoolId) : IRequest;

public class SendSubscriptionReminderCommandHandler(
    IApplicationDbContext dbContext,
    IAuthStore authStore,
    IAuditLogStore auditLogStore,
    ICurrentUserService currentUser,
    IEmailSender emailSender,
    TimeProvider timeProvider,
    ILogger<SendSubscriptionReminderCommandHandler> logger)
    : IRequestHandler<SendSubscriptionReminderCommand>
{
    public async Task Handle(SendSubscriptionReminderCommand request, CancellationToken cancellationToken)
    {
        var superAdminId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Aucun utilisateur authentifié.");

        // `schools` n'est PAS sous RLS (AGENTS.md règle #2, voir ApproveRegistrationRequestHandler) :
        // un Super Admin sans schoolId le lit normalement via EF.
        var school = await dbContext.Schools
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == request.SchoolId, cancellationToken)
            ?? throw new KeyNotFoundException("Établissement introuvable.");

        // `users` EST sous RLS : retrouver le Directeur cible passe par la même porte étroite
        // SECURITY DEFINER que l'impersonation (auth_find_active_director_by_school).
        var director = await authStore.FindActiveDirectorForSchoolAsync(request.SchoolId, cancellationToken)
            ?? throw new BusinessRuleException(
                "Cet établissement n'a aucun compte Directeur actif : impossible d'envoyer un rappel.");

        var now = timeProvider.GetUtcNow();

        await SendReminderEmailAsync(director.Email, director.FullName, school.Name, cancellationToken);

        // Attribuée à l'école CIBLE, pas au Super Admin (SchoolId nul) : c'est ce que le Directeur verra
        // dans son propre journal d'audit (Volume 7 §7), à qui incombe le paiement.
        await auditLogStore.AppendAsync(
            school.Id, superAdminId, "Subscriptions", "SendPaymentReminder",
            success: true, failureReason: null, currentUser.IpAddress, now, cancellationToken);

        logger.LogInformation(
            "Rappel de paiement envoyé au Directeur {DirectorId} de l'établissement {SchoolId}.",
            director.Id, school.Id);
    }

    private async Task SendReminderEmailAsync(
        string email, string fullName, string schoolName, CancellationToken cancellationToken)
    {
        var body =
            $"""
             Bonjour {fullName},

             Ceci est un rappel concernant l'abonnement Unikol de l'établissement « {schoolName} ».

             Merci de régulariser le paiement de votre abonnement dès que possible depuis votre espace,
             afin de conserver un accès complet à la plateforme.
             """;

        await emailSender.SendAsync(
            new EmailMessage(email, "Rappel — Abonnement Unikol", body),
            cancellationToken);
    }
}
