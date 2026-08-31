using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Discipline.Queries.GetDisciplinaryPv;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class DisciplinaryPvPdfGenerator(ILogger<DisciplinaryPvPdfGenerator>? logger = null) : IDisciplinaryPvPdfGenerator
{
    static DisciplinaryPvPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(DisciplinaryPvDto pv, byte[]? logo, byte[] qrCodeImage, byte[]? surveillantSignature) =>
        PdfRenderGuard.Render(
            logger,
            $"PV de discipline {pv.PvNumber} (matricule {pv.Matricule}, dossier {pv.DisciplineRecordId})",
            () => new DisciplinaryPvDocument(pv, logo, qrCodeImage, surveillantSignature).GeneratePdf(),
            logo is not null || surveillantSignature is not null
                ? () => new DisciplinaryPvDocument(pv, null, qrCodeImage, null).GeneratePdf()
                : null);
}
