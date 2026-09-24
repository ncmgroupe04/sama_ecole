using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;

namespace SamaEcole.Application.ReportCards.Commands.SendReportCard;

public class SendReportCardCommandHandler(
    IApplicationDbContext dbContext,
    IMediator mediator,
    IWhatsAppSender whatsAppSender,
    IEmailSender emailSender,
    ISmsDispatcher smsDispatcher,
    ITenantProvider tenantProvider,
    ILogger<SendReportCardCommandHandler> logger) : IRequestHandler<SendReportCardCommand, SendReportCardResult>
{
    public async Task<SendReportCardResult> Handle(SendReportCardCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à la session.");

        var student = await dbContext.Students.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.StudentId && s.SchoolId == schoolId, cancellationToken);

        if (student == null)
        {
            throw new ValidationException([new ValidationFailure("StudentId", "L'élève n'existe pas ou n'appartient pas à l'établissement.")]);
        }

        var wantsWhatsApp = request.Channel is CommunicationChannel.WhatsApp or CommunicationChannel.Both;
        var wantsEmail = request.Channel is CommunicationChannel.Email or CommunicationChannel.Both;

        if (string.IsNullOrWhiteSpace(student.GuardianPhone) && wantsWhatsApp)
        {
            throw new ValidationException([new ValidationFailure("Channel", "Le numéro WhatsApp du tuteur n'est pas renseigné pour cet élève.")]);
        }

        if (string.IsNullOrWhiteSpace(student.GuardianEmail) && wantsEmail)
        {
            throw new ValidationException([new ValidationFailure("Channel", "L'e-mail du tuteur n'est pas renseigné pour cet élève.")]);
        }

        // Libellé du trimestre pour le corps du modèle WhatsApp ({{2}}). Le filtre multi-tenant borne
        // déjà à l'école courante ; introuvable (bulletin d'archive) → repli neutre, jamais d'échec ici.
        var termLabel = await dbContext.Terms.AsNoTracking()
            .Where(t => t.Id == request.TermId)
            .Select(t => t.Label)
            .FirstOrDefaultAsync(cancellationToken) ?? "cette période";

        var pdfResult = await mediator.Send(new GetReportCardPdfQuery(request.StudentId, request.TermId), cancellationToken);

        string tutor = !string.IsNullOrWhiteSpace(student.GuardianName) ? student.GuardianName : "Cher Parent";
        string messageBody = $"Bonjour {tutor},\nVeuillez trouver ci-joint le bulletin de notes de {student.FullName}.\nCordialement,\nLa Direction.";

        var whatsAppSimulated = false;

        if (wantsWhatsApp)
        {
            var attachment = new WhatsAppAttachment(pdfResult.FileName, pdfResult.Content, "application/pdf");

            // Le message porte À LA FOIS le texte libre (repli, n'atteint que les tuteurs dans la
            // fenêtre de 24 h de Meta) ET le contenu métier d'un modèle. L'expéditeur choisit selon SA
            // configuration : la couche Application n'a pas à connaître le nom du modèle Meta.
            var whatsAppMessage = new WhatsAppMessage(
                student.GuardianPhone!,
                messageBody,
                [attachment],
                new WhatsAppTemplateContent([student.FullName, termLabel], attachment));

            var whatsAppResult = await whatsAppSender.SendAsync(whatsAppMessage, cancellationToken);

            if (whatsAppResult.IsFailed)
            {
                logger.LogError(
                    "Envoi manuel du bulletin par WhatsApp échoué (Élève: {StudentId}, code Meta: {MetaCode}, HTTP: {HttpStatus}) : {Reason}",
                    student.Id, whatsAppResult.MetaErrorCode, whatsAppResult.HttpStatusCode, whatsAppResult.FailureReason);

                throw new WhatsAppDeliveryException(
                    whatsAppResult.FailureReason ?? "L'envoi WhatsApp a échoué.",
                    whatsAppResult.MetaErrorCode,
                    whatsAppResult.HttpStatusCode);
            }

            whatsAppSimulated = whatsAppResult.IsSimulated;

            if (whatsAppSimulated)
            {
                logger.LogWarning(
                    "Envoi manuel du bulletin par WhatsApp en mode simulation (Élève: {StudentId}) — "
                    + "section 'WhatsApp' non configurée, rien n'a été transmis.", student.Id);
            }
        }

        if (wantsEmail)
        {
            var emailAttachment = new EmailAttachment(pdfResult.FileName, pdfResult.Content, "application/pdf");
            var emailMsg = new EmailMessage(student.GuardianEmail!, "Bulletin de notes", messageBody, [emailAttachment]);
            await emailSender.SendAsync(emailMsg, cancellationToken);
        }

        // SMS d'AVIS, en complément et non en remplacement : un SMS ne transporte pas de pièce
        // jointe, il annonce seulement que le bulletin est disponible. Envoyé quel que soit le canal
        // choisi — au Sénégal, une partie des tuteurs n'ouvre ni WhatsApp ni sa boîte mail, et le
        // SMS est le seul canal qui les atteint à coup sûr.
        //
        // Aucune garde ici : formule, activation, solde et historique sont tous vérifiés par
        // SmsDispatcher, qui ne lève jamais. L'échec d'un avis ne doit pas annuler l'envoi du
        // bulletin lui-même, déjà parti ci-dessus.
        await smsDispatcher.DispatchAsync(
            new SmsDispatchRequest(
                schoolId,
                student.GuardianPhone,
                $"Le bulletin de notes de {student.FullName} est disponible. "
                + "Rapprochez-vous de l'établissement pour le retirer.",
                SmsTrigger.ReportCard,
                student.Id),
            cancellationToken);

        return whatsAppSimulated
            ? new SendReportCardResult(
                WhatsAppSimulated: true,
                Message: "Service WhatsApp non configuré (Mode Simulation / Log activé) — le bulletin n'a pas été transmis par WhatsApp.")
            : new SendReportCardResult(WhatsAppSimulated: false, Message: "Bulletin envoyé avec succès.");
    }
}
