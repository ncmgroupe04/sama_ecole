using SamaEcole.Application.ReportCards;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using SamaEcole.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Procès-verbal du conseil de classe (délibération) — PV d'une PÉRIODE (semestre/trimestre) ou PV ANNUEL
/// (Évolution N°7). A4 portrait, présentation attendue par les Inspections d'Académie :
///   1. en-tête institutionnel (République du Sénégal, Ministère de l'Éducation nationale, IA, IEF, établissement) ;
///   2. tableau récapitulatif ventilé Filles / Garçons / Total — effectif, présents, classés, admis, taux de
///      réussite (moyenne ≥ moitié du barème) — puis moyenne de la classe, extrêmes et décompte des distinctions
///      (période) ou des décisions (annuel) : <see cref="DeliberationStatistics"/>, jamais recalculé ici ;
///   3. la liste des élèves par ordre de mérite, avec leur distinction et la décision du conseil — au PV annuel,
///      une décision non encore prise imprime la PROPOSITION (« Proposé : … », en italique), jamais une décision
///      inventée.
/// </summary>
public class ClassDeliberationDocument(
    IReadOnlyList<ReportCardDto> reportCards, byte[]? logo, DeliberationScope scope = DeliberationScope.Period) : IDocument
{
    private const float RuleThickness = 0.75f;

    private bool IsAnnual => scope == DeliberationScope.Annual;

    public DocumentMetadata GetMetadata() => new()
    {
        Title = reportCards.Count > 0 ? $"PV Conseil de classe — {reportCards[0].ClassroomName}" : "PV Conseil de classe",
        Author = reportCards.Count > 0 ? reportCards[0].SchoolName : string.Empty
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(15, Unit.Millimetre);
            page.DefaultTextStyle(text => text.FontFamily("Times New Roman").FontSize(10).FontColor(Colors.Black));

            page.Header().Element(ComposeHeader);
            page.Content().PaddingVertical(10).Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    public static string DecisionLabel(CouncilDecision? decision) => decision switch
    {
        CouncilDecision.Admitted => "Admis(e) en classe supérieure",
        CouncilDecision.AllowedToRepeat => "Autorisé(e) à redoubler",
        CouncilDecision.Excluded => "Exclu(e)",
        _ => string.Empty
    };

    public static string DistinctionLabel(DisciplinaryMention? mention) => mention switch
    {
        DisciplinaryMention.Felicitations => "Félicitations",
        DisciplinaryMention.TableauHonneur => "Tableau d'honneur",
        DisciplinaryMention.Encouragements => "Encouragements",
        DisciplinaryMention.Avertissement => "Avertissement",
        DisciplinaryMention.Blame => "Blâme",
        _ => string.Empty
    };

    private static string Rate(decimal? rate) => rate is { } r ? $"{ReportCardDocument.FormatGrade(r)} %" : "—";

    private void ComposeHeader(IContainer container)
    {
        if (reportCards.Count == 0) return;
        var first = reportCards[0];

        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("RÉPUBLIQUE DU SÉNÉGAL").Bold().FontSize(9);
                    left.Item().Text("Un Peuple – Un But – Une Foi").Italic().FontSize(8);
                    left.Item().Text("MINISTÈRE DE L'ÉDUCATION NATIONALE").Bold().FontSize(9);
                    left.Item().Text(ReportCardDocument.Upper(first.InspectionAcademie)).Bold().FontSize(9);
                    left.Item().Text(ReportCardDocument.Upper(first.InspectionEducationFormation)).Bold().FontSize(9);
                    left.Item().Text(first.HeadingPrefix + " " + ReportCardDocument.Upper(first.HeadingName)).Bold().FontSize(9);
                });

                if (logo != null)
                {
                    row.ConstantItem(60).Image(logo);
                }

                row.RelativeItem().AlignRight().Column(right =>
                {
                    right.Item().Text($"Année scolaire : {first.SchoolYearLabel}").Bold();
                    right.Item().Text(IsAnnual ? "Bilan annuel" : first.TermLabel).Bold();
                    right.Item().Text($"Classe : {first.ClassroomName}").Bold();
                });
            });

            column.Item().PaddingTop(12).AlignCenter()
                .Text(IsAnnual ? "PROCÈS-VERBAL DU CONSEIL DE CLASSE — FIN D'ANNÉE" : "PROCÈS-VERBAL DU CONSEIL DE CLASSE")
                .Bold().FontSize(15).Underline();

            // Classe passerelle / accélérée : le PV DOIT dire que la délibération porte sur deux niveaux.
            if (first.AcceleratedPathLabel is not null)
            {
                column.Item().PaddingTop(3).AlignCenter().Text(first.AcceleratedPathLabel).Italic().Bold().FontSize(11);
            }

            column.Item().PaddingTop(10).Element(ComposeStatistics);
        });
    }

    private void ComposeStatistics(IContainer container)
    {
        var stats = DeliberationStatistics.Compute(reportCards, scope);
        var scale = reportCards[0].GradingScale;

        container.Column(column =>
        {
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1.4f);
                    for (var i = 0; i < 5; i++) columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    foreach (var title in new[] { "", "Effectif", "Présents", "Classés", $"Moy. ≥ {scale / 2m:0.##}", "Taux de réussite" })
                    {
                        header.Cell().Element(HeaderCell).AlignCenter().Text(title).Bold().FontSize(9);
                    }
                });

                foreach (var (label, breakdown) in new[] { ("Filles", stats.Girls), ("Garçons", stats.Boys), ("Total", stats.Total) })
                {
                    table.Cell().Element(BodyCell).Text(label).Bold();
                    table.Cell().Element(BodyCell).AlignCenter().Text(breakdown.Enrolled.ToString());
                    table.Cell().Element(BodyCell).AlignCenter().Text(breakdown.Present.ToString());
                    table.Cell().Element(BodyCell).AlignCenter().Text(breakdown.Ranked.ToString());
                    table.Cell().Element(BodyCell).AlignCenter().Text(breakdown.Passed.ToString());
                    table.Cell().Element(BodyCell).AlignCenter().Text(Rate(breakdown.PassRate)).Bold();
                }
            });

            column.Item().PaddingTop(5).Border(RuleThickness).Padding(5).Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text($"Moyenne de la classe : {Grade(stats.ClassAverage)} / {scale}").Bold();
                    col.Item().Text($"Plus forte moyenne : {Grade(stats.Highest)}");
                    col.Item().Text($"Plus faible moyenne : {Grade(stats.Lowest)}");
                });

                row.RelativeItem().Column(col =>
                {
                    if (IsAnnual)
                    {
                        col.Item().Text($"Admis en classe supérieure : {stats.Admitted}");
                        col.Item().Text($"Autorisés à redoubler : {stats.AllowedToRepeat}");
                        col.Item().Text($"Exclus : {stats.Excluded}");
                    }
                    else
                    {
                        col.Item().Text($"Félicitations : {stats.Felicitations}");
                        col.Item().Text($"Tableau d'honneur : {stats.HonorRoll}");
                        col.Item().Text($"Encouragements : {stats.Encouragements}");
                    }
                });
            });
        });

        static string Grade(decimal? value) => value is { } v ? ReportCardDocument.FormatGrade(v) : "—";
    }

    private void ComposeContent(IContainer container)
    {
        if (reportCards.Count == 0) return;
        var first = reportCards[0];

        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(PdfColumnWidths.Identifier); // Matricule
                columns.RelativeColumn(3f);   // Prénoms & Nom
                columns.RelativeColumn(0.5f); // Sexe
                columns.RelativeColumn(1f);   // Moyenne
                columns.RelativeColumn(0.7f); // Rang
                columns.RelativeColumn(1.6f); // Distinction
                columns.RelativeColumn(2.4f); // Décision
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("Matricule").Bold().FontSize(8.5f);
                header.Cell().Element(HeaderCell).Text("Prénoms & Nom").Bold();
                header.Cell().Element(HeaderCell).AlignCenter().Text("Sexe").Bold();
                header.Cell().Element(HeaderCell).AlignCenter().Text(IsAnnual ? "Moy. annuelle" : "Moyenne").Bold();
                header.Cell().Element(HeaderCell).AlignCenter().Text("Rang").Bold();
                header.Cell().Element(HeaderCell).AlignCenter().Text("Distinction").Bold();
                header.Cell().Element(HeaderCell).Text("Décision du Conseil").Bold();
            });

            foreach (var student in reportCards)
            {
                var average = DeliberationStatistics.AverageOf(student, scope);
                var rank = IsAnnual ? student.AnnualRank : (average is null ? null : student.GeneralRank);

                table.Cell().Element(BodyCell).Text(NoBreakText.NoBreak(student.Matricule)).FontSize(8.5f);
                table.Cell().Element(BodyCell).Text(student.StudentFullName).Bold();
                table.Cell().Element(BodyCell).AlignCenter().Text(DeliberationStatistics.IsGirl(student) ? "F" : "G");
                table.Cell().Element(BodyCell).AlignCenter().Text(average is { } a ? ReportCardDocument.FormatGrade(a) : "NC");
                table.Cell().Element(BodyCell).AlignCenter().Text(rank?.ToString() ?? "—");
                table.Cell().Element(BodyCell).AlignCenter().Text(DistinctionLabel(student.DisciplinaryMention)).FontSize(9);

                // Une décision prise s'imprime telle quelle ; au PV annuel, à défaut, la proposition — en
                // italique et préfixée, pour qu'on ne la confonde jamais avec une décision du conseil.
                var decisionCell = table.Cell().Element(BodyCell);
                var validatedLevels = student.ValidatedLevels ?? [];
                if (student.CouncilDecision is { } decided)
                {
                    decisionCell.Text(first.AcceleratedPathLabel is not null && validatedLevels.Count > 1
                        ? $"{DecisionLabel(decided)} — niveaux validés : {string.Join(", ", validatedLevels)}"
                        : DecisionLabel(decided));
                }
                else if (IsAnnual && student.ProposedCouncilDecision is { } proposed)
                {
                    decisionCell.Text($"Proposé : {DecisionLabel(proposed)}").Italic().FontColor(Colors.Grey.Darken2);
                }
            }
        });
    }

    private static IContainer HeaderCell(IContainer c) =>
        c.Border(RuleThickness).BorderColor(Colors.Black).Background(Colors.Grey.Lighten3).PaddingVertical(3).PaddingHorizontal(4);

    private static IContainer BodyCell(IContainer c) =>
        c.Border(0.5f).BorderColor(Colors.Black).PaddingVertical(3).PaddingHorizontal(4);

    private void ComposeFooter(IContainer container)
    {
        container.PaddingTop(20).Row(row =>
        {
            row.RelativeItem().AlignCenter().Text("Le Professeur Principal").Bold();
            row.RelativeItem().AlignCenter().Text("Le Censeur / Surveillant général").Bold();
            row.RelativeItem().AlignCenter().Text("Le Chef d'Établissement").Bold();
        });
    }
}
