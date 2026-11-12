using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SamaEcole.Application.Finance.Queries.GetMonthlyStatus;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Situation des mensualités (GET /finance/monthly-status/pdf) : une section par catégorie (payé,
/// partiellement payé, non payé), ou UNE seule quand un statut est demandé. A4 paysage (assez large pour les colonnes de montants sans retour à la ligne), style épuré des
/// documents de travail internes (comme <see cref="InventoryReportDocument"/>).
/// </summary>
public class MonthlyStatusDocument(MonthlyStatusDto report, string schoolName, string? classroomName, string? status) : IDocument
{
    private static readonly CultureInfo FrenchCulture = CultureInfo.GetCultureInfo("fr-FR");

    private static readonly string[] MonthNames =
    [
        "Janvier", "Février", "Mars", "Avril", "Mai", "Juin",
        "Juillet", "Août", "Septembre", "Octobre", "Novembre", "Décembre"
    ];

    private string MonthName => MonthNames[Math.Clamp(report.Month, 1, 12) - 1];

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Situation des mensualités — {MonthName} {report.SchoolYearLabel}",
        Author = schoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(1.5f, Unit.Centimetre);
            page.DefaultTextStyle(text => text.FontSize(8.5f).FontColor(Colors.Black).DisableFontFeature(FontFeatures.StandardLigatures));

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
                row.RelativeItem().Text(schoolName.ToUpperInvariant()).Bold().FontSize(12);
                row.RelativeItem().AlignRight().Text("SITUATION DES MENSUALITÉS").Bold().FontSize(12);
            });

            column.Item().PaddingTop(3).Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.Span("Mois : ").SemiBold();
                    text.Span($"{MonthName} — année {report.SchoolYearLabel}");
                });
                row.RelativeItem().Text(text =>
                {
                    text.Span("Classe : ").SemiBold();
                    text.Span(classroomName ?? "Toutes les classes");
                });
                row.RelativeItem().AlignRight().Text(text =>
                {
                    text.Span("Affichage : ").SemiBold();
                    text.Span(status switch
                    {
                        "paid" => "Ont payé",
                        "partial" => "Ont partiellement payé",
                        "unpaid" => "N'ont pas payé",
                        _ => "Toutes les catégories"
                    });
                });
            });
        });
    }

    private void ComposeBody(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Element(ComposeSummary);

            if (status is null or "paid")
            {
                column.Item().PaddingTop(10).Element(c => ComposeSection(
                    c, "ONT PAYÉ", report.Paid,
                    [
                        new("Attendu", 80, true, r => Amount(r.ExpectedAmount)),
                        new("Payé", 80, true, r => Amount(r.PaidAmount)),
                        new("Dernier paiement", 80, false, r => FormatDate(r.LastPaymentAt))
                    ],
                    totals: ["Attendu", "Payé"]));
            }

            if (status is null or "partial")
            {
                column.Item().PaddingTop(10).Element(c => ComposeSection(
                    c, "ONT PARTIELLEMENT PAYÉ", report.Partial,
                    [
                        new("Attendu", 80, true, r => Amount(r.ExpectedAmount)),
                        new("Payé", 80, true, r => Amount(r.PaidAmount)),
                        new("Reste", 80, true, r => Amount(r.RemainingAmount)),
                        new("% payé", 50, true, r => Percent(r.PaidAmount, r.ExpectedAmount)),
                        new("Dernier paiement", 80, false, r => FormatDate(r.LastPaymentAt))
                    ],
                    totals: ["Attendu", "Payé", "Reste"]));
            }

            if (status is null or "unpaid")
            {
                column.Item().PaddingTop(10).Element(c => ComposeSection(
                    c, "N'ONT PAS PAYÉ", report.Unpaid,
                    [
                        new("Attendu", 90, true, r => Amount(r.ExpectedAmount)),
                        new("Restant dû", 90, true, r => Amount(r.RemainingAmount))
                    ],
                    totals: ["Attendu", "Restant dû"]));
            }
        });
    }

    /// <summary>Bandeau de synthèse : effectifs et montants du mois (toutes catégories, quel que soit le filtre).</summary>
    private void ComposeSummary(IContainer container)
    {
        var s = report.Summary;
        var collectedRate = Percent(s.TotalCollected, s.TotalExpected);

        container.Border(0.5f).BorderColor(Colors.Grey.Medium).Background(Colors.Grey.Lighten4).Padding(5).Column(column =>
        {
            column.Item().Row(row =>
            {
                SummaryCell(row.RelativeItem(), "Élèves concernés", s.TotalStudents.ToString(FrenchCulture));
                SummaryCell(row.RelativeItem(), "Ont payé", s.PaidCount.ToString(FrenchCulture));
                SummaryCell(row.RelativeItem(), "Partiellement", s.PartialCount.ToString(FrenchCulture));
                SummaryCell(row.RelativeItem(), "N'ont pas payé", s.UnpaidCount.ToString(FrenchCulture));
            });
            column.Item().PaddingTop(4).Row(row =>
            {
                SummaryCell(row.RelativeItem(), "Attendu (FCFA)", Amount(s.TotalExpected));
                SummaryCell(row.RelativeItem(), "Encaissé (FCFA)", Amount(s.TotalCollected));
                SummaryCell(row.RelativeItem(), "Reste à recouvrer (FCFA)", Amount(s.TotalRemaining));
                SummaryCell(row.RelativeItem(), "Taux de recouvrement", collectedRate);
            });
        });
    }

    private static void SummaryCell(IContainer container, string label, string value)
    {
        container.Column(c =>
        {
            c.Item().Text(label).FontSize(7).FontColor(Colors.Grey.Darken2);
            c.Item().Text(value).Bold().FontSize(9.5f);
        });
    }

    /// <summary>Colonne de montant/date : libellé, largeur fixe, alignement et valeur extraite de la ligne.</summary>
    private sealed record Col(string Header, float Width, bool Right, Func<MonthlyStatusRow, string> Value);

    private static void ComposeSection(
        IContainer container, string title, IReadOnlyList<MonthlyStatusRow> rows,
        Col[] columnsSpec, string[] totals)
    {
        container.Column(column =>
        {
            column.Item().PaddingBottom(3).Text($"{title} ({rows.Count})").Bold().FontSize(9.5f);

            if (rows.Count == 0)
            {
                column.Item().Text("Aucun élève.").Italic().FontColor(Colors.Grey.Darken1);
                return;
            }

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(18);                            // N°
                    columns.ConstantColumn(PdfColumnWidths.Identifier);   // Matricule
                    columns.RelativeColumn(3);                            // Élève
                    columns.RelativeColumn(1.4f);                         // Classe
                    foreach (var col in columnsSpec) columns.ConstantColumn(col.Width);
                });

                table.Header(header =>
                {
                    HeaderCell(header.Cell(), "N°");
                    HeaderCell(header.Cell(), "Matricule");
                    HeaderCell(header.Cell(), "Élève");
                    HeaderCell(header.Cell(), "Classe");
                    foreach (var col in columnsSpec) HeaderCell(header.Cell(), col.Header, col.Right);
                });

                var index = 0;
                foreach (var row in rows)
                {
                    index++;
                    BodyCell(table.Cell(), index.ToString(FrenchCulture));
                    BodyCell(table.Cell(), NoBreakText.NoBreak(row.Matricule));
                    BodyCell(table.Cell(), row.StudentFullName);
                    BodyCell(table.Cell(), row.ClassroomName);
                    foreach (var col in columnsSpec) BodyCell(table.Cell(), col.Value(row), col.Right);
                }

                // Ligne de totaux : somme des colonnes de montant demandées, vide ailleurs.
                TotalCell(table.Cell().ColumnSpan(4), $"Total ({rows.Count} élève{(rows.Count > 1 ? "s" : "")})", false);
                foreach (var col in columnsSpec)
                {
                    TotalCell(table.Cell(), totals.Contains(col.Header) ? Amount(Sum(rows, col.Header)) : "", col.Right);
                }
            });
        });
    }

    private static decimal Sum(IReadOnlyList<MonthlyStatusRow> rows, string header) => header switch
    {
        "Attendu" => rows.Sum(r => r.ExpectedAmount),
        "Payé" => rows.Sum(r => r.PaidAmount),
        _ => rows.Sum(r => r.RemainingAmount) // « Reste » / « Restant dû »
    };

    private static void TotalCell(IContainer container, string value, bool right)
    {
        var cell = container.BorderTop(0.75f).BorderColor(Colors.Black).Background(Colors.Grey.Lighten4)
            .PaddingVertical(3).PaddingHorizontal(3);

        (right ? cell.AlignRight() : cell).Text(value).Bold().FontSize(8);
    }

    private static string Percent(decimal part, decimal whole) =>
        whole <= 0 ? "—" : $"{Math.Round(part / whole * 100m, 0, MidpointRounding.AwayFromZero).ToString("N0", FrenchCulture)} %";

    private void ComposeFooter(IContainer container)
    {
        container.BorderTop(0.5f).BorderColor(Colors.Grey.Medium).PaddingTop(3).Row(row =>
        {
            row.RelativeItem().Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(7).FontColor(Colors.Grey.Darken2));
                text.Span($"Édité le {DateTime.UtcNow:dd/MM/yyyy}  ·  Attendu {Amount(report.Summary.TotalExpected)} FCFA, ");
                text.Span($"encaissé {Amount(report.Summary.TotalCollected)} FCFA, reste {Amount(report.Summary.TotalRemaining)} FCFA (toutes catégories)");
            });

            row.ConstantItem(80).AlignRight().Text(text =>
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

    private static string FormatDate(DateTimeOffset? value) => value is { } v ? v.ToString("dd/MM/yyyy", FrenchCulture) : "—";

    private static string Amount(decimal value) => value.ToString("N0", FrenchCulture);
}
