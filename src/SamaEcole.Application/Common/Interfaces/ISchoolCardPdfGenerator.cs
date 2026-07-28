using SamaEcole.Application.Classrooms.Queries.GetSchoolCardsPdf;

namespace SamaEcole.Application.Common.Interfaces;

public interface ISchoolCardPdfGenerator
{
    byte[] GeneratePdf(SchoolCardBatchDto batch);
}
