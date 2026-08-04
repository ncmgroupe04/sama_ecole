using System.Globalization;
using SamaEcole.Application.Common;
using SamaEcole.Application.Finance.Queries.GetDuesNotice;
using SamaEcole.Infrastructure.Documents.Components;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Sommation pour impayés (A4 portrait) — lettre de mise en demeure sur papier à en-tête de
/// l'établissement (même charte que le reçu/bulletin de paie : NINEA, pas de bandeau M.E.N., ce
/// n'est pas un acte académique mais une communication financière de l'école en tant qu'entité privée).
/// </summary>
public class DuesNoticeDocument(DuesNoticeDto notice, byte[]? logo, byte[] qrCodeImage) : IDocument
{
    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Sommation {notice.NoticeNumber}",
        Author = notice.SchoolName
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

                column.Item().PaddingTop(16).AlignCenter()
                    .Text("SOMMATION POUR IMPAYÉS").Bold().FontSize(15);
                column.Item().AlignCenter()
                    .Text($"N° {NoBreakText.NoBreak(notice.NoticeNumber)}").FontSize(8).FontColor(Colors.Grey.Darken1);

                column.Item().PaddingTop(16).Text(FaitA()).AlignRight().Italic().FontSize(9);

                column.Item().PaddingTop(10).Element(ComposeRecipientBlock);
                column.Item().PaddingTop(14).Element(ComposeBody);
                column.Item().PaddingTop(10).Element(ComposeInstallmentsTable);
                column.Item().PaddingTop(10).Element(ComposeTotalBlock);
                column.Item().PaddingTop(20).Element(ComposeClosing);
                column.Item().PaddingTop(30).Element(ComposeSignature);
                column.Item().PaddingTop(24).Element(c => OfficialHeaderComponent.ComposeAuthenticityFooter(c, qrCodeImage, notice.NoticeNumber));
            });
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingBottom(6).Row(row =>
        {
            row.RelativeItem().Column(header =>
            {
                header.Item().Text(notice.SchoolName.ToUpperInvariant()).Bold().FontSize(13);

                var contact = JoinPresent(notice.SchoolAddress, PhoneFormatter.FormatSenegal(notice.SchoolPhone));
                if (contact.Length > 0)
                {
                    header.Item().Text(contact).FontSize(7).FontColor(Colors.Grey.Darken2);
                }

                if (!string.IsNullOrWhiteSpace(notice.SchoolNinea))
                {
                    header.Item().Text($"NINEA : {notice.SchoolNinea}").FontSize(7).FontColor(Colors.Grey.Darken2);
                }
            });

            if (logo is not null)
            {
                row.ConstantItem(60).MaxHeight(42).AlignRight().Image(logo).FitArea();
            }
        });
    }

    private void ComposeRecipientBlock(IContainer container)
    {
        container.Text(text =>
        {
            text.DefaultTextStyle(style => style.FontSize(10));
            text.Span("À l'attention de : ").SemiBold();
            text.Span(string.IsNullOrWhiteSpace(notice.GuardianName) ? "Tuteur / Responsable légal" : notice.GuardianName);
            if (!string.IsNullOrWhiteSpace(notice.GuardianPhone))
            {
                text.Span($" ({PhoneFormatter.FormatSenegal(notice.GuardianPhone)})");
            }
        });
    }

    private void ComposeBody(IContainer container)
    {
        container.Text(text =>
        {
            text.DefaultTextStyle(style => style.LineHeight(1.5f));
            text.Justify();
            text.Span("Nous portons à votre connaissance que le compte scolaire de l'élève ");
            text.Span(notice.StudentFullName).Bold();
            text.Span($" (matricule {NoBreakText.NoBreak(notice.Matricule)}, classe {notice.ClassroomName}, année scolaire {notice.SchoolYearLabel}) présente à ce jour ");
            text.Span("une ou plusieurs échéances de frais de scolarité échues et non réglées, détaillées ci-dessous.");
        });
    }

    private void ComposeInstallmentsTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();                       // Échéance — libellé libre, prend le reste
                columns.ConstantColumn(PdfColumnWidths.Date);   // Date d'exigibilité — l'année ne saute plus à la ligne
                columns.ConstantColumn(PdfColumnWidths.Amount); // Montant dû
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("Échéance").Bold();
                header.Cell().Element(HeaderCell).Text("Date d'exigibilité").Bold();
                header.Cell().Element(HeaderCell).AlignRight().Text("Montant dû").Bold();
            });

            foreach (var installment in notice.OverdueInstallments)
            {
                table.Cell().Element(BodyCell).Text(installment.Designation);
                table.Cell().Element(BodyCell).Text(FormatDate(installment.DueDate));
                table.Cell().Element(BodyCell).AlignRight().Text(FormatMoney(installment.Amount));
            }
        });
    }

    private void ComposeTotalBlock(IContainer container)
    {
        container.Background(Colors.Grey.Lighten4).Padding(10).Row(row =>
        {
            row.RelativeItem().Text("SOLDE TOTAL RESTANT DÛ").Bold().FontSize(11);
            row.RelativeItem().AlignRight().Text(FormatMoney(notice.RemainingBalance)).Bold().FontSize(13);
        });
    }

    private void ComposeClosing(IContainer container)
    {
        container.Text(text =>
        {
            text.DefaultTextStyle(style => style.LineHeight(1.5f));
            text.Justify();
            text.Span("Nous vous prions de bien vouloir régulariser cette situation dans les meilleurs délais ");
            text.Span("auprès du service Finance de l'établissement. À défaut de régularisation, l'établissement ");
            text.Span("se réserve le droit d'appliquer les mesures prévues par le règlement intérieur.");
        });
    }

    private void ComposeSignature(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem();
            row.RelativeItem().Column(right =>
            {
                right.Item().AlignCenter().Text("Le Service Finance").FontSize(10);
                right.Item().PaddingTop(30).AlignCenter()
                    .Text("[Signature et Cachet]").FontSize(8).FontColor(Colors.Grey.Medium);
            });
        });
    }

    private string FaitA() =>
        string.IsNullOrWhiteSpace(notice.SchoolCity)
            ? $"Fait le {FormatDate(notice.IssuedAt)}"
            : $"Fait à {notice.SchoolCity}, le {FormatDate(notice.IssuedAt)}";

    private static IContainer HeaderCell(IContainer container) =>
        container.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken3))
            .PaddingVertical(4).BorderBottom(1).BorderColor(Colors.Grey.Darken1);

    private static IContainer BodyCell(IContainer container) =>
        container.PaddingVertical(4).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2);

    private static string JoinPresent(params string?[] parts) =>
        string.Join("  ·  ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static string FormatDate(DateTimeOffset moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string FormatDate(DateOnly moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string FormatMoney(decimal amount) =>
        amount.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ") + " FCFA";
}
