using System.Globalization;
using SamaEcole.Application.Finance.Queries.GetFinancialCommitment;
using SamaEcole.Infrastructure.Documents.Components;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Engagement financier / reconnaissance de dette (A4 portrait) — trace écrite d'un accord
/// d'échéancier entre l'établissement et le tuteur, sur papier à en-tête de l'école (comme le reçu),
/// sans bandeau M.E.N. : ce n'est pas un acte académique.
/// </summary>
public class FinancialCommitmentDocument(FinancialCommitmentDto commitment, byte[]? logo, byte[] qrCodeImage) : IDocument
{
    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Engagement financier {commitment.CommitmentNumber}",
        Author = commitment.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(20, Unit.Millimetre);
            page.DefaultTextStyle(text => text.FontSize(10).FontColor(Colors.Black));

            page.Content().Column(column =>
            {
                column.Item().Element(ComposeHeader);

                column.Item().PaddingTop(20).AlignCenter()
                    .Text("ENGAGEMENT FINANCIER").Bold().FontSize(15).Underline();
                column.Item().AlignCenter()
                    .Text("(Reconnaissance de dette et échéancier de paiement)").Italic().FontSize(9).FontColor(Colors.Grey.Darken2);
                column.Item().AlignCenter()
                    .Text($"N° {commitment.CommitmentNumber}").FontSize(8).FontColor(Colors.Grey.Darken1);

                column.Item().PaddingTop(20).Element(ComposeBody);
                column.Item().PaddingTop(14).Element(ComposeAmountBlock);
                column.Item().PaddingTop(14).Element(ComposeTermsBlock);
                column.Item().PaddingTop(36).Element(ComposeSignatures);
                column.Item().PaddingTop(24).Element(c => OfficialHeaderComponent.ComposeAuthenticityFooter(c, qrCodeImage, commitment.CommitmentNumber));
            });
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingBottom(6).Row(row =>
        {
            row.RelativeItem().Column(header =>
            {
                header.Item().Text(commitment.SchoolName.ToUpperInvariant()).Bold().FontSize(13);

                var contact = JoinPresent(commitment.SchoolAddress, commitment.SchoolPhone);
                if (contact.Length > 0)
                {
                    header.Item().Text(contact).FontSize(7).FontColor(Colors.Grey.Darken2);
                }

                if (!string.IsNullOrWhiteSpace(commitment.SchoolNinea))
                {
                    header.Item().Text($"NINEA : {commitment.SchoolNinea}").FontSize(7).FontColor(Colors.Grey.Darken2);
                }
            });

            if (logo is not null)
            {
                row.ConstantItem(60).MaxHeight(42).AlignRight().Image(logo).FitArea();
            }
        });
    }

    private void ComposeBody(IContainer container)
    {
        container.Text(text =>
        {
            text.DefaultTextStyle(style => style.LineHeight(1.6f));
            text.Justify();
            text.Span("Je soussigné(e), ");
            text.Span(string.IsNullOrWhiteSpace(commitment.GuardianName) ? "le tuteur / la tutrice" : commitment.GuardianName).Bold();
            if (!string.IsNullOrWhiteSpace(commitment.GuardianPhone))
            {
                text.Span($" ({commitment.GuardianPhone})");
            }
            text.Span(", responsable légal(e) de l'élève ");
            text.Span(commitment.StudentFullName).Bold();
            text.Span($" (matricule {commitment.Matricule}, classe {commitment.ClassroomName}, année scolaire {commitment.SchoolYearLabel}), ");
            text.Span("reconnais devoir à l'établissement ");
            text.Span(commitment.SchoolName).Bold();
            text.Span(" la somme précisée ci-dessous, et m'engage à la régler selon l'échéancier convenu.");
        });
    }

    private void ComposeAmountBlock(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Background(Colors.Grey.Lighten4).Padding(10).Row(row =>
            {
                row.RelativeItem().Text("MONTANT DE L'ENGAGEMENT").Bold().FontSize(11);
                row.RelativeItem().AlignRight().Text(FormatMoney(commitment.Amount)).Bold().FontSize(13);
            });
            column.Item().PaddingTop(4).Text($"Échéance convenue : {FormatDate(commitment.DueDate)}").FontSize(9).FontColor(Colors.Grey.Darken2);
        });
    }

    private void ComposeTermsBlock(IContainer container)
    {
        container.Border(0.75f).BorderColor(Colors.Grey.Darken1).Padding(8).Column(column =>
        {
            column.Item().Text("MODALITÉS CONVENUES").Bold().FontSize(9).FontColor(Colors.Grey.Darken3);
            column.Item().PaddingTop(4).Text(commitment.Terms).FontSize(10);
        });
    }

    private void ComposeSignatures(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().AlignCenter().Text(FaitA()).Italic().FontSize(10);
                left.Item().PaddingTop(4).AlignCenter().Text("Signature du parent / tuteur").FontSize(10);
                left.Item().PaddingTop(30).AlignCenter().Text("________________________").FontSize(9);
            });
            row.RelativeItem().Column(right =>
            {
                right.Item().AlignCenter().Text("Pour l'établissement").FontSize(10);
                right.Item().PaddingTop(4).AlignCenter().Text("Le Service Finance").FontSize(10);
                right.Item().PaddingTop(30).AlignCenter()
                    .Text("[Signature et Cachet]").FontSize(8).FontColor(Colors.Grey.Medium);
            });
        });
    }

    private string FaitA()
    {
        var date = FormatDate(commitment.SignedAt);
        return string.IsNullOrWhiteSpace(commitment.SchoolCity)
            ? $"Fait le {date}"
            : $"Fait à {commitment.SchoolCity}, le {date}";
    }

    private static string JoinPresent(params string?[] parts) =>
        string.Join("  ·  ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static string FormatDate(DateTimeOffset moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string FormatDate(DateOnly moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string FormatMoney(decimal amount) =>
        amount.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ") + " FCFA";
}
