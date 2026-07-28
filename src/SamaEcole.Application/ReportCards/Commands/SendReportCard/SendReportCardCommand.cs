using MediatR;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.ReportCards.Commands.SendReportCard;

public enum CommunicationChannel
{
    Email,
    WhatsApp,
    Both
}

public record SendReportCardCommand(Guid StudentId, Guid TermId, CommunicationChannel Channel) : IRequest;
