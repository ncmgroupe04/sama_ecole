using System.Globalization;
using SamaEcole.Application.Finance.Queries.GetTaxDeclaration;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Déclaration Fiscale mensuelle (A4 portrait) — état synthétique des charges sociales sénégalaises
/// (IPRES, CSS, VRS, BRS) et de la TVA (collectée/déductible/nette) remis chaque mois à
/// l'administration fiscale. Document Finance interne (comme le bulletin de paie) : pas de mention du
/// formalisme M.E.N., juste l'identité de l'établissement comme déclarant.
/// </summary>
public class TaxDeclarationDocument(TaxDeclarationDto declaration, byte[]? logo) : IDocument
{
    private static readonly CultureInfo French = new("fr-FR");

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Déclaration fiscale {declaration.DeclarationNumber}",
        Author = declaration.SchoolName
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
                    .Text("DÉCLARATION FISCALE MENSUELLE").Bold().FontSize(15);
                column.Item().AlignCenter().Text("État synthétique — VRS, IPRES, CSS, BRS et TVA").FontSize(9).FontColor(Colors.Grey.Darken2);
                column.Item().AlignCenter().Text(PeriodLabel()).FontSize(10).FontColor(Colors.Grey.Darken2);
                column.Item().AlignCenter()
                    .Text($"N° {NoBreakText.NoBreak(declaration.DeclarationNumber)}").FontSize(8).FontColor(Colors.Grey.Darken1);

                column.Item().PaddingTop(16).Element(ComposeChargesTable);
                column.Item().PaddingTop(14).Element(ComposeTvaTable);
                column.Item().PaddingTop(6).Element(ComposeTotalDueBlock);
                column.Item().PaddingTop(30).Element(ComposeSignatures);
            });
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingBottom(6).Row(row =>
        {
            row.RelativeItem().Column(header =>
            {
                header.Item().Text(declaration.SchoolName.ToUpperInvariant()).Bold().FontSize(13);

                if (!string.IsNullOrWhiteSpace(declaration.SchoolAddress))
                {
                    header.Item().Text(declaration.SchoolAddress).FontSize(7).FontColor(Colors.Grey.Darken2);
                }

                if (!string.IsNullOrWhiteSpace(declaration.SchoolNinea))
                {
                    header.Item().Text($"NINEA : {declaration.SchoolNinea}").FontSize(7).FontColor(Colors.Grey.Darken2);
                }
            });

            if (logo is not null)
            {
                row.ConstantItem(60).MaxHeight(42).AlignRight().Image(logo).FitArea();
            }
        });
    }

    private void ComposeChargesTable(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Text("CHARGES SOCIALES").Bold().FontSize(10).FontColor(Colors.Grey.Darken3);
            column.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(4);
                    columns.RelativeColumn(2);
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("Cotisation").Bold();
                    header.Cell().Element(HeaderCell).AlignRight().Text("Montant").Bold();
                });

                Row("IPRES (retraite)", declaration.TotalIpres);
                Row("CSS (prestations familiales)", declaration.TotalCss);
                Row("VRS / CFCE", declaration.TotalVrs);
                Row("BRS (retenue à la source)", declaration.TotalBrs);

                void Row(string label, decimal amount)
                {
                    table.Cell().Element(BodyCell).Text(label);
                    table.Cell().Element(BodyCell).AlignRight().Text(FormatMoney(amount));
                }
            });
        });
    }

    private void ComposeTvaTable(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Text("TAXE SUR LA VALEUR AJOUTÉE (TVA)").Bold().FontSize(10).FontColor(Colors.Grey.Darken3);
            column.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(4);
                    columns.RelativeColumn(2);
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("Élément").Bold();
                    header.Cell().Element(HeaderCell).AlignRight().Text("Montant").Bold();
                });

                Row("TVA collectée", declaration.TvaCollected);
                Row("TVA déductible", declaration.TvaDeductible);
                Row("TVA nette", declaration.NetTva);

                void Row(string label, decimal amount)
                {
                    table.Cell().Element(BodyCell).Text(label);
                    table.Cell().Element(BodyCell).AlignRight().Text(FormatMoney(amount));
                }
            });
        });
    }

    private void ComposeTotalDueBlock(IContainer container)
    {
        container.Background(Colors.Grey.Lighten4).Padding(10).Row(row =>
        {
            row.RelativeItem().Text("TOTAL DÛ À L'ÉTAT").Bold().FontSize(11);
            row.RelativeItem().AlignRight().Text(FormatMoney(declaration.TotalDueToState)).Bold().FontSize(13);
        });
    }

    private void ComposeSignatures(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text("Le Service Finance").Italic().FontSize(9);
                left.Item().PaddingTop(30).Text("________________________").FontSize(9);
            });
            row.RelativeItem().AlignRight().Column(right =>
            {
                right.Item().AlignRight().Text("Le Directeur").Italic().FontSize(9);
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
        new DateTime(declaration.Year, declaration.Month, 1).ToString("MMMM yyyy", French);

    private static string FormatMoney(decimal amount) =>
        amount.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ") + " FCFA";
}
