using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments.Events;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Enrollments.EventHandlers;

/// <summary>
/// Ticket JGK-E03 — notifie par e-mail tous les Directeurs actifs de l'école dès qu'une inscription
/// est enregistrée. Cible <see cref="Role.Directeur"/>, jamais <c>School.Email</c> (adresse de
/// contact imprimée sur le reçu, pas forcément surveillée).
///
/// Tourne dans le contexte de la requête authentifiée qui a déclenché l'inscription : le tenant EF
/// Core (RLS + Global Query Filter) est déjà positionné sur <see cref="StudentEnrolledEvent.SchoolId"/>,
/// donc <c>dbContext.Users</c> peut être interrogé directement — contrairement au chemin login, pas
/// besoin d'IAuthStore ici.
///
/// Invariant essentiel : un échec d'envoi (SMTP transitoire) ne doit JAMAIS faire échouer
/// l'inscription qui vient d'être enregistrée avec succès. <c>SmtpEmailSender.SendAsync</c> relance
/// l'exception SMTP — chaque envoi est donc entouré d'un try/catch qui logue et continue, sans jamais
/// relancer depuis ce handler.
/// </summary>
public class NotifyAdminOnStudentEnrolledEventHandler(
    IApplicationDbContext dbContext,
    IEmailSender emailSender,
    ILogger<NotifyAdminOnStudentEnrolledEventHandler> logger) : INotificationHandler<StudentEnrolledEvent>
{
    public async Task Handle(StudentEnrolledEvent notification, CancellationToken cancellationToken)
    {
        var directors = await dbContext.Users.AsNoTracking()
            .Where(u => u.SchoolId == notification.SchoolId
                        && u.Role == Role.Directeur
                        && u.Status == EntityStatus.Active)
            .ToListAsync(cancellationToken);

        if (directors.Count == 0)
        {
            logger.LogWarning(
                "Aucun Directeur actif trouvé pour l'école {SchoolId} — notification d'inscription de {StudentFullName} non envoyée.",
                notification.SchoolId, notification.StudentFullName);
            return;
        }

        var subject = $"Nouvelle inscription — {notification.StudentFullName}";
        var body =
            $"Bonjour,\n\n"
            + $"Une nouvelle inscription vient d'être enregistrée :\n\n"
            + $"Élève : {notification.StudentFullName}\n"
            + $"Matricule : {notification.Matricule}\n"
            + $"Date d'inscription : {notification.EnrolledAt:dd/MM/yyyy à HH:mm}\n\n"
            + $"Ceci est une notification automatique de Sama Ecole.";

        foreach (var director in directors)
        {
            try
            {
                await emailSender.SendAsync(new EmailMessage(director.Email, subject, body), cancellationToken);
            }
            catch (Exception ex)
            {
                // Ne relance JAMAIS : une inscription réussie ne doit jamais se traduire par une erreur
                // 500 côté utilisateur à cause d'un problème SMTP transitoire. On continue avec les
                // autres destinataires plutôt que d'abandonner au premier échec.
                logger.LogError(
                    ex,
                    "Échec d'envoi de la notification d'inscription à {To} (école {SchoolId}).",
                    director.Email, notification.SchoolId);
            }
        }
    }
}
