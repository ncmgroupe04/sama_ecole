using System.Globalization;
using SamaEcole.Application.Exams;
using SamaEcole.Infrastructure.Documents.Components;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Carte de convocation à un examen officiel — <b>A5 paysage</b>, charte <see cref="OfficialHeaderComponent"/> :
/// centre, numéro de table et date de l'épreuve (Volume 1 §22.5). C'est une pièce remise en main
/// propre au candidat : même format que les autres pièces à remettre (<see cref="EntryTicketDocument"/>,
/// <see cref="DischargeNoteDocument"/>), une pleine A4 pour ce contenu serait du gâchis de papier.
/// Paddings et corps sont calibrés pour tenir sur UNE page — voir le test « une seule page »
/// (ExamConvocationPdfGeneratorTests) avant de les regonfler.
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
            // A5 paysage, marge 12 mm (~186 × 124 mm utiles). Corps à 9 pt et paddings resserrés pour
            // que toute la convocation tienne sur UNE page.
            page.Size(PageSizes.A5.Landscape());
            page.Margin(12, Unit.Millimetre);
            page.DefaultTextStyle(text => text.FontFamily("Times New Roman").FontSize(9).FontColor(Colors.Black));

            page.Content().Column(column =>
            {
                column.Item().Element(OfficialHeaderComponent.ComposeMinistryBanner);
                column.Item().PaddingTop(6).Element(c => OfficialHeaderComponent.ComposeEstablishmentBlock(
                    c, notice.InspectionAcademie, notice.InspectionEducationFormation, "Établissement", notice.SchoolName, logo));

                column.Item().PaddingTop(10).AlignCenter()
                    .Text($"CONVOCATION — {notice.ExamType.ToUpperInvariant()}").Bold().FontSize(13).Underline();
                column.Item().PaddingTop(1).AlignCenter()
                    .Text($"N° {NoBreakText.NoBreak(notice.Reference)}").FontSize(8).FontColor(Colors.Grey.Darken2);

                column.Item().PaddingTop(8).Text(FaitA()).AlignRight().Italic().FontSize(9);

                column.Item().PaddingTop(8).Element(ComposeBody);
                column.Item().PaddingTop(8).Element(ComposeDetails);
                column.Item().PaddingTop(12).Element(ComposeSignatureAndFooter);
            });
        });
    }

    private void ComposeBody(IContainer container)
    {
        container.Text(text =>
        {
            text.DefaultTextStyle(style => style.LineHeight(1.35f));
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
        container.Border(0.75f).BorderColor(Colors.Grey.Darken1).Padding(8).Column(column =>
        {
            column.Item().Text("AFFECTATION").Bold().FontSize(8).FontColor(Colors.Grey.Darken3);

            column.Item().PaddingTop(5).Row(row =>
            {
                row.RelativeItem().Text(t =>
                {
                    t.Span("Centre d'examen : ").Bold().FontSize(10);
                    t.Span(notice.ExamCenterName).FontSize(10);
                });

                row.RelativeItem().Text(t =>
                {
                    t.Span("Numéro de table : ").Bold().FontSize(10);
                    t.Span(notice.CandidateNumber).FontSize(10);
                });
            });

            if (!string.IsNullOrWhiteSpace(notice.Series))
            {
                column.Item().PaddingTop(3).Text(t =>
                {
                    t.Span("Série : ").Bold().FontSize(10);
                    t.Span(notice.Series).FontSize(10);
                });
            }
        });
    }

    /// <summary>
    /// Sur A5 paysage il n'y a pas la hauteur pour empiler le visa puis le pied d'authenticité comme
    /// sur l'ancienne A4 : QR + référence à gauche, visa du chef d'établissement à droite, même bande.
    /// </summary>
    private void ComposeSignatureAndFooter(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().AlignBottom().Element(c =>
                OfficialHeaderComponent.ComposeAuthenticityFooter(c, qrCodeImage, notice.Reference));

            row.ConstantItem(150).Column(right =>
            {
                right.Item().AlignCenter().Text("Le Chef d'Établissement").FontSize(9);
                right.Item().PaddingTop(20).AlignCenter().Text("[Signature et Cachet]").FontSize(8).FontColor(Colors.Grey.Medium);
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
