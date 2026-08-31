using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetDuesNotice;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class DuesNoticePdfGenerator(ILogger<DuesNoticePdfGenerator>? logger = null) : IDuesNoticePdfGenerator
{
    static DuesNoticePdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(DuesNoticeDto notice, byte[]? logo, byte[] qrCodeImage) =>
        PdfRenderGuard.Render(
            logger,
            $"sommation pour impayés {notice.NoticeNumber} (matricule {notice.Matricule}, inscription {notice.EnrollmentId})",
            () => new DuesNoticeDocument(notice, logo, qrCodeImage).GeneratePdf(),
            logo is not null ? () => new DuesNoticeDocument(notice, null, qrCodeImage).GeneratePdf() : null);
}
