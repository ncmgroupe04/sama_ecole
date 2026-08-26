using System.Globalization;
using SamaEcole.Application.Inventory;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Fiche d'inventaire global de l'établissement (GET /inventory/reports/global/pdf) : un tableau des
/// lots du périmètre filtré, groupé par catégorie, avec sous-totaux et valorisation.
///
/// A4 PAYSAGE, et non portrait : huit colonnes dont un libellé, un code d'inventaire et un
/// emplacement. En portrait, ce sont les trois colonnes textuelles qui se replient sur deux lignes —
/// exactement ce que <see cref="PdfColumnWidths"/> existe pour éviter ailleurs.
///
/// Document de TRAVAIL interne présenté à la mairie ou à l'IEF, pas une pièce officielle nominative :
/// même style épuré que <see cref="StudentsExportDocument"/> — pas de logo, pas de cachet, pas de QR.
/// </summary>
public class InventoryReportDocument(InventoryReportModel model) : IDocument
{
    /// <summary>Un lot sans prix saisi n'affiche pas « 0 » mais un tiret cadratin.</summary>
    private const string NoValue = "—";

    private static readonly CultureInfo FrenchCulture = CultureInfo.GetCultureInfo("fr-FR");

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Inventaire — {model.CategoryFilterName ?? "Toutes catégories"}",
        Author = model.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(1.5f, Unit.Centimetre);
            page.DefaultTextStyle(text => text.FontSize(8.5f).FontColor(Colors.Black));

            page.Header().Element(ComposeHeader);
            page.Content().PaddingTop(8).Element(ComposeBody);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.BorderBottom(1).BorderColor(Colors.Black).PaddingBottom(4).Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Text(model.SchoolName.ToUpperInvariant()).Bold().FontSize(12);
                row.RelativeItem().AlignRight().Text("FICHE D'INVENTAIRE").Bold().FontSize(12);
            });

            // IA / IEF : imprimées seulement si l'école les a renseignées — jamais une ligne vide
            // légendée, jamais une valeur inventée (convention des documents officiels du projet).
            var administration = string.Join(
                "  ·  ",
                new[] { model.InspectionAcademie, model.InspectionEducationFormation }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));

            if (administration.Length > 0)
            {
                column.Item().PaddingTop(2).Text(administration).FontSize(8).FontColor(Colors.Grey.Darken2);
            }

            column.Item().PaddingTop(3).Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.Span("Catégorie : ").SemiBold();
                    text.Span(model.CategoryFilterName ?? "Toutes les catégories");
                });
                row.RelativeItem().Text(text =>
                {
                    text.Span("Emplacement : ").SemiBold();
                    text.Span(model.LocationFilterName ?? "Tous les emplacements");
                });
                row.RelativeItem().AlignRight().Text(text =>
                {
                    text.Span("Effectif total : ").SemiBold();
                    text.Span(Number(model.GrandTotalQuantity)).Bold();
                });
            });
        });
    }

    private void ComposeBody(IContainer container)
    {
        if (model.Groups.Count == 0)
        {
            container.PaddingTop(40).AlignCenter()
                .Text("Aucun bien ne correspond aux filtres retenus.")
                .Italic().FontColor(Colors.Grey.Darken1);
            return;
        }

        container.Column(column =>
        {
            foreach (var group in model.Groups)
            {
                column.Item().PaddingTop(8).Element(c => ComposeGroup(c, group));
            }

            column.Item().PaddingTop(12).Element(ComposeGrandTotal);
        });
    }

    private void ComposeGroup(IContainer container, InventoryReportGroup group)
    {
        container.Column(column =>
        {
            column.Item().PaddingBottom(3).Text(group.CategoryName.ToUpperInvariant()).Bold().FontSize(9.5f);

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3);                              // Désignation
                    columns.ConstantColumn(PdfColumnWidths.Identifier);     // Code d'inventaire
                    columns.RelativeColumn(2);                              // Emplacement
                    columns.ConstantColumn(PdfColumnWidths.Initial + 12);   // Total
                    columns.ConstantColumn(PdfColumnWidths.Initial + 12);   // Disponible
                    columns.ConstantColumn(PdfColumnWidths.Initial + 12);   // Prêtés
                    columns.ConstantColumn(PdfColumnWidths.Amount);         // État
                    columns.ConstantColumn(PdfColumnWidths.Amount + 10);    // Valeur
                });

                table.Header(header =>
                {
                    HeaderCell(header.Cell(), "Désignation");
                    HeaderCell(header.Cell(), "Code");
                    HeaderCell(header.Cell(), "Emplacement");
                    HeaderCell(header.Cell(), "Total", right: true);
                    HeaderCell(header.Cell(), "Dispo.", right: true);
                    HeaderCell(header.Cell(), "Prêtés", right: true);
                    HeaderCell(header.Cell(), "État");
                    HeaderCell(header.Cell(), model.HasValuation ? "Valeur (FCFA)" : "", right: true);
                });

                foreach (var row in group.Rows)
                {
                    BodyCell(table.Cell(), row.Name);
                    BodyCell(table.Cell(), row.Code is null ? NoValue : NoBreakText.NoBreak(row.Code));
                    BodyCell(table.Cell(), row.Location);
                    BodyCell(table.Cell(), Number(row.QuantityTotal), right: true);
                    BodyCell(table.Cell(), Number(row.QuantityAvailable), right: true);
                    BodyCell(table.Cell(), Number(row.OnLoanQuantity), right: true);
                    BodyCell(table.Cell(), ConditionLabel(row.Condition));
                    BodyCell(
                        table.Cell(),
                        model.HasValuation ? row.LineValue.HasValue ? Amount(row.LineValue.Value) : NoValue : "",
                        right: true);
                }
            });

            column.Item().PaddingTop(2).AlignRight().Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(8).SemiBold());
                text.Span($"Sous-total {group.CategoryName} : ");
                text.Span($"{Number(group.TotalQuantity)} unité(s), dont {Number(group.TotalAvailable)} disponible(s)");

                if (model.HasValuation)
                {
                    text.Span($" — {Amount(group.TotalValue)} FCFA");
                }
            });
        });
    }

    private void ComposeGrandTotal(IContainer container)
    {
        container.BorderTop(1).BorderColor(Colors.Black).PaddingTop(5).Row(row =>
        {
            row.RelativeItem().Text("TOTAL GÉNÉRAL").Bold().FontSize(10);

            row.RelativeItem().AlignRight().Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(10).Bold());
                text.Span($"{Number(model.GrandTotalQuantity)} unité(s)  ·  ");
                text.Span($"{Number(model.GrandTotalAvailable)} disponible(s)");

                // La valorisation ne s'imprime que si au moins un prix a été saisi : sinon la ligne
                // annoncerait un patrimoine à 0 FCFA, ce qui est faux — il est simplement non chiffré.
                if (model.HasValuation)
                {
                    text.Span($"  ·  {Amount(model.GrandTotalValue)} FCFA");
                }
            });
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.BorderTop(0.5f).BorderColor(Colors.Grey.Medium).PaddingTop(3).Row(row =>
        {
            row.RelativeItem().Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(7).FontColor(Colors.Grey.Darken2));
                text.Span($"Édité le {model.GeneratedOn:dd/MM/yyyy}");

                if (model.HasValuation)
                {
                    text.Span("  ·  Valorisation INDICATIVE, sans portée comptable ni amortissement");
                }
            });

            row.RelativeItem().AlignRight().Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(7).FontColor(Colors.Grey.Darken2));
                text.Span("Page ");
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
            });
        });
    }

    private static void HeaderCell(IContainer container, string label, bool right = false)
    {
        var cell = container.Background(Colors.Grey.Lighten3).BorderBottom(0.5f).BorderColor(Colors.Grey.Medium)
            .PaddingVertical(3).PaddingHorizontal(3);

        (right ? cell.AlignRight() : cell).Text(label).Bold().FontSize(8);
    }

    private static void BodyCell(IContainer container, string value, bool right = false)
    {
        var cell = container.BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten1)
            .PaddingVertical(2.5f).PaddingHorizontal(3);

        (right ? cell.AlignRight() : cell).Text(value).FontSize(8);
    }

    /// <summary>
    /// « AReparer » est un nom de membre d'énumération, pas une étiquette lisible : cette fiche est
    /// destinée à un contrôleur, pas à un développeur.
    /// </summary>
    private static string ConditionLabel(string condition) => condition switch
    {
        "Neuf" => "Neuf",
        "Bon" => "Bon état",
        "AReparer" => "À réparer",
        "HorsService" => "Hors service",
        _ => condition
    };

    private static string Number(int value) => value.ToString("N0", FrenchCulture);

    /// <summary>FCFA : entiers, séparateur de milliers, sans décimales — la monnaie n'en a pas.</summary>
    private static string Amount(decimal value) => value.ToString("N0", FrenchCulture);
}
