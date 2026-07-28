using SamaEcole.Application.Absences.Queries.GetExitTicket;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>Rend le billet de sortie en PDF (A5 paysage) — pendant de <see cref="IEntryTicketPdfGenerator"/>.</summary>
public interface IExitTicketPdfGenerator
{
    byte[] Generate(ExitTicketDto ticket, byte[]? logo, byte[] qrCodeImage, byte[]? surveillantSignature);
}
