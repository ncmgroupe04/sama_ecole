using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.VieScolaire.Queries.GetParentNotice;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class ParentNoticePdfGenerator : IParentNoticePdfGenerator
{
    static ParentNoticePdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(ParentNoticeDto notice, byte[]? logo, byte[] qrCodeImage)
    {
        try
        {
            return new ParentNoticeDocument(notice, logo, qrCodeImage).GeneratePdf();
        }
        catch (Exception) when (logo is not null)
        {
            return new ParentNoticeDocument(notice, null, qrCodeImage).GeneratePdf();
        }
    }
}
