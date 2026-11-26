using SamaEcole.Application.Absences.Queries.GetAbsenceTicket;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>Rend le billet d'absence en PDF (A5 paysage) — pendant de <see cref="IExitTicketPdfGenerator"/>.</summary>
public interface IAbsenceTicketPdfGenerator
{
    byte[] Generate(AbsenceTicketDto ticket, byte[]? logo, byte[] qrCodeImage, byte[]? surveillantSignature);
}
