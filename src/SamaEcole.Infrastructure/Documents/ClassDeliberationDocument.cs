using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Document PDF générant le PV de délibération d'une classe.
/// Format A4 Portrait. Affiche un en-tête avec les statistiques de la classe,
/// puis le tableau récapitulatif des élèves avec leurs moyennes et la décision du conseil.
/// </summary>
public class ClassDeliberationDocument(IReadOnlyList<ReportCardDto> reportCards, byte[]? logo) : IDocument
{
    private const float RuleThickness = 0.75f;

    public DocumentMetadata GetMetadata() => new()
    {
        Title = reportCards.Count > 0 ? $"PV Délibération — {reportCards[0].ClassroomName}" : "PV Délibération",
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
                    left.Item().Text(ReportCardDocument.Upper(first.InspectionAcademie)).Bold().FontSize(10);
                    left.Item().Text(ReportCardDocument.Upper(first.InspectionEducationFormation)).Bold().FontSize(10);
                    left.Item().Text(first.HeadingPrefix + " " + ReportCardDocument.Upper(first.HeadingName)).Bold().FontSize(10);
                });

                if (logo != null)
                {
                    row.ConstantItem(60).Image(logo);
                }

                row.RelativeItem().AlignRight().Column(right =>
                {
                    right.Item().Text($"Année Scolaire : {first.SchoolYearLabel}").Bold();
                    right.Item().Text(first.TermLabel).Bold();
                });
            });

            column.Item().PaddingVertical(15).AlignCenter().Text("PROCÈS-VERBAL DE DÉLIBÉRATION").Bold().FontSize(16).Underline();

            // Statistiques globales de la classe
            var validAverages = reportCards.Where(r => r.GeneralAverage > 0).Select(r => r.GeneralAverage).ToList();
            decimal classAverage = validAverages.Count > 0 ? validAverages.Average() : 0;
            decimal maxAverage = validAverages.Count > 0 ? validAverages.Max() : 0;
            decimal minAverage = validAverages.Count > 0 ? validAverages.Min() : 0;

            decimal passMark = first.GradingScale / 2m; // 10 sur 20, ou 5 sur 10
            int passedCount = validAverages.Count(a => a >= passMark);
            decimal passRate = validAverages.Count > 0 ? (decimal)passedCount / validAverages.Count * 100 : 0;

            column.Item().Border(RuleThickness).BorderColor(Colors.Black).Padding(5).Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text($"Classe : {first.ClassroomName}").Bold();
                    col.Item().Text($"Effectif : {first.ClassSize}");
                });
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text($"Moyenne Générale : {ReportCardDocument.FormatGrade(classAverage)} / {first.GradingScale}").Bold();
                    col.Item().Text($"Plus forte moyenne : {ReportCardDocument.FormatGrade(maxAverage)}");
                    col.Item().Text($"Plus faible moyenne : {ReportCardDocument.FormatGrade(minAverage)}");
                });
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text($"Admis : {passedCount}");
                    col.Item().Text($"Taux de réussite : {ReportCardDocument.FormatGrade(passRate)} %").Bold();
                });
            });
        });
    }

    private void ComposeContent(IContainer container)
    {
        if (reportCards.Count == 0) return;
        var first = reportCards[0];

        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(78);   // Matricule — largeur fixe, garantit "ELEV-2025-0005" sur une seule ligne
                columns.RelativeColumn(3f);   // Prénoms & Nom
                columns.RelativeColumn(1f);   // Moyenne
                columns.RelativeColumn(0.8f); // Rang
                columns.RelativeColumn(1.5f); // Mention
                columns.RelativeColumn(2.5f); // Décision / Observation
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("Matricule").Bold().FontSize(8.5f);
                header.Cell().Element(HeaderCell).Text("Prénoms & Nom").Bold();
                header.Cell().Element(HeaderCell).AlignCenter().Text("Moyenne").Bold();
                header.Cell().Element(HeaderCell).AlignCenter().Text("Rang").Bold();
                header.Cell().Element(HeaderCell).AlignCenter().Text("Mention").Bold();
                header.Cell().Element(HeaderCell).Text("Décision du Conseil").Bold();
            });

            foreach (var student in reportCards)
            {
                table.Cell().Element(BodyCell).Text(MatriculeText.NoBreak(student.Matricule)).FontSize(8.5f);
                table.Cell().Element(BodyCell).Text(student.StudentFullName).Bold();
                table.Cell().Element(BodyCell).AlignCenter().Text(ReportCardDocument.FormatGrade(student.GeneralAverage));
                table.Cell().Element(BodyCell).AlignCenter().Text(student.GeneralRank.ToString());
                table.Cell().Element(BodyCell).AlignCenter().Text(student.Mention ?? "—");

                var decisionText = student.CouncilDecision switch
                {
                    SamaEcole.Domain.Enums.CouncilDecision.Admitted => "Admis(e)",
                    SamaEcole.Domain.Enums.CouncilDecision.AllowedToRepeat => "Autorisé(e) à redoubler",
                    SamaEcole.Domain.Enums.CouncilDecision.Excluded => "Exclusion",
                    _ => ""
                };
                
                table.Cell().Element(BodyCell).Text(decisionText);
            }
        });

        static IContainer HeaderCell(IContainer c) =>
            c.Border(RuleThickness).BorderColor(Colors.Black).Background(Colors.Grey.Lighten3).PaddingVertical(4).PaddingHorizontal(4);
        
        static IContainer BodyCell(IContainer c) =>
            c.Border(0.5f).BorderColor(Colors.Black).PaddingVertical(3).PaddingHorizontal(4);
    }

    private void ComposeFooter(IContainer container)
    {
        container.PaddingTop(20).Row(row =>
        {
            row.RelativeItem().AlignCenter().Text("Le Professeur Principal").Bold();
            row.RelativeItem().AlignCenter().Text("Le Chef d'Établissement").Bold();
        });
    }
}
