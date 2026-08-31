using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Inventory;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class DischargeNotePdfGenerator(ILogger<DischargeNotePdfGenerator>? logger = null) : IDischargeNotePdfGenerator
{
    static DischargeNotePdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(DischargeNoteModel model, byte[]? logo, byte[] qrCodeImage) =>
        PdfRenderGuard.Render(
            logger,
            $"décharge de matériel {model.Reference}",
            () => new DischargeNoteDocument(model, logo, qrCodeImage).GeneratePdf(),
            // Même repli que les autres pièces officielles : un logo illisible ne doit pas empêcher
            // l'émission du document, il doit seulement en disparaître.
            logo is not null ? () => new DischargeNoteDocument(model, null, qrCodeImage).GeneratePdf() : null);
}
