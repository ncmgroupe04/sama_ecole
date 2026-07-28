using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Classrooms.Queries.GetSchoolCardsPdf;
using QuestPDF.Fluent;

namespace SamaEcole.Infrastructure.Documents;

public class SchoolCardPdfGenerator : ISchoolCardPdfGenerator
{
    public byte[] GeneratePdf(SchoolCardBatchDto batch)
    {
        var document = new SchoolCardDocument(batch);
        return document.GeneratePdf();
    }
}
