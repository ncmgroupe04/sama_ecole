using System.Globalization;
using SamaEcole.Application.Common;
using SamaEcole.Application.Finance.Queries.GetDailyCashRegisterPdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class DailyCashRegisterDocument(DailyCashRegisterDto report, byte[]? logo) : IDocument
{
    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Journal de Caisse - {FormatDate(report.Date)}",
        Author = report.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4.Portrait());
            page.Margin(10, Unit.Millimetre);
            page.DefaultTextStyle(text => text.FontSize(9).FontColor(Colors.Black));

            page.Content().Column(column =>
            {
                ComposeHeader(column);

                column.Item().PaddingTop(10).AlignCenter()
                    .Text($"JOURNAL DE CAISSE DU {FormatDate(report.Date)}").Bold().FontSize(12).Underline();

                column.Item().PaddingTop(12).Element(ComposeSummaryBlock);

                column.Item().PaddingTop(16).Element(ComposeDetailedTable);

                column.Item().PaddingTop(20).Element(ComposeSignatures);
            });
            
            page.Footer()
                .AlignCenter()
                .Text(x =>
                {
                    x.Span("Page ");
                    x.CurrentPageNumber();
                    x.Span(" sur ");
                    x.TotalPages();
                });
        });
    }

    private void ComposeHeader(ColumnDescriptor column)
    {
        column.Item().BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingBottom(6).Row(row =>
        {
            row.RelativeItem().Column(header =>
            {
                header.Item().Text(report.SchoolName.ToUpperInvariant()).Bold().FontSize(14);

                var contact = JoinPresent(report.SchoolAddress, PhoneFormatter.FormatSenegal(report.SchoolPhone), report.SchoolEmail);
                if (contact.Length > 0)
                {
                    header.Item().Text(contact).FontSize(8).FontColor(Colors.Grey.Darken2);
                }
            });

            if (logo is not null)
            {
                row.ConstantItem(70).MaxHeight(50).Image(logo).FitArea();
            }
            else
            {
                row.ConstantItem(70).AlignRight().AlignMiddle()
                    .Text("[Logo]").FontSize(8).FontColor(Colors.Grey.Medium);
            }
        });
    }

    private void ComposeSummaryBlock(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem(2).Column(column =>
            {
                column.Item().Text("RÉCAPITULATIF PAR MODE DE PAIEMENT").Bold().FontSize(10).FontColor(Colors.Grey.Darken3);
                column.Item().PaddingTop(4).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(1);
                    });

                    foreach (var total in report.TotalsByMethod)
                    {
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).Text(MethodLabel(total.Method));
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).AlignRight().Text(FormatMoney(total.Total)).Bold();
                    }

                    table.Cell().PaddingTop(4).Text("TOTAL ENCAISSÉ").Bold();
                    table.Cell().PaddingTop(4).AlignRight().Text(FormatMoney(report.TotalCollected)).Bold().FontSize(10);
                });
            });

            row.ConstantItem(20);

            row.RelativeItem(1).Background(Colors.Grey.Lighten4).Padding(6).Column(column =>
            {
                column.Item().Text("INFORMATIONS").Bold().FontSize(8).FontColor(Colors.Grey.Darken2);
                column.Item().PaddingTop(2).Text($"Date : {FormatDate(report.Date)}").FontSize(8);
                column.Item().Text($"Transactions : {report.Payments.Count}").FontSize(8);
            });
        });
    }

    private void ComposeDetailedTable(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().PaddingBottom(4).Text("DÉTAIL DES ENCAISSEMENTS").Bold().FontSize(10).FontColor(Colors.Grey.Darken3);

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(PdfColumnWidths.Time);       // Heure
                    columns.ConstantColumn(PdfColumnWidths.Identifier); // Matricule
                    columns.RelativeColumn(3);                          // Nom — seule colonne à contenu libre, prend le reste
                    columns.ConstantColumn(PdfColumnWidths.Identifier); // Reçu N° — « REC‑2025‑0002 », même gabarit qu'un matricule
                    columns.RelativeColumn(1.2f);                       // Mode — libellés courts (« Espèces », « Wave »)
                    columns.ConstantColumn(PdfColumnWidths.Amount);     // Montant
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("Heure").Bold();
                    header.Cell().Element(HeaderCell).Text("Matricule").Bold().FontSize(8f);
                    header.Cell().Element(HeaderCell).Text("Nom Complet").Bold();
                    header.Cell().Element(HeaderCell).Text("Reçu N°").Bold();
                    header.Cell().Element(HeaderCell).Text("Mode").Bold();
                    header.Cell().Element(HeaderCell).AlignRight().Text("Montant").Bold();
                });

                if (report.Payments.Count == 0)
                {
                    table.Cell().ColumnSpan(6).Padding(8).AlignCenter().Text("Aucun encaissement à cette date.").Italic();
                }
                else
                {
                    foreach (var payment in report.Payments)
                    {
                        table.Cell().Element(BodyCell).Text(FormatTime(payment.PaidAt));
                        table.Cell().Element(BodyCell).Text(NoBreakText.NoBreak(payment.Matricule)).FontSize(8f);
                        table.Cell().Element(BodyCell).Text(payment.StudentFullName);
                        table.Cell().Element(BodyCell).Text(NoBreakText.NoBreak(payment.ReceiptNumber));
                        table.Cell().Element(BodyCell).Text(MethodLabel(payment.Method));
                        table.Cell().Element(BodyCell).AlignRight().Text(FormatMoney(payment.Amount)).Bold();
                    }
                }
            });
        });

        static IContainer HeaderCell(IContainer c) =>
            c.BorderBottom(1).BorderColor(Colors.Grey.Darken1).Background(Colors.Grey.Lighten3).PaddingVertical(4).PaddingHorizontal(2);
        static IContainer BodyCell(IContainer c) =>
            c.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).PaddingHorizontal(2);
    }

    private void ComposeSignatures(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text($"Fait le {FormatDate(DateTimeOffset.Now)}").Italic();
                left.Item().PaddingTop(20).Text("Le Caissier / Service Financier").Bold();
            });

            row.RelativeItem().AlignRight().Column(right =>
            {
                right.Item().AlignRight().Text("Le Directeur").Bold();
            });
        });
    }

    private static string MethodLabel(string method) => method switch
    {
        "Cash" => "Espèces",
        "Cheque" => "Chèque",
        "Transfer" => "Virement",
        "MobileMoney" => "Mobile Money",
        _ => method
    };

    private static string JoinPresent(params string?[] parts) =>
        string.Join("  ·  ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static string FormatMoney(decimal amount) =>
        amount.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ") + " FCFA";

    private static string FormatDate(DateOnly date) =>
        date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTimeOffset moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        
    private static string FormatTime(DateTimeOffset moment) =>
        moment.ToString("HH:mm", CultureInfo.InvariantCulture);
}
