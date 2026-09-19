using MediatR;

namespace SamaEcole.Application.ReportCards.Commands.SendReportCard;

public enum CommunicationChannel
{
    Email,
    WhatsApp,
    Both
}

public record SendReportCardCommand(Guid StudentId, Guid TermId, CommunicationChannel Channel)
    : IRequest<SendReportCardResult>;

/// <param name="WhatsAppSimulated">
/// Vrai quand le canal WhatsApp a été demandé mais qu'aucun fournisseur n'est configuré : le bulletin
/// a été JOURNALISÉ, pas transmis. L'API renvoie alors 200 avec un avertissement, jamais un faux
/// « envoyé avec succès ». Un ÉCHEC réel (rejet de Meta, jeton expiré…) ne passe pas par ici : il lève
/// <see cref="SamaEcole.Application.Common.Exceptions.WhatsAppDeliveryException"/>.
/// </param>
/// <param name="Message">Texte prêt à afficher : confirmation, ou avertissement de simulation.</param>
public record SendReportCardResult(bool WhatsAppSimulated, string Message);
