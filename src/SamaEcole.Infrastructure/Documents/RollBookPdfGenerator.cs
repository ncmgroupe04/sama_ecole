using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.RollBook.Pdf;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Génère le cahier de roulement. Si le rendu avec les images (logo, signature, cachet) échoue — image corrompue ou
/// format non pris en charge —, il est retenté SANS images plutôt que de refuser l'émission d'un document officiel
/// (<see cref="PdfRenderGuard"/>) ; il ne renvoie jamais un PDF vide.
/// </summary>
public class RollBookPdfGenerator(ILogger<RollBookPdfGenerator>? logger = null) : IRollBookPdfGenerator
{
    static RollBookPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(RollBookPdfModel model, RollBookPdfAssets assets) =>
        PdfRenderGuard.Render(
            logger,
            $"cahier de roulement {model.ClassroomName} ({model.Months.Count} mois, {model.StudentCount} élève(s))",
            () => new RollBookDocument(model, assets).GeneratePdf(),
            assets.HasImages ? () => new RollBookDocument(model, assets.WithoutImages()).GeneratePdf() : null);
}
