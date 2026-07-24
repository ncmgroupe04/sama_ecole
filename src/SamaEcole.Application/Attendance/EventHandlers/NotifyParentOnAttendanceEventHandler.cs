using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Attendance.Events;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Attendance.EventHandlers;

public class NotifyParentOnAttendanceEventHandler(
    IApplicationDbContext dbContext,
    IWhatsAppSender whatsAppSender,
    IEmailSender emailSender) : INotificationHandler<AttendanceRecordedEvent>
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

        // Email (if we add GuardianEmail later, we could use it. For now, we only have GuardianPhone.
        // We leave the email logic ready if an email is added to the student).
        // if (!string.IsNullOrWhiteSpace(student.GuardianEmail))
        // {
        //     var emailMsg = new EmailMessage(student.GuardianEmail, "Alerte d'assiduité", messageBody);
        //     await emailSender.SendAsync(emailMsg, cancellationToken);
        // }
    }
}
