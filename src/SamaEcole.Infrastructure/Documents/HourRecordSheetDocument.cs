using System.Globalization;
using SamaEcole.Application.Finance.Queries.GetHourRecordSheet;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Fiche de suivi des heures d'un Vacataire (A4 portrait) — document RH interne, même charte que le
/// bulletin de paie (<see cref="PayslipDocument"/>) : pas de bandeau M.E.N.
/// </summary>
public class HourRecordSheetDocument(HourRecordSheetDto sheet, byte[]? logo) : IDocument
{
    private static readonly CultureInfo French = new("fr-FR");

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Fiche d'heures {sheet.SheetNumber}",
        Author = sheet.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(20, Unit.Millimetre);
            page.DefaultTextStyle(text => text.FontSize(9).FontColor(Colors.Black));

            page.Content().Column(column =>
            {
                column.Item().Element(ComposeHeader);

                column.Item().PaddingTop(16).AlignCenter()
                    .Text("FICHE DE SUIVI DES HEURES").Bold().FontSize(15);
                column.Item().AlignCenter().Text(PeriodLabel()).FontSize(10).FontColor(Colors.Grey.Darken2);
                column.Item().AlignCenter()
                    .Text($"N° {sheet.SheetNumber}").FontSize(8).FontColor(Colors.Grey.Darken1);

                column.Item().PaddingTop(16).Element(ComposeEmployeeBlock);
                column.Item().PaddingTop(14).Element(ComposeHoursTable);
                column.Item().PaddingTop(6).Element(ComposeTotalBlock);
                column.Item().PaddingTop(30).Element(ComposeSignatures);
            });
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingBottom(6).Row(row =>
        {
            row.RelativeItem().Text(sheet.SchoolName.ToUpperInvariant()).Bold().FontSize(13);

            if (logo is not null)
            {
                row.ConstantItem(60).MaxHeight(42).AlignRight().Image(logo).FitArea();
            }
        });
    }

    private void ComposeEmployeeBlock(IContainer container)
    {
        container.Border(0.75f).BorderColor(Colors.Grey.Lighten1).Padding(8).Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                InfoRow(left, "Employé(e)", sheet.EmployeeFullName);
                InfoRow(left, "Type de contrat", "Vacataire (horaire)");
            });
            row.RelativeItem().Column(right =>
            {
                InfoRow(right, "Taux horaire", FormatMoney(sheet.HourlyRate) + " / h");
                InfoRow(right, "Total du mois", $"{sheet.TotalHours:0.##} h");
            });
        });
    }

    private static void InfoRow(ColumnDescriptor column, string label, string value) =>
        column.Item().PaddingVertical(1).Row(row =>
        {
            row.ConstantItem(100).Text($"{label} :").FontColor(Colors.Grey.Darken2);
            row.RelativeItem().Text(value).SemiBold();
        });

    private void ComposeHoursTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(2);
                columns.RelativeColumn(2);
                columns.RelativeColumn(5);
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("Date").Bold();
                header.Cell().Element(HeaderCell).AlignRight().Text("Heures").Bold();
                header.Cell().Element(HeaderCell).Text("Note").Bold();
            });

            if (sheet.Lines.Count == 0)
            {
                table.Cell().ColumnSpan(3).Element(BodyCell).AlignCenter()
                    .Text("Aucune heure enregistrée pour cette période.").Italic().FontColor(Colors.Grey.Medium);
            }

            foreach (var line in sheet.Lines)
            {
                table.Cell().Element(BodyCell).Text(FormatDate(line.Date));
                table.Cell().Element(BodyCell).AlignRight().Text(line.Hours.ToString("0.##", CultureInfo.InvariantCulture));
                table.Cell().Element(BodyCell).Text(line.Note ?? "—");
            }
        });
    }

    private void ComposeTotalBlock(IContainer container)
    {
        container.Background(Colors.Grey.Lighten4).Padding(10).Row(row =>
        {
            row.RelativeItem().Text("TOTAL HEURES DU MOIS").Bold().FontSize(11);
            row.RelativeItem().AlignRight().Text($"{sheet.TotalHours:0.##} h").Bold().FontSize(13);
        });
    }

    private void ComposeSignatures(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text("Signature de l'Employé(e)").Italic().FontSize(9);
                left.Item().PaddingTop(30).Text("________________________").FontSize(9);
            });
            row.RelativeItem().AlignRight().Column(right =>
            {
                right.Item().AlignRight().Text("Visa du Superviseur").Italic().FontSize(9);
                right.Item().PaddingTop(30).AlignRight().Text("________________________").FontSize(9);
            });
        });
    }

    private static IContainer HeaderCell(IContainer container) =>
        container.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken3))
            .PaddingVertical(4).BorderBottom(1).BorderColor(Colors.Grey.Darken1);

    private static IContainer BodyCell(IContainer container) =>
        container.PaddingVertical(4).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2);

    private string PeriodLabel() =>
        new DateTime(sheet.Year, sheet.Month, 1).ToString("MMMM yyyy", French);

    private static string FormatDate(DateOnly moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string FormatMoney(decimal amount) =>
        amount.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ") + " FCFA";
}
