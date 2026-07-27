using System.Globalization;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetDailyClosingReportPdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class DailyClosingReportDocument(DailyClosingReportDto report, byte[]? logo) : IDocument
{
    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Rapport de Clôture de Caisse - {FormatDate(report.Date)}",
        Author = report.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4.Portrait());
            page.Margin(15, Unit.Millimetre);
            page.DefaultTextStyle(text => text.FontSize(9).FontColor(Colors.Black));

            page.Content().Column(column =>
            {
                ComposeHeader(column);

                column.Item().PaddingTop(15).Element(ComposeSummaryCards);

                column.Item().PaddingTop(15).Row(row =>
                {
                    row.RelativeItem().PaddingRight(10).Element(ComposeMethodBreakdown);
                    row.RelativeItem().PaddingLeft(10).Element(ComposeCategoryBreakdown);
                });

                column.Item().PaddingTop(20).Element(ComposeTransactionGrid);

                column.Item().PaddingTop(40).Element(ComposeSignatures);
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
        column.Item().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).PaddingBottom(10).Row(row =>
        {
            row.RelativeItem().Column(header =>
            {
                header.Item().Text(report.SchoolName.ToUpperInvariant()).Bold().FontSize(14).FontColor(Colors.Blue.Darken2);
                header.Item().Text(report.SchoolAddress).FontSize(8).FontColor(Colors.Grey.Medium);
                
                header.Item().PaddingTop(10).Text("Rapport de Clôture de Caisse Quotidien").Bold().FontSize(12);

                header.Item().PaddingTop(5).Text($"Date du jour : {FormatDate(report.Date)}");
                header.Item().Text($"Identifiant unique de session : {report.SessionId}");
                header.Item().Text($"Nom de l'agent caissier : {report.CashierName}");
                header.Item().Text($"Plage horaire de la session : {report.TimeRange}");
            });

            if (logo is not null)
            {
                row.ConstantItem(70).MaxHeight(70).Image(logo).FitArea();
            }
        });
    }

    private void ComposeSummaryCards(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(col => 
            {
                col.Item().Text("Fonds de caisse initial").FontSize(8).FontColor(Colors.Grey.Darken1);
                col.Item().Text($"{FormatMoney(report.OpeningBalance)} FCFA").Bold().FontSize(12).FontColor(Colors.Blue.Darken2);
            });

            row.Spacing(10);

            row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(col => 
            {
                col.Item().Text("Total Encaissé").FontSize(8).FontColor(Colors.Grey.Darken1);
                col.Item().Text($"{FormatMoney(report.TotalCollected)} FCFA").Bold().FontSize(12).FontColor(Colors.Green.Darken2);
            });

            row.Spacing(10);

            row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(col => 
            {
                col.Item().Text("Total Espèces en Caisse").FontSize(8).FontColor(Colors.Grey.Darken1);
                col.Item().Text($"{FormatMoney(report.TotalCashInRegister)} FCFA").Bold().FontSize(12).FontColor(Colors.Orange.Darken2);
            });
        });
    }

    private void ComposeMethodBreakdown(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().PaddingBottom(5).Text("Répartition par Mode de Paiement").Bold().FontSize(10);
            
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).PaddingBottom(2).Text("Mode").FontSize(8).FontColor(Colors.Grey.Darken2);
                    header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).PaddingBottom(2).AlignRight().Text("Montant").FontSize(8).FontColor(Colors.Grey.Darken2);
                });

                foreach (var method in report.MethodBreakdowns)
                {
                    table.Cell().PaddingVertical(2).Text(method.Method.ToString()).FontSize(8);
                    table.Cell().PaddingVertical(2).AlignRight().Text($"{FormatMoney(method.Amount)} FCFA").FontSize(8).Bold();
                }
            });
        });
    }

    private void ComposeCategoryBreakdown(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().PaddingBottom(5).Text("Répartition par Catégorie de Frais").Bold().FontSize(10);
            
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).PaddingBottom(2).Text("Catégorie").FontSize(8).FontColor(Colors.Grey.Darken2);
                    header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).PaddingBottom(2).AlignRight().Text("Montant").FontSize(8).FontColor(Colors.Grey.Darken2);
                });

                foreach (var category in report.CategoryBreakdowns)
                {
                    table.Cell().PaddingVertical(2).Text(category.CategoryName).FontSize(8);
                    table.Cell().PaddingVertical(2).AlignRight().Text($"{FormatMoney(category.Amount)} FCFA").FontSize(8).Bold();
                }
            });
        });
    }

    private void ComposeTransactionGrid(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().PaddingBottom(5).Text("Grille Détaillée des Transactions").Bold().FontSize(10);

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(40);
                    columns.ConstantColumn(80);
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.ConstantColumn(60);
                    columns.ConstantColumn(80);
                });

                table.Header(header =>
                {
                    header.Cell().BorderBottom(1).BorderColor(Colors.Black).PaddingBottom(2).Text("Heure").FontSize(8).Bold();
                    header.Cell().BorderBottom(1).BorderColor(Colors.Black).PaddingBottom(2).Text("Reçu").FontSize(8).Bold();
                    header.Cell().BorderBottom(1).BorderColor(Colors.Black).PaddingBottom(2).Text("Élève / Tiers").FontSize(8).Bold();
                    header.Cell().BorderBottom(1).BorderColor(Colors.Black).PaddingBottom(2).Text("Catégorie").FontSize(8).Bold();
                    header.Cell().BorderBottom(1).BorderColor(Colors.Black).PaddingBottom(2).Text("Mode").FontSize(8).Bold();
                    header.Cell().BorderBottom(1).BorderColor(Colors.Black).PaddingBottom(2).AlignRight().Text("Montant").FontSize(8).Bold();
                });

                foreach (var tx in report.Transactions)
                {
                    table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).Text(tx.Time).FontSize(8);
                    table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).Text(NoBreakText.NoBreak(tx.ReceiptNumber)).FontSize(8);
                    table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).Text(tx.StudentName).FontSize(8);
                    table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).Text(tx.Category).FontSize(8);
                    table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).Text(tx.Method).FontSize(8);
                    table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).AlignRight().Text(FormatMoney(tx.Amount)).FontSize(8).Bold();
                }
            });
        });
    }

    private void ComposeSignatures(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().AlignCenter().Column(col =>
            {
                col.Item().Text("Visa de l'Agent Caissier").FontSize(9).Bold();
                col.Item().PaddingTop(30).Text("......................................................").FontColor(Colors.Grey.Medium);
            });

            row.RelativeItem().AlignCenter().Column(col =>
            {
                col.Item().Text("Visa du Contrôleur / Gérant").FontSize(9).Bold();
                col.Item().PaddingTop(30).Text("......................................................").FontColor(Colors.Grey.Medium);
            });
        });
    }

    private static string FormatMoney(decimal amount) =>
        amount.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ");

    private static string FormatDate(DateTime date) =>
        date.ToString("dddd d MMMM yyyy", new CultureInfo("fr-FR"));
}

public class DailyClosingReportPdfGenerator : IDailyClosingReportPdfGenerator
{
    public byte[] Generate(DailyClosingReportDto report, byte[]? schoolLogo)
    {
        var document = new DailyClosingReportDocument(report, schoolLogo);
        return document.GeneratePdf();
    }
}
