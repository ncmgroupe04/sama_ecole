using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.VieScolaire.Queries.GetParentNotice;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class ParentNoticePdfGenerator(ILogger<ParentNoticePdfGenerator>? logger = null) : IParentNoticePdfGenerator
{
    static ParentNoticePdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(ParentNoticeDto notice, byte[]? logo, byte[] qrCodeImage) =>
        PdfRenderGuard.Render(
            logger,
            $"convocation de parent {notice.NoticeNumber} (matricule {notice.Matricule}, convocation {notice.ParentSummonsId})",
            () => new ParentNoticeDocument(notice, logo, qrCodeImage).GeneratePdf(),
            logo is not null ? () => new ParentNoticeDocument(notice, null, qrCodeImage).GeneratePdf() : null);
}
