using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Discipline.Queries.GetDisciplinaryPv;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class DisciplinaryPvPdfGenerator : IDisciplinaryPvPdfGenerator
{
    static DisciplinaryPvPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(DisciplinaryPvDto pv, byte[]? logo, byte[] qrCodeImage, byte[]? surveillantSignature)
    {
        try
        {
            return new DisciplinaryPvDocument(pv, logo, qrCodeImage, surveillantSignature).GeneratePdf();
        }
        catch (Exception) when (logo is not null || surveillantSignature is not null)
        {
            return new DisciplinaryPvDocument(pv, null, qrCodeImage, null).GeneratePdf();
        }
    }
}
