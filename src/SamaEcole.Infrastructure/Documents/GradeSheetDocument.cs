using System.Globalization;
using SamaEcole.Application.Grades.Queries.GetGradeSheetPdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Fiche de saisie PAPIER des notes (A4 portrait) — une grille VIERGE que l'enseignant remplit au
/// stylo dans la salle, avant de reporter les notes à l'écran. Une ligne par élève, par ordre
/// alphabétique (fourni par la requête), avec deux colonnes vides : « Note » (sur le barème de la
/// matière) et « Appréciation ». Volontairement dépourvue de toute note pré-remplie : ce n'est pas la
/// feuille Excel de réimport, c'est un support d'écriture.
///
/// Document de travail interne, même charte sobre que la fiche de suivi des heures
/// (<see cref="HourRecordSheetDocument"/>) : pas de bandeau M.E.N., ni le gabarit du bulletin
/// (docs/design-references/ ne s'applique qu'au reçu, au bulletin et au tableau de bord).
/// </summary>
public class GradeSheetDocument(GradeSheetPdfDto sheet, byte[]? logo) : IDocument
{
    private const float RowHeight = 26;

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Fiche de saisie — {sheet.ClassroomName} — {sheet.SubjectName} — {sheet.EvaluationLabel}",
        Author = sheet.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(15, Unit.Millimetre);
            page.DefaultTextStyle(text => text.FontSize(9).FontColor(Colors.Black));

            page.Content().Column(column =>
            {
                column.Item().Element(ComposeHeader);

                column.Item().PaddingTop(10).AlignCenter()
                    .Text("FICHE DE SAISIE DES NOTES").Bold().FontSize(14);

                column.Item().PaddingTop(8).Element(ComposeIdentification);
                column.Item().PaddingTop(10).Element(ComposeGrid);
                column.Item().PaddingTop(22).ShowEntire().Element(ComposeSignatures);
            });

            page.Footer().AlignCenter().Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(8).FontColor(Colors.Grey.Darken1));
                text.Span($"{sheet.ClassroomName} · {sheet.SubjectName} · {sheet.EvaluationLabel} — Page ");
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
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

    private void ComposeIdentification(IContainer container)
    {
        container.Border(0.75f).BorderColor(Colors.Grey.Lighten1).Padding(8).Column(outer =>
        {
            outer.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    InfoRow(left, "Classe", sheet.ClassroomName);
                    InfoRow(left, "Matière", sheet.SubjectName);
                    InfoRow(left, "Évaluation", sheet.EvaluationLabel);
                });
                row.RelativeItem().Column(right =>
                {
                    InfoRow(right, "Période", sheet.TermLabel);
                    InfoRow(right, "Année scolaire", sheet.SchoolYearLabel);
                    InfoRow(right, "Barème", $"sur {FormatScale(sheet.MaxScore)}");
                });
            });

            // À compléter à la main : l'enseignant et la date de l'épreuve ne sont pas connus du système.
            outer.Item().PaddingTop(8).Row(row =>
            {
                row.RelativeItem().Text("Enseignant(e) : ............................................................")
                    .FontColor(Colors.Grey.Darken2);
                row.ConstantItem(170).AlignRight().Text("Date de l'épreuve : ....... / ....... / ...........")
                    .FontColor(Colors.Grey.Darken2);
            });
        });
    }

    private static void InfoRow(ColumnDescriptor column, string label, string value) =>
        column.Item().PaddingVertical(1).Row(row =>
        {
            row.ConstantItem(82).Text($"{label} :").FontColor(Colors.Grey.Darken2);
            row.RelativeItem().Text(value).SemiBold();
        });

    private void ComposeGrid(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().PaddingBottom(4).Text($"Effectif : {sheet.Students.Count} élève(s)")
                .FontSize(8).FontColor(Colors.Grey.Darken1);

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(26);                       // N°
                    columns.ConstantColumn(PdfColumnWidths.Identifier); // Matricule
                    columns.RelativeColumn(3);                        // Nom & Prénom
                    columns.ConstantColumn(78);                       // Note (/max)
                    columns.RelativeColumn(3);                        // Appréciation
                });

                // Répété en tête de chaque page : sur une classe de quarante élèves, la fiche tient sur
                // plusieurs pages et l'enseignant doit toujours voir à quelle colonne il écrit.
                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).AlignCenter().Text("N°").Bold();
                    header.Cell().Element(HeaderCell).Text("Matricule").Bold();
                    header.Cell().Element(HeaderCell).Text("Nom et prénom").Bold();
                    header.Cell().Element(HeaderCell).AlignCenter().Text($"Note / {FormatScale(sheet.MaxScore)}").Bold();
                    header.Cell().Element(HeaderCell).Text("Appréciation").Bold();
                });

                if (sheet.Students.Count == 0)
                {
                    table.Cell().ColumnSpan(5).Element(BodyCell).AlignCenter()
                        .Text("Aucun élève dans cette classe.").Italic().FontColor(Colors.Grey.Medium);
                }

                for (var i = 0; i < sheet.Students.Count; i++)
                {
                    var student = sheet.Students[i];
                    table.Cell().Element(BodyCell).AlignCenter().Text((i + 1).ToString(CultureInfo.InvariantCulture))
                        .FontColor(Colors.Grey.Darken1);
                    table.Cell().Element(BodyCell).Text(student.Matricule).FontSize(8);
                    table.Cell().Element(BodyCell).Text(student.FullName);
                    table.Cell().Element(BodyCell).Text(string.Empty);   // Note : à écrire au stylo
                    table.Cell().Element(BodyCell).Text(string.Empty);   // Appréciation : à écrire au stylo
                }
            });
        });
    }

    private void ComposeSignatures(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text("Signature de l'enseignant(e)").FontColor(Colors.Grey.Darken2);
                column.Item().PaddingTop(28).LineHorizontal(0.75f).LineColor(Colors.Grey.Medium);
            });

            row.ConstantItem(40);

            row.RelativeItem().Column(column =>
            {
                column.Item().Text("Visa du Directeur / du Secrétariat").FontColor(Colors.Grey.Darken2);
                column.Item().PaddingTop(28).LineHorizontal(0.75f).LineColor(Colors.Grey.Medium);
            });
        });
    }

    private static IContainer HeaderCell(IContainer container) =>
        container.Background(Colors.Grey.Lighten3).Border(0.75f).BorderColor(Colors.Grey.Medium)
            .PaddingVertical(5).PaddingHorizontal(4);

    /// <summary>Cellule d'une ligne d'élève : hauteur mini fixe, pour laisser la place d'écrire au stylo.</summary>
    private static IContainer BodyCell(IContainer container) =>
        container.Border(0.75f).BorderColor(Colors.Grey.Medium)
            .MinHeight(RowHeight).PaddingHorizontal(4).AlignMiddle();

    /// <summary>Barème sans zéros inutiles : « 20 », « 10 » ou « 40 » — jamais « 20,00 ».</summary>
    private static string FormatScale(decimal scale) =>
        scale.ToString("0.##", CultureInfo.GetCultureInfo("fr-FR"));
}
