using System.Globalization;
using SamaEcole.Application.Exams;
using SamaEcole.Infrastructure.Documents.Components;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Carte de convocation à un examen officiel (A4 portrait, charte <see cref="OfficialHeaderComponent"/>,
/// même structure que <see cref="ParentNoticeDocument"/>) : centre, numéro de table et date de
/// l'épreuve (Volume 1 §22.5).
/// </summary>
public class ExamConvocationDocument(ExamConvocationModel notice, byte[]? logo, byte[] qrCodeImage) : IDocument
{
    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Convocation {notice.Reference}",
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
                    c, notice.InspectionAcademie, notice.InspectionEducationFormation, "Établissement", notice.SchoolName, logo));

                column.Item().PaddingTop(24).AlignCenter()
                    .Text($"CONVOCATION — {notice.ExamType.ToUpperInvariant()}").Bold().FontSize(16).Underline();
                column.Item().PaddingTop(2).AlignCenter()
                    .Text($"N° {NoBreakText.NoBreak(notice.Reference)}").FontSize(9).FontColor(Colors.Grey.Darken2);

                column.Item().PaddingTop(24).Text(FaitA()).AlignRight().Italic().FontSize(10);

                column.Item().PaddingTop(20).Element(ComposeBody);
                column.Item().PaddingTop(20).Element(ComposeDetails);
                column.Item().PaddingTop(40).Element(ComposeSignature);
                column.Item().PaddingTop(24).Element(c => OfficialHeaderComponent.ComposeAuthenticityFooter(c, qrCodeImage, notice.Reference));
            });
        });
    }

    private void ComposeBody(IContainer container)
    {
        container.Text(text =>
        {
            text.DefaultTextStyle(style => style.LineHeight(1.6f));
            text.Justify();
            text.Span("Le candidat ");
            text.Span(notice.StudentFullName).Bold();
            text.Span($" (matricule {NoBreakText.NoBreak(notice.StudentMatricule)}, classe {notice.ClassroomName}) ");
            text.Span("est convoqué(e) à se présenter au centre d'examen désigné ci-dessous, muni(e) d'une pièce d'identité ");
            text.Span("et de la présente convocation.");
        });
    }

    private void ComposeDetails(IContainer container)
    {
        container.Border(0.75f).BorderColor(Colors.Grey.Darken1).Padding(10).Column(column =>
        {
            column.Item().Text("AFFECTATION").Bold().FontSize(9).FontColor(Colors.Grey.Darken3);

            column.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text(t =>
                {
                    t.Span("Centre d'examen : ").Bold().FontSize(10.5f);
                    t.Span(notice.ExamCenterName).FontSize(10.5f);
                });

                row.RelativeItem().Text(t =>
                {
                    t.Span("Numéro de table : ").Bold().FontSize(10.5f);
                    t.Span(notice.CandidateNumber).FontSize(10.5f);
                });
            });

            if (!string.IsNullOrWhiteSpace(notice.Series))
            {
                column.Item().PaddingTop(4).Text(t =>
                {
                    t.Span("Série : ").Bold().FontSize(10.5f);
                    t.Span(notice.Series).FontSize(10.5f);
                });
            }
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
                right.Item().PaddingTop(30).AlignCenter().Text("[Signature et Cachet]").FontSize(8).FontColor(Colors.Grey.Medium);
            });
        });
    }

    private string FaitA() =>
        string.IsNullOrWhiteSpace(notice.SchoolCity)
            ? $"Fait le {FormatDate(notice.IssuedAt)}"
            : $"Fait à {notice.SchoolCity}, le {FormatDate(notice.IssuedAt)}";

    private static string FormatDate(DateTimeOffset moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
