using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Classrooms.Queries.GetSchoolCardsPdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class SchoolCardPdfGenerator(ILogger<SchoolCardPdfGenerator>? logger = null) : ISchoolCardPdfGenerator
{
    static SchoolCardPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] GeneratePdf(SchoolCardBatchDto batch) =>
        PdfRenderGuard.Render(
            logger,
            $"cartes scolaires (école {batch.SchoolName}, classe {batch.ClassroomName}, {batch.SchoolYearName})",
            () => new SchoolCardDocument(batch).GeneratePdf(),
            batch.SchoolLogo is not null
                ? () => new SchoolCardDocument(batch with { SchoolLogo = null }).GeneratePdf()
                : null);
}
