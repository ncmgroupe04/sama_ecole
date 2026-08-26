using SamaEcole.Application.Inventory;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Rend la fiche de décharge en PDF (GET /inventory/assignments/{id}/pdf). Pièce officielle remise au
/// bénéficiaire : logo et QR code d'authenticité, comme le certificat d'Exéat ou la convocation.
/// </summary>
public interface IDischargeNotePdfGenerator
{
    byte[] Generate(DischargeNoteModel model, byte[]? logo, byte[] qrCodeImage);
}
