using System.Globalization;
using SamaEcole.Application.VieScolaire.Queries.GetParentNotice;
using SamaEcole.Infrastructure.Documents.Components;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Convocation d'un parent/tuteur (A4 portrait), même charte que les autres documents officiels
/// destinés à la famille (en-tête M.E.N., <see cref="OfficialHeaderComponent"/>).
/// </summary>
public class ParentNoticeDocument(ParentNoticeDto notice, byte[]? logo, byte[] qrCodeImage) : IDocument
{
    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Convocation {notice.NoticeNumber}",
        Author = notice.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimetre);
            page.DefaultTextStyle(text => text.FontFamily("Times New Roman").FontSize(11).FontColor(Colors.Black));

            page.Content().Column(column =>
            {
                column.Item().Element(OfficialHeaderComponent.ComposeMinistryBanner);
                column.Item().PaddingTop(10).Element(c => OfficialHeaderComponent.ComposeEstablishmentBlock(
                    c, notice.InspectionAcademie, notice.InspectionEducationFormation, notice.HeadingPrefix, notice.HeadingName, logo));

                column.Item().PaddingTop(24).AlignCenter()
                    .Text("CONVOCATION").Bold().FontSize(16).Underline();
                column.Item().PaddingTop(2).AlignCenter()
                    .Text($"N° {notice.NoticeNumber}").FontSize(9).FontColor(Colors.Grey.Darken2);

                column.Item().PaddingTop(24).Text(FaitA()).AlignRight().Italic().FontSize(10);

                column.Item().PaddingTop(16).Element(ComposeRecipientBlock);
                column.Item().PaddingTop(16).Element(ComposeBody);
                column.Item().PaddingTop(40).Element(ComposeSignature);
                column.Item().PaddingTop(24).Element(c => OfficialHeaderComponent.ComposeAuthenticityFooter(c, qrCodeImage, notice.NoticeNumber));
            });
        });
    }

    private void ComposeRecipientBlock(IContainer container)
    {
        container.Text(text =>
        {
            text.DefaultTextStyle(style => style.FontSize(11));
            text.Span("À l'attention de : ").SemiBold();
            text.Span(string.IsNullOrWhiteSpace(notice.GuardianName) ? "Parent / Tuteur" : notice.GuardianName);
            if (!string.IsNullOrWhiteSpace(notice.GuardianPhone))
            {
                text.Span($" ({notice.GuardianPhone})");
            }
        });
    }

    private void ComposeBody(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Text(text =>
            {
                text.DefaultTextStyle(style => style.LineHeight(1.6f));
                text.Justify();
                text.Span("Nous vous prions de bien vouloir vous présenter à l'établissement le ");
                text.Span(FormatDateTime(notice.ScheduledAt)).Bold();
                text.Span(" pour un entretien concernant votre enfant/pupille ");
                text.Span(notice.StudentFullName).Bold();
                text.Span($" (matricule {notice.Matricule}, classe {notice.ClassroomName}");
                if (!string.IsNullOrWhiteSpace(notice.SchoolYearLabel))
                {
                    text.Span($", année scolaire {notice.SchoolYearLabel}");
                }
                text.Span(").");
            });

            column.Item().PaddingTop(14).Border(0.75f).BorderColor(Colors.Grey.Darken1).Padding(8).Column(motive =>
            {
                motive.Item().Text("MOTIF DE LA CONVOCATION").Bold().FontSize(9).FontColor(Colors.Grey.Darken3);
                motive.Item().PaddingTop(4).Text(notice.Reason).FontSize(10);
            });

            column.Item().PaddingTop(14).Text(
                "Votre présence est vivement souhaitée dans l'intérêt de la scolarité de votre enfant.");
        });
    }

    private void ComposeSignature(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem();
            row.RelativeItem().Column(right =>
            {
                right.Item().AlignCenter().Text("Le Chef d'Établissement").FontSize(10);
                right.Item().PaddingTop(30).AlignCenter()
                    .Text("[Signature et Cachet]").FontSize(8).FontColor(Colors.Grey.Medium);
            });
        });
    }

    private string FaitA() =>
        string.IsNullOrWhiteSpace(notice.SchoolCity)
            ? $"Fait le {FormatDate(notice.IssuedAt)}"
            : $"Fait à {notice.SchoolCity}, le {FormatDate(notice.IssuedAt)}";

    private static string FormatDate(DateTimeOffset moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string FormatDateTime(DateTimeOffset moment) =>
        moment.ToString("dd/MM/yyyy 'à' HH'h'mm", CultureInfo.InvariantCulture);
}
