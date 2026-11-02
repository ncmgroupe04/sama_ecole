using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SamaEcole.Application.Boarding.Documents.MonthlyAttendanceSummary;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// « Récapitulatif mensuel du pointage » (A4 portrait) : pour une session et un groupe, par élève, les jours présents, absents, en
/// permission et non pointés du mois. Utile à la pension (nuits et repas effectifs). Les jours pris en compte sont ceux où la session
/// a été pointée pour le groupe ; ils sont listés sous le titre pour que le total se vérifie.
///
/// CONFIDENTIALITÉ : nom, matricule et comptes seulement — aucune donnée de santé ni téléphone (le DTO n'en porte pas).
/// </summary>
public class MonthlyAttendanceSummaryDocument(MonthlyAttendanceSummaryReportDto report, byte[]? logo) : IDocument
{
    private static readonly CultureInfo French = new("fr-FR");

    private const string Accent = "#1E3A5F";

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Récapitulatif mensuel du pointage — {report.Summary.SessionName} — {MonthLabel()}",
        Author = report.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1.4f, Unit.Centimetre);
            page.DefaultTextStyle(text => text.FontFamily("Times New Roman").FontSize(9.5f).FontColor(Colors.Black).DisableFontFeature(FontFeatures.StandardLigatures));

            page.Header().Element(ComposeHeader);
            page.Content().PaddingTop(8).Element(ComposeTable);

            page.Footer().Row(row =>
            {
                row.RelativeItem().Text(
                        $"Édité le {report.GeneratedAt.ToString("dd/MM/yyyy", French)} — {report.SchoolName}")
                    .FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                row.ConstantItem(80).AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(7.5f).FontColor(Colors.Grey.Darken1));
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        });
    }

    private string MonthLabel()
    {
        var label = report.Summary.MonthStart.ToString("MMMM yyyy", French);
        return char.ToUpper(label[0], French) + label[1..];
    }

    private string GroupKind() => report.Summary.Audience switch
    {
        AttendanceAudience.Interns => "Pavillon",
        AttendanceAudience.Halqa => "Halqa de l'Oustaz",
        _ => "Classe"
    };

    private void ComposeHeader(IContainer container) =>
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                if (logo is not null)
                {
                    row.ConstantItem(46).Height(46).Image(logo).FitArea();
                    row.ConstantItem(10);
                }

                row.RelativeItem().AlignMiddle().Column(info =>
                {
                    info.Item().Text(report.SchoolName).Bold().FontSize(12);
                    if (!string.IsNullOrWhiteSpace(report.SchoolAddress))
                    {
                        info.Item().Text(report.SchoolAddress).FontSize(8.5f).FontColor(Colors.Grey.Darken2);
                    }
                });

                row.RelativeItem().AlignRight().AlignMiddle().Text(
                        report.SchoolYearLabel is { Length: > 0 } year ? $"Année scolaire {year}" : string.Empty)
                    .FontSize(9).FontColor(Colors.Grey.Darken2);
            });

            column.Item().PaddingTop(6).LineHorizontal(1.2f).LineColor(Accent);
            column.Item().PaddingTop(5).AlignCenter().Text("RÉCAPITULATIF MENSUEL DU POINTAGE").Bold().FontSize(14);
            column.Item().AlignCenter().Text($"{report.Summary.SessionName} — {MonthLabel()}").Bold().FontSize(11);
            column.Item().AlignCenter().Text($"{GroupKind()} : {report.Summary.GroupName}").FontSize(10);
        });

    private void ComposeTable(IContainer container)
    {
        var summary = report.Summary;

        container.Column(column =>
        {
            column.Item().Text(text =>
            {
                text.Span($"Jours pointés : {summary.Days.Count}").Bold();
                if (summary.Days.Count > 0)
                {
                    text.Span($"  ({string.Join(", ", summary.Days.Select(d => d.Day.ToString(French)))})").FontColor(Colors.Grey.Darken2);
                }
            });

            if (summary.Rows.Count == 0)
            {
                column.Item().PaddingTop(20).AlignCenter().Text("Aucun élève dans ce groupe.").Italic().FontSize(11);
                return;
            }

            if (summary.Days.Count == 0)
            {
                column.Item().PaddingTop(2).Text("Aucun pointage ce mois-ci pour cette session : les comptes restent à zéro.")
                    .Italic().FontSize(9).FontColor(Colors.Grey.Darken2);
            }

            var showDetail = summary.Rows.Any(r => r.Detail is not null);

            column.Item().PaddingTop(6).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(3.2f);   // Nom
                    c.RelativeColumn(2.4f);   // Matricule
                    if (showDetail)
                    {
                        c.RelativeColumn(1.8f);   // Régime / chambre
                    }

                    c.RelativeColumn(1.1f);   // Présents
                    c.RelativeColumn(1.1f);   // Absents
                    c.RelativeColumn(1.3f);   // En permission
                    c.RelativeColumn(1.3f);   // Non pointés
                });

                // L'en-tête se répète sur chaque page : le tableau se lit page par page.
                table.Header(header =>
                {
                    HeaderCell(header.Cell(), "Nom");
                    HeaderCell(header.Cell(), "Matricule");
                    if (showDetail)
                    {
                        HeaderCell(header.Cell(), "Détail");
                    }

                    HeaderCell(header.Cell(), "Présents", center: true);
                    HeaderCell(header.Cell(), "Absents", center: true);
                    HeaderCell(header.Cell(), "En perm.", center: true);
                    HeaderCell(header.Cell(), "Non pointés", center: true);
                });

                foreach (var row in summary.Rows)
                {
                    BodyCell(table.Cell()).Column(name =>
                    {
                        name.Item().Text(row.StudentName).Bold();
                        name.Item().Element(c => Bilingual.ArabicBlock(c, row.StudentNameAr, 10));
                    });
                    BodyCell(table.Cell()).Text(row.Matricule);
                    if (showDetail)
                    {
                        BodyCell(table.Cell()).Text(row.Detail ?? string.Empty);
                    }

                    CountCell(table.Cell(), row.Present);
                    CountCell(table.Cell(), row.Absent);
                    CountCell(table.Cell(), row.OnLeave);
                    CountCell(table.Cell(), row.NotRecorded);
                }

                // Total du groupe : repères de pension (nuits ou repas effectivement pris).
                var labelSpan = showDetail ? 3 : 2;
                table.Cell().ColumnSpan((uint)labelSpan).Background(Colors.Grey.Lighten3).Border(0.4f).BorderColor(Colors.Grey.Lighten1)
                    .Padding(3).Text("Total du groupe").Bold();
                TotalCell(table.Cell(), summary.Rows.Sum(r => r.Present));
                TotalCell(table.Cell(), summary.Rows.Sum(r => r.Absent));
                TotalCell(table.Cell(), summary.Rows.Sum(r => r.OnLeave));
                TotalCell(table.Cell(), summary.Rows.Sum(r => r.NotRecorded));
            });
        });
    }

    private static void HeaderCell(IContainer cell, string label, bool center = false)
    {
        var styled = cell.Background(Colors.Grey.Lighten3).Border(0.4f).BorderColor(Colors.Grey.Lighten1).Padding(3);
        (center ? styled.AlignCenter() : styled).Text(label).Bold().FontSize(9);
    }

    private static IContainer BodyCell(IContainer cell) =>
        cell.Border(0.4f).BorderColor(Colors.Grey.Lighten1).Padding(3).MinHeight(22).AlignMiddle();

    private static void CountCell(IContainer cell, int count) =>
        BodyCell(cell).AlignCenter().Text(count.ToString(French));

    private static void TotalCell(IContainer cell, int count) =>
        cell.Background(Colors.Grey.Lighten3).Border(0.4f).BorderColor(Colors.Grey.Lighten1).Padding(3)
            .AlignCenter().Text(count.ToString(French)).Bold();
}
