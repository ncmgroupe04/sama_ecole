using System.Globalization;
using SamaEcole.Application.Internat;
using SamaEcole.Application.Internat.Queries.GetStudentHizbReport;
using SamaEcole.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Bulletin coranique d'un élève (A4 portrait) : identité, Oustaz, synthèse de la mémorisation et grille des 60 Hizb
/// regroupés par Juz — avec, pour chaque Hizb, les quarts acquis, la note de l'Oustaz et la date de dernière évaluation.
///
/// Mise en page BILINGUE bloc par bloc (<see cref="Bilingual"/>) : le titre, le nom de l'élève et celui de l'Oustaz
/// s'impriment en arabe sous leur version française quand ils existent, jamais mélangés dans un même <c>Text()</c>.
/// Une valeur non renseignée n'imprime rien plutôt qu'un espace réservé trompeur. Même charte que les autres
/// documents (Times New Roman → Liberation Serif, bandeau d'établissement, pied de page numéroté).
///
/// Aucun texte d'appréciation n'est généré : le suivi ne porte qu'une note par Hizb. Le bulletin l'affiche telle quelle,
/// sa moyenne, et réserve un cadre pour l'appréciation manuscrite de l'Oustaz et la signature de la Direction.
/// </summary>
public class HizbReportDocument(HizbReportDto report, byte[]? logo) : IDocument
{
    private static readonly CultureInfo French = new("fr-FR");

    private const string QuarterFilled = "#15803D";   // vert : quart acquis
    private const string QuarterEmpty = "#FFFFFF";
    private const string CompletedFill = "#DCFCE7";
    private const string InProgressFill = "#FEF3C7";

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Bulletin coranique — {report.FullName} ({report.Matricule})",
        Author = report.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1.4f, Unit.Centimetre);
            page.DefaultTextStyle(text => text.FontFamily("Times New Roman").FontSize(9).FontColor(Colors.Black));

            page.Content().Column(column =>
            {
                column.Item().Element(ComposeHeader);
                column.Item().PaddingTop(10).Element(ComposeIdentity);
                column.Item().PaddingTop(10).Element(ComposeSummary);
                column.Item().PaddingTop(10).Element(ComposeGrid);
                // ShowEntire : le bloc d'appréciation et de signatures ne se coupe jamais en deux entre deux pages.
                column.Item().PaddingTop(14).ShowEntire().Element(ComposeAppreciationAndSignatures);
            });

            page.Footer().Row(row =>
            {
                row.RelativeItem().Text($"Établi le {report.GeneratedAt.ToString("dd/MM/yyyy", French)} — {report.SchoolName}")
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

    private void ComposeHeader(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                if (logo is not null)
                {
                    row.ConstantItem(54).Height(54).Image(logo).FitArea();
                    row.ConstantItem(10);
                }

                row.RelativeItem().AlignMiddle().Column(info =>
                {
                    info.Item().Text(report.SchoolName).Bold().FontSize(13);
                    if (!string.IsNullOrWhiteSpace(report.SchoolAddress))
                    {
                        info.Item().Text(report.SchoolAddress).FontSize(8.5f).FontColor(Colors.Grey.Darken2);
                    }
                });
            });

            column.Item().PaddingTop(10).LineHorizontal(1.2f).LineColor(QuarterFilled);
            column.Item().PaddingTop(8).AlignCenter().Text("BULLETIN CORANIQUE").Bold().FontSize(16);
            column.Item().AlignCenter().Element(c => Bilingual.ArabicBlock(c, "كشف حفظ القرآن الكريم", 14, bold: true));
            if (!string.IsNullOrWhiteSpace(report.SchoolYearLabel))
            {
                column.Item().PaddingTop(2).AlignCenter()
                    .Text($"Année scolaire {report.SchoolYearLabel}").FontSize(9).FontColor(Colors.Grey.Darken2);
            }
        });
    }

    private void ComposeIdentity(IContainer container)
    {
        container.Border(0.6f).BorderColor(Colors.Grey.Lighten1).Padding(8).Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text("ÉLÈVE").FontSize(7.5f).Bold().FontColor(Colors.Grey.Darken1);
                left.Item().Text(report.FullName).Bold().FontSize(12);
                left.Item().Element(c => Bilingual.ArabicBlock(c, report.FullNameAr, 12, bold: true));
                left.Item().PaddingTop(3).Text(text =>
                {
                    text.Span("Matricule : ").FontColor(Colors.Grey.Darken1);
                    text.Span(NoBreakText.NoBreak(report.Matricule)).Bold();
                });
                left.Item().Text(text =>
                {
                    text.Span("Né(e) le ").FontColor(Colors.Grey.Darken1);
                    text.Span($"{report.BirthDate.ToString("dd/MM/yyyy", French)} à {report.BirthPlace}");
                });
                if (!string.IsNullOrWhiteSpace(report.ClassroomName))
                {
                    left.Item().Text(text =>
                    {
                        text.Span("Classe : ").FontColor(Colors.Grey.Darken1);
                        text.Span(report.ClassroomName);
                    });
                }
            });

            row.ConstantItem(12);

            row.RelativeItem().Column(right =>
            {
                right.Item().Text("OUSTAZ — HALQA").FontSize(7.5f).Bold().FontColor(Colors.Grey.Darken1);
                if (string.IsNullOrWhiteSpace(report.InstructorName))
                {
                    right.Item().Text("Aucun Oustaz rattaché").FontColor(Colors.Grey.Darken1);
                }
                else
                {
                    right.Item().Text(report.InstructorName).Bold().FontSize(12);
                    right.Item().Element(c => Bilingual.ArabicBlock(c, report.InstructorNameAr, 12, bold: true));
                }

                right.Item().PaddingTop(3).Text(text =>
                {
                    text.Span("Dernière évaluation : ").FontColor(Colors.Grey.Darken1);
                    text.Span(report.LastEvaluatedAt is { } last
                        ? last.ToString("dd/MM/yyyy", French)
                        : "aucune").Bold();
                });
            });
        });
    }

    private void ComposeSummary(IContainer container)
    {
        var s = report.Summary;
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                Stat(row, s.CompletedHizbs.ToString(French), "Hizb complets");
                row.ConstantItem(6);
                Stat(row, s.InProgressHizbs.ToString(French), "Hizb en cours");
                row.ConstantItem(6);
                Stat(row, $"{s.CompletedQuarters}/{s.TotalQuarters}", "Quarts acquis");
                row.ConstantItem(6);
                Stat(row, $"{s.ProgressPercent.ToString("0.#", French)} %", "Du Coran mémorisé");
                row.ConstantItem(6);
                Stat(row, report.AverageRating is { } avg ? $"{avg.ToString("0.0", French)} / 5" : "—", "Note moyenne");
            });

            // Barre d'avancement : deux segments proportionnels. Un segment de poids nul est omis (QuestPDF refuse un poids ≤ 0).
            var percent = (float)Math.Clamp(s.ProgressPercent, 0m, 100m);
            column.Item().PaddingTop(6).Height(7).Row(bar =>
            {
                if (percent > 0)
                {
                    bar.RelativeItem(percent).Background(QuarterFilled);
                }

                if (percent < 100)
                {
                    bar.RelativeItem(100 - percent).Background(Colors.Grey.Lighten3);
                }
            });
        });
    }

    private static void Stat(RowDescriptor row, string value, string label)
    {
        row.RelativeItem().Border(0.6f).BorderColor(Colors.Grey.Lighten1).Padding(5).Column(column =>
        {
            column.Item().AlignCenter().Text(value).Bold().FontSize(13);
            column.Item().AlignCenter().Text(label).FontSize(7.5f).FontColor(Colors.Grey.Darken1);
        });
    }

    private void ComposeGrid(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Text("Détail par Juz et par Hizb").Bold().FontSize(10);
            // La légende précède la grille : placée dessous, elle risquait de se couper à la fin de la page.
            column.Item().PaddingTop(2).PaddingBottom(4).Element(ComposeLegend);

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(30);
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("Juz").Bold();
                    header.Cell().Element(HeaderCell).Text("Premier Hizb").Bold();
                    header.Cell().Element(HeaderCell).Text("Second Hizb").Bold();
                });

                foreach (var group in report.Hizbs.GroupBy(c => (c.HizbNumber + 1) / 2).OrderBy(g => g.Key))
                {
                    table.Cell().Element(BodyCell).AlignCenter().AlignMiddle().Text(group.Key.ToString(French)).Bold();
                    foreach (var cell in group.OrderBy(c => c.HizbNumber))
                    {
                        table.Cell().Element(c => BodyCell(c, Background(cell.State))).Element(c => ComposeHizb(c, cell));
                    }
                }
            });
        });
    }

    private static string Background(HizbMemorizationState state) => state switch
    {
        HizbMemorizationState.Completed => CompletedFill,
        HizbMemorizationState.InProgress => InProgressFill,
        _ => QuarterEmpty
    };

    private static IContainer HeaderCell(IContainer container) =>
        container.Background(Colors.Grey.Lighten3).Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(3);

    private static IContainer BodyCell(IContainer container) => BodyCell(container, QuarterEmpty);

    private static IContainer BodyCell(IContainer container, string background) =>
        container.Background(background).Border(0.5f).BorderColor(Colors.Grey.Lighten1).PaddingVertical(2).PaddingHorizontal(4);

    /// <summary>« Hizb n », quatre carrés (un par quart acquis), la note et la date de dernière évaluation.</summary>
    private static void ComposeHizb(IContainer container, HizbCellDto cell)
    {
        container.Row(row =>
        {
            row.ConstantItem(34).AlignMiddle().Text($"Hizb {cell.HizbNumber.ToString(French)}").Bold();

            for (var quarter = 1; quarter <= HizbRules.QuartersPerHizb; quarter++)
            {
                row.ConstantItem(11).AlignMiddle().Height(8).Width(8)
                    .Border(0.6f).BorderColor(Colors.Grey.Darken1)
                    .Background(cell.CompletedQuarters >= quarter ? QuarterFilled : QuarterEmpty);
            }

            row.ConstantItem(6);
            row.ConstantItem(22).AlignMiddle().Text(cell.Rating is { } rating ? $"{rating}/5" : "—").FontSize(8);
            row.RelativeItem().AlignMiddle().AlignRight()
                .Text(cell.LastEvaluatedAt is { } at ? at.ToString("dd/MM/yy", French) : string.Empty)
                .FontSize(7.5f).FontColor(Colors.Grey.Darken1);
        });
    }

    private void ComposeLegend(IContainer container)
    {
        container.Text(text =>
        {
            text.DefaultTextStyle(style => style.FontSize(7.5f).FontColor(Colors.Grey.Darken1));
            text.Span("Chaque Hizb compte quatre quarts (carré plein = quart acquis). Fond vert : complet ; jaune : en cours ; ");
            text.Span("blanc : non commencé. Note de 1 à 5 : celle de l'Oustaz à sa dernière évaluation.");
        });
    }

    private void ComposeAppreciationAndSignatures(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Text("Appréciation de l'Oustaz").Bold().FontSize(10);
            for (var i = 0; i < 3; i++)
            {
                column.Item().PaddingTop(14).LineHorizontal(0.5f).LineColor(Colors.Grey.Medium);
            }

            column.Item().PaddingTop(18).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("L'Oustaz").Bold();
                    c.Item().Height(36);
                    c.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Medium);
                });
                row.ConstantItem(40);
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("La Direction").Bold();
                    c.Item().Height(36);
                    c.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Medium);
                });
            });
        });
    }
}
