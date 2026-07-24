using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using FluentValidation.Results;

namespace SamaEcole.Application.ReportCards.Commands.SendReportCard;

public class SendReportCardCommandHandler(
    IApplicationDbContext dbContext,
    IMediator mediator,
    IWhatsAppSender whatsAppSender,
    IEmailSender emailSender,
    ITenantProvider tenantProvider) : IRequestHandler<SendReportCardCommand>
{
    public async Task Handle(SendReportCardCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à la session.");

        var student = await dbContext.Students.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.StudentId && s.SchoolId == schoolId, cancellationToken);

        if (student == null)
        {
            throw new ValidationException([new ValidationFailure("StudentId", "L'élève n'existe pas ou n'appartient pas à l'établissement.")]);
        }

        if (string.IsNullOrWhiteSpace(student.GuardianPhone) && (request.Channel == CommunicationChannel.WhatsApp || request.Channel == CommunicationChannel.Both))
        {
            throw new ValidationException([new ValidationFailure("Channel", "Le numéro WhatsApp du tuteur n'est pas renseigné pour cet élève.")]);
        }

        // Generate the PDF
        var pdfResult = await mediator.Send(new GetReportCardPdfQuery(request.StudentId, request.TermId), cancellationToken);

        string tutor = !string.IsNullOrWhiteSpace(student.GuardianName) ? student.GuardianName : "Cher Parent";
        string messageBody = $"Bonjour {tutor},\nVeuillez trouver ci-joint le bulletin de notes de {student.FullName}.\nCordialement,\nLa Direction.";

        // WhatsApp
        if (request.Channel is CommunicationChannel.WhatsApp or CommunicationChannel.Both)
        {
            var whatsAppAttachment = new WhatsAppAttachment(pdfResult.FileName, pdfResult.Content, "application/pdf");
            var whatsAppMsg = new WhatsAppMessage(student.GuardianPhone!, messageBody, [whatsAppAttachment]);
            await whatsAppSender.SendAsync(whatsAppMsg, cancellationToken);
        }

        // Email (if email is supported in the future or GuardianPhone is used as fallback, wait, we don't have GuardianEmail right now.
        // We will mock the email send to "tutor@example.com" if Email is selected, just for demonstration until GuardianEmail is added to Student).
        if (request.Channel is CommunicationChannel.Email or CommunicationChannel.Both)
        {
            var emailAttachment = new EmailAttachment(pdfResult.FileName, pdfResult.Content, "application/pdf");
            // Placeholder: On utilise une adresse email fictive ou celle du tuteur si ajoutée plus tard.
            var emailMsg = new EmailMessage("tutor@example.com", "Bulletin de notes", messageBody, [emailAttachment]);
            await emailSender.SendAsync(emailMsg, cancellationToken);
        }
    }
}
