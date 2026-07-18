using System.Globalization;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Bulletin de notes en PDF, format A5 portrait (ticket JGK-G03). Reproduit
/// docs/design-references/bulletin-reference.png (AGENTS.md règle #12) : mêmes colonnes du tableau de
/// notes, mêmes blocs de synthèse (moyenne générale, décision du conseil, récapitulatif annuel).
///
/// Certaines cases de la référence restent volontairement VIDES — visuellement présentes, jamais
/// remplies d'une donnée inventée — car rien dans le système ne les alimente encore : T.H, Absences,
/// Retards, mentions disciplinaires (Blâme/Avertissement/Tableau d'honneur/Encouragements/
/// Félicitations), Décision du Conseil, Appréciation par matière, lieu de naissance, Classe redoublée,
/// et la hiérarchie Inspection d'Académie/départementale de l'en-tête (voir ReportCardDto).
/// </summary>
public class ReportCardDocument(ReportCardDto reportCard, byte[]? logo) : IDocument
{
    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Bulletin de notes — {reportCard.StudentFullName}",
        Author = reportCard.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A5);
            page.Margin(1, Unit.Centimetre);
            page.DefaultTextStyle(text => text.FontSize(7.5f).FontColor(Colors.Black));

            page.Content().Column(column =>
            {
                column.Spacing(4);
                column.Item().Element(ComposeHeader);
                column.Item().Element(ComposeTitle);
                column.Item().Element(ComposeIdentity);
                column.Item().PaddingTop(2).Element(ComposeGradesTable);
                column.Item().Element(ComposeSynthesisRow);
                column.Item().Element(ComposeDisciplinaryMentionsRow);
                column.Item().PaddingTop(2).Element(ComposeDecisionAndRecap);
                column.Item().PaddingTop(6).Element(ComposeFooter);
            });
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.BorderBottom(1).BorderColor(Colors.Black).PaddingBottom(3).Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text(reportCard.SchoolName.ToUpperInvariant()).Bold().FontSize(10);

                if (logo is not null)
                {
                    left.Item().PaddingTop(2).MaxHeight(28).MaxWidth(90).Image(logo).FitArea();
                }
            });

            row.RelativeItem().AlignRight().Column(right =>
            {
                right.Item().AlignRight().Text($"Année scolaire : {reportCard.SchoolYearLabel}");
                right.Item().AlignRight().Text(reportCard.TermLabel).Bold();
            });
        });
    }

    private static void ComposeTitle(IContainer container)
    {
        container.BorderTop(1.5f).BorderBottom(1.5f).BorderColor(Colors.Black)
            .PaddingVertical(3).AlignCenter().Text("BULLETIN DE NOTES").Bold().FontSize(11);
    }

    private void ComposeIdentity(IContainer container)
    {
        container.PaddingTop(3).Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem(2).Text(t =>
                {
                    t.Span("Nom et prénoms : ");
                    t.Span(reportCard.StudentFullName).Bold();
                });
                row.RelativeItem(1).Text(t =>
                {
                    t.Span("Classe : ");
                    t.Span(reportCard.ClassroomName).Bold();
                });
            });

            column.Item().PaddingTop(1).Row(row =>
            {
                row.RelativeItem(2).Text($"Né(e) le : {FormatDate(reportCard.BirthDate)}");
                row.RelativeItem(1).Text($"Matricule : {reportCard.Matricule}");
            });

            column.Item().PaddingTop(1).Row(row =>
            {
                row.RelativeItem(2).Text($"Nombre d'élèves de la classe : {reportCard.ClassSize}");
                row.RelativeItem(1).Text("Classe redoublée : ☐");
            });
        });
    }

    private void ComposeGradesTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(2.9f); // Disciplines
                columns.RelativeColumn(0.9f); // Devoir
                columns.RelativeColumn(0.9f); // Composition
                columns.RelativeColumn(1.05f); // Moyenne
                columns.RelativeColumn(0.7f); // Coefficient
                columns.RelativeColumn(1.05f); // Moyenne x Coef
                columns.RelativeColumn(0.6f); // T.H
                columns.RelativeColumn(0.9f); // Rang
                columns.RelativeColumn(1.85f); // Appréciation
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("Disciplines").Bold().FontSize(7);
                header.Cell().Element(HeaderCell).AlignCenter().Text("Devoir").Bold().FontSize(7);
                header.Cell().Element(HeaderCell).AlignCenter().Text("Comp.").Bold().FontSize(7);
                header.Cell().Element(HeaderCell).AlignCenter().Text($"Moy./{reportCard.GradingScale}").Bold().FontSize(7);
                header.Cell().Element(HeaderCell).AlignCenter().Text("Coef").Bold().FontSize(7);
                header.Cell().Element(HeaderCell).AlignCenter().Text("Moy x").Bold().FontSize(7);
                header.Cell().Element(HeaderCell).AlignCenter().Text("T.H").Bold().FontSize(7);
                header.Cell().Element(HeaderCell).AlignCenter().Text("Rang").Bold().FontSize(7);
                header.Cell().Element(HeaderCell).AlignCenter().Text("Appréciations").Bold().FontSize(7);
            });

            foreach (var subject in reportCard.Subjects)
            {
                table.Cell().Element(BodyCell).Text(subject.SubjectName);
                table.Cell().Element(BodyCell).AlignCenter().Text(FormatOptionalGrade(subject.Devoir));
                table.Cell().Element(BodyCell).AlignCenter().Text(FormatOptionalGrade(subject.Composition));
                table.Cell().Element(BodyCell).AlignCenter().Text(FormatGrade(subject.Average));
                table.Cell().Element(BodyCell).AlignCenter().Text(FormatGrade(subject.Coefficient));
                table.Cell().Element(BodyCell).AlignCenter().Text(FormatGrade(subject.WeightedPoints));
                table.Cell().Element(BodyCell).AlignCenter().Text("—");
                table.Cell().Element(BodyCell).AlignCenter().Text(reportCard.SubjectRanks.GetValueOrDefault(subject.SubjectId, 0) is > 0 and var rank ? rank.ToString() : "—");
                table.Cell().Element(BodyCell).Text("");
            }

            table.Cell().Element(TotalCell).Text("TOTAL").Bold();
            table.Cell().Element(TotalCell).Text("");
            table.Cell().Element(TotalCell).Text("");
            table.Cell().Element(TotalCell).Text("");
            table.Cell().Element(TotalCell).AlignCenter().Text(FormatGrade(reportCard.TotalCoefficients)).Bold();
            table.Cell().Element(TotalCell).AlignCenter().Text(FormatGrade(reportCard.TotalPoints)).Bold();
            table.Cell().Element(TotalCell).Text("");
            table.Cell().Element(TotalCell).Text("");
            table.Cell().ColumnSpan(1).Element(TotalCell).Text("");
        });

        static IContainer HeaderCell(IContainer c) =>
            c.Border(0.75f).BorderColor(Colors.Grey.Darken1).Background(Colors.Grey.Lighten3).PaddingVertical(3).PaddingHorizontal(3);
        static IContainer BodyCell(IContainer c) =>
            c.Border(0.5f).BorderColor(Colors.Grey.Darken1).PaddingVertical(2).PaddingHorizontal(3);
        static IContainer TotalCell(IContainer c) =>
            c.Border(0.75f).BorderColor(Colors.Grey.Darken1).Background(Colors.Grey.Lighten4).PaddingVertical(2).PaddingHorizontal(3);
    }

    private void ComposeSynthesisRow(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(2);
                columns.RelativeColumn(1);
                columns.RelativeColumn(1);
                columns.RelativeColumn(1.2f);
            });

            SynthesisCell(table, $"Moyenne générale : {FormatGrade(reportCard.GeneralAverage)} /{reportCard.GradingScale}", bold: true);
            SynthesisCell(table, $"Rang : {reportCard.GeneralRank}");
            SynthesisCell(table, "Retards : —");
            SynthesisCell(table, "Absences totales : —");
        });

        static void SynthesisCell(TableDescriptor table, string text, bool bold = false)
        {
            var cell = table.Cell().Border(0.75f).BorderColor(Colors.Grey.Darken1).PaddingVertical(3).PaddingHorizontal(4).Text(text);
            if (bold) cell.Bold();
        }
    }

    private static void ComposeDisciplinaryMentionsRow(IContainer container)
    {
        // Cases visuellement présentes, jamais cochées : aucune décision disciplinaire n'est modélisée
        // (relève du conseil de classe, voir la remarque de classe).
        string[] mentions = ["Blâme", "Avertissement", "Tableau d'honneur", "Encouragements", "Félicitations"];

        container.PaddingTop(2).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                foreach (var _ in mentions) columns.RelativeColumn();
            });

            foreach (var mention in mentions)
            {
                table.Cell().Border(0.5f).BorderColor(Colors.Grey.Darken1).PaddingVertical(2).AlignCenter().Text(mention).FontSize(6.5f);
            }
        });
    }

    private void ComposeDecisionAndRecap(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Element(ComposeDecisionDuConseil);
            row.ConstantItem(6);
            row.RelativeItem().Element(ComposeAnnualRecap);
        });
    }

    private static void ComposeDecisionDuConseil(IContainer container)
    {
        string[] decisions = ["Admis(e) en classe supérieure", "Autorisé(e) à redoubler", "Exclusion"];

        container.Border(0.75f).BorderColor(Colors.Grey.Darken1).Column(column =>
        {
            column.Item().Background(Colors.Grey.Lighten3).PaddingVertical(2).AlignCenter().Text("Décision du Conseil").Bold();

            foreach (var decision in decisions)
            {
                column.Item().BorderTop(0.5f).BorderColor(Colors.Grey.Darken1).Row(row =>
                {
                    row.RelativeItem().PaddingVertical(2).PaddingHorizontal(3).Text(decision);
                    row.ConstantItem(16).PaddingVertical(2).AlignCenter().Text("☐");
                });
            }
        });
    }

    private void ComposeAnnualRecap(IContainer container)
    {
        container.Border(0.75f).BorderColor(Colors.Grey.Darken1).Column(column =>
        {
            foreach (var recap in reportCard.TermRecaps)
            {
                column.Item().BorderBottom(0.5f).BorderColor(Colors.Grey.Darken1).Row(row =>
                {
                    row.RelativeItem().PaddingVertical(2).PaddingHorizontal(3).Text($"Moy. {recap.TermLabel}");
                    row.ConstantItem(36).PaddingVertical(2).AlignRight().PaddingRight(3)
                        .Text(recap.Average is { } avg ? FormatGrade(avg) : "—");
                });
            }

            column.Item().BorderBottom(0.5f).BorderColor(Colors.Grey.Darken1).Row(row =>
            {
                row.RelativeItem().PaddingVertical(2).PaddingHorizontal(3).Text("Moyenne annuelle").Bold();
                row.ConstantItem(36).PaddingVertical(2).AlignRight().PaddingRight(3)
                    .Text(reportCard.AnnualAverage is { } annual ? FormatGrade(annual) : "—").Bold();
            });

            column.Item().Row(row =>
            {
                row.RelativeItem().PaddingVertical(2).PaddingHorizontal(3).Text("Rang annuel");
                row.ConstantItem(36).PaddingVertical(2).AlignRight().PaddingRight(3)
                    .Text(reportCard.AnnualRank is { } rank ? rank.ToString() : "—");
            });
        });
    }

    private static void ComposeFooter(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem(3).Column(left =>
            {
                left.Item().Text("Observations du conseil des professeurs").Bold();
                left.Item().PaddingTop(2).Border(0.5f).BorderColor(Colors.Grey.Darken1).MinHeight(40);
            });

            row.ConstantItem(6);

            row.RelativeItem(2).Column(right =>
            {
                right.Item().AlignCenter().Text("Le Chef d'établissement").Bold();
                right.Item().PaddingTop(14).AlignCenter().Text("[Emplacement Cachet Officiel]")
                    .FontSize(6.5f).FontColor(Colors.Grey.Medium);
            });
        });
    }

    private static string FormatDate(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>« - » quand la note n'a pas encore été saisie (Devoir ou Composition manquant) — jamais un zéro trompeur.</summary>
    internal static string FormatOptionalGrade(decimal? value) => value is { } v ? FormatGrade(v) : "-";

    /// <summary>
    /// Volume 1 §8.5 — suppression des décimales inutiles : 17,00 → 17 ; 15,50 → 15,5. Arrondi à 2
    /// décimales avant affichage (une moyenne pondérée peut porter bien plus de décimales brutes).
    /// Séparateur décimal virgule, comme la référence visuelle ("11,13", "9,5625"). <c>internal</c> pour
    /// être exercée directement par ReportCardDocumentTests (voir InternalsVisibleTo du csproj).
    /// </summary>
    internal static string FormatGrade(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero)
            .ToString("0.##", CultureInfo.InvariantCulture)
            .Replace('.', ',');
}
