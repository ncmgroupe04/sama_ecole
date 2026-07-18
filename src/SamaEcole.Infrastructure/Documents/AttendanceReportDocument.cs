using System.Globalization;
using SamaEcole.Application.Reports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Rapport d'assiduité en PDF, A4 paysage (ticket JGK-R03) : un tableau listant chaque élève de la
/// période avec ses décomptes et son taux de présence. Document de travail interne (pas une pièce
/// officielle) — pas de logo ni de cachet, contrairement au reçu ou au bulletin.
/// </summary>
public class AttendanceReportDocument(AttendanceReportExportModel model) : IDocument
{
    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Rapport d'assiduité — {model.ClassName ?? "Toutes les classes"}",
        Author = model.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(1.2f, Unit.Centimetre);
            page.DefaultTextStyle(text => text.FontSize(8).FontColor(Colors.Black));

            page.Header().Element(ComposeHeader);
            page.Content().PaddingTop(6).Element(ComposeTable);
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
                row.RelativeItem().AlignRight().Text("RAPPORT D'ASSIDUITÉ").Bold().FontSize(12);
            });

            column.Item().PaddingTop(3).Row(row =>
            {
                row.RelativeItem().Text(t =>
                {
                    t.Span("Période : ").SemiBold();
                    t.Span($"{Format(model.StartDate)} au {Format(model.EndDate)}");
                });
                row.RelativeItem().Text(t =>
                {
                    t.Span("Classe : ").SemiBold();
                    t.Span(model.ClassName ?? "Toutes les classes");
                });
                row.RelativeItem().AlignRight().Text(t =>
                {
                    t.Span("Taux moyen : ").SemiBold();
                    t.Span(model.AverageAttendanceRate is { } avg ? FormatPercent(avg) : "—").Bold();
                });
            });
        });
    }

    private void ComposeTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(1.3f);  // Matricule
                columns.RelativeColumn(2.4f);  // Nom
                columns.RelativeColumn(1.6f);  // Classe
                columns.RelativeColumn(0.8f);  // Appels
                columns.RelativeColumn(0.8f);  // Présents
                columns.RelativeColumn(0.8f);  // Retards
                columns.RelativeColumn(1.0f);  // Min. retard
                columns.RelativeColumn(0.9f);  // Abs. justifiées
                columns.RelativeColumn(1.0f);  // Abs. non justifiées
                columns.RelativeColumn(1.0f);  // Taux
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("Matricule").Bold().FontSize(7.5f);
                header.Cell().Element(HeaderCell).Text("Nom").Bold().FontSize(7.5f);
                header.Cell().Element(HeaderCell).Text("Classe").Bold().FontSize(7.5f);
                header.Cell().Element(HeaderCell).AlignCenter().Text("Appels").Bold().FontSize(7.5f);
                header.Cell().Element(HeaderCell).AlignCenter().Text("Prés.").Bold().FontSize(7.5f);
                header.Cell().Element(HeaderCell).AlignCenter().Text("Ret.").Bold().FontSize(7.5f);
                header.Cell().Element(HeaderCell).AlignCenter().Text("Min. ret.").Bold().FontSize(7.5f);
                header.Cell().Element(HeaderCell).AlignCenter().Text("Abs. J").Bold().FontSize(7.5f);
                header.Cell().Element(HeaderCell).AlignCenter().Text("Abs. NJ").Bold().FontSize(7.5f);
                header.Cell().Element(HeaderCell).AlignCenter().Text("Taux").Bold().FontSize(7.5f);
            });

            if (model.Students.Count == 0)
            {
                table.Cell().ColumnSpan(10).Element(BodyCell).AlignCenter().PaddingVertical(8)
                    .Text("Aucun appel sur cette période.").FontColor(Colors.Grey.Darken1);
                return;
            }

            foreach (var s in model.Students)
            {
                table.Cell().Element(BodyCell).Text(s.Matricule);
                table.Cell().Element(BodyCell).Text(s.FullName);
                table.Cell().Element(BodyCell).Text(s.ClassroomName);
                table.Cell().Element(BodyCell).AlignCenter().Text(s.TotalCalls.ToString(CultureInfo.InvariantCulture));
                table.Cell().Element(BodyCell).AlignCenter().Text(s.Present.ToString(CultureInfo.InvariantCulture));
                table.Cell().Element(BodyCell).AlignCenter().Text(s.Late.ToString(CultureInfo.InvariantCulture));
                table.Cell().Element(BodyCell).AlignCenter().Text(s.TotalLateMinutes.ToString(CultureInfo.InvariantCulture));
                table.Cell().Element(BodyCell).AlignCenter().Text(s.JustifiedAbsences.ToString(CultureInfo.InvariantCulture));
                table.Cell().Element(BodyCell).AlignCenter().Text(s.UnjustifiedAbsences.ToString(CultureInfo.InvariantCulture));
                table.Cell().Element(BodyCell).AlignCenter().Text(FormatPercent(s.AttendanceRate));
            }

            static IContainer HeaderCell(IContainer c) =>
                c.Border(0.75f).BorderColor(Colors.Grey.Darken1).Background(Colors.Grey.Lighten3).PaddingVertical(3).PaddingHorizontal(3);
            static IContainer BodyCell(IContainer c) =>
                c.Border(0.5f).BorderColor(Colors.Grey.Darken1).PaddingVertical(2).PaddingHorizontal(3);
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.AlignRight().Text(t =>
        {
            t.Span($"Généré le {DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)} — page ")
                .FontSize(7).FontColor(Colors.Grey.Darken1);
            t.CurrentPageNumber().FontSize(7).FontColor(Colors.Grey.Darken1);
            t.Span("/").FontSize(7).FontColor(Colors.Grey.Darken1);
            t.TotalPages().FontSize(7).FontColor(Colors.Grey.Darken1);
        });
    }

    private static string Format(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string FormatPercent(decimal rate) =>
        Math.Round(rate * 100m, 1, MidpointRounding.AwayFromZero)
            .ToString("0.#", CultureInfo.InvariantCulture)
            .Replace('.', ',') + " %";
}
