using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Classrooms.Queries.GetSchoolCardsPdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class SchoolCardPdfGenerator : ISchoolCardPdfGenerator
{
    static SchoolCardPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] GeneratePdf(SchoolCardBatchDto batch)
    {
        var document = new SchoolCardDocument(batch);
        return document.GeneratePdf();
    }
}
