using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Inventory;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class DischargeNotePdfGenerator : IDischargeNotePdfGenerator
{
    static DischargeNotePdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(DischargeNoteModel model, byte[]? logo, byte[] qrCodeImage)
    {
        try
        {
            return new DischargeNoteDocument(model, logo, qrCodeImage).GeneratePdf();
        }
        catch (Exception) when (logo is not null)
        {
            // Même repli que les autres pièces officielles : un logo illisible ou corrompu ne doit pas
            // empêcher l'émission du document, il doit seulement en disparaître.
            return new DischargeNoteDocument(model, null, qrCodeImage).GeneratePdf();
        }
    }
}
