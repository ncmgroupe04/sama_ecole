using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetDuesNotice;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class DuesNoticePdfGenerator : IDuesNoticePdfGenerator
{
    static DuesNoticePdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(DuesNoticeDto notice, byte[]? logo, byte[] qrCodeImage)
    {
        try
        {
            return new DuesNoticeDocument(notice, logo, qrCodeImage).GeneratePdf();
        }
        catch (Exception) when (logo is not null)
        {
            return new DuesNoticeDocument(notice, null, qrCodeImage).GeneratePdf();
        }
    }
}
