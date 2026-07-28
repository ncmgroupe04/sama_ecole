using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Attendance.Events;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Attendance.EventHandlers;

public class NotifyParentOnAttendanceEventHandler(
    IApplicationDbContext dbContext,
    IWhatsAppSender whatsAppSender,
    ISmsDispatcher smsDispatcher) : INotificationHandler<AttendanceRecordedEvent>
{
    public async Task Handle(AttendanceRecordedEvent notification, CancellationToken cancellationToken)
    {
        var student = await dbContext.Students.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == notification.StudentId && s.SchoolId == notification.SchoolId, cancellationToken);

        if (student == null) return;

        var subject = await dbContext.Subjects.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == notification.SubjectId && s.SchoolId == notification.SchoolId, cancellationToken);

        if (subject == null) return;

        string tutor = !string.IsNullOrWhiteSpace(student.GuardianName) ? student.GuardianName : "Cher Parent";
        string statusText = (notification.Status == AttendanceStatus.UnjustifiedAbsence || notification.Status == AttendanceStatus.JustifiedAbsence)
            ? "absent(e)"
            : $"en retard de {notification.LateMinutes} minute(s)";

        string messageBody = $"Bonjour {tutor},\nNous vous informons que {student.FullName} a été marqué(e) {statusText} au cours de {subject.Name} le {notification.Date:dd/MM/yyyy} ({notification.Period}).\nMerci de contacter la direction si besoin.";

        // WhatsApp
        if (!string.IsNullOrWhiteSpace(student.GuardianPhone))
        {
            var whatsAppMsg = new WhatsAppMessage(student.GuardianPhone, messageBody);
            await whatsAppSender.SendAsync(whatsAppMsg, cancellationToken);
        }

        // SMS (offre Premium) — canal distinct de WhatsApp et non un repli : au Sénégal, une partie
        // des tuteurs n'utilise pas WhatsApp, et l'école qui paie l'option veut joindre TOUT le monde.
        //
        // Aucune garde ici : formule, activation de l'alerte, solde et historique sont TOUS vérifiés
        // par SmsDispatcher (point de passage unique). Il ne lève jamais — un opérateur injoignable
        // ne doit pas faire échouer la saisie de l'appel qui a produit cet événement.
        var smsBody =
            $"{student.FullName} a été marqué(e) {statusText} le {notification.Date:dd/MM/yyyy} "
            + $"({subject.Name}). Contactez la direction si besoin.";

        await smsDispatcher.DispatchAsync(
            new SmsDispatchRequest(
                notification.SchoolId, student.GuardianPhone, smsBody,
                SmsTrigger.AttendanceAlert, student.Id),
            cancellationToken);
    }
}
