using System.Globalization;
using SamaEcole.Application.Common;
using SamaEcole.Application.Students;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Export PDF des élèves (GET /students/export/pdf) : un tableau listant les élèves du périmètre
/// filtré (classe et/ou année active). Document de travail interne (pas une pièce officielle) — même
/// style épuré que <see cref="AttendanceReportDocument"/> : pas de logo ni de cachet.
/// </summary>
public class StudentsExportDocument(StudentsExportModel model) : IDocument
{
    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Liste des élèves — {model.ClassName ?? "Toutes les classes"}",
        Author = model.SchoolName ?? string.Empty
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1.5f, Unit.Centimetre);
            page.DefaultTextStyle(text => text.FontSize(9).FontColor(Colors.Black));

            page.Header().Element(ComposeHeader);
            page.Content().PaddingTop(8).Element(ComposeTable);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.BorderBottom(1).BorderColor(Colors.Black).PaddingBottom(4).Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Text((model.SchoolName ?? string.Empty).ToUpperInvariant()).Bold().FontSize(12);
                row.RelativeItem().AlignRight().Text("LISTE DES ÉLÈVES").Bold().FontSize(12);
            });

            column.Item().PaddingTop(3).Row(row =>
            {
                row.RelativeItem().Text(t =>
                {
                    t.Span("Classe : ").SemiBold();
                    t.Span(model.ClassName ?? "Toutes les classes");
                });
                row.RelativeItem().Text(t =>
                {
                    t.Span("Année scolaire : ").SemiBold();
                    t.Span(model.SchoolYearLabel ?? "Toutes");
                });
                row.RelativeItem().AlignRight().Text(t =>
                {
                    t.Span("Effectif : ").SemiBold();
                    t.Span(model.Students.Count.ToString(CultureInfo.InvariantCulture)).Bold();
                });
            });
        });
    }

    private void ComposeTable(IContainer container)
    {
        container.Table(table =>
        {
            // Les colonnes à contenu de LARGEUR FIXE (matricule, date, genre, téléphone) sont en
            // ConstantColumn, sur les largeurs partagées de PdfColumnWidths : leur gabarit ne varie
            // pas d'un élève à l'autre, une largeur relative les faisait déborder dès que le tableau
            // se resserrait. L'espace nécessaire est repris sur Classe (« CM2 A », très court).
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(PdfColumnWidths.Identifier); // Matricule
                columns.RelativeColumn(2.2f);                       // Nom complet
                columns.RelativeColumn(0.75f);                      // Classe — codes courts (« CM2 A »)
                columns.ConstantColumn(PdfColumnWidths.Date);       // Naissance
                columns.ConstantColumn(PdfColumnWidths.Initial);    // Genre — « M »/« F »
                columns.RelativeColumn(1.8f);                       // Tuteur
                columns.ConstantColumn(PdfColumnWidths.Phone);      // Tél. tuteur
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("Matricule").Bold().FontSize(8);
                header.Cell().Element(HeaderCell).Text("Nom complet").Bold().FontSize(8);
                header.Cell().Element(HeaderCell).Text("Classe").Bold().FontSize(8);
                header.Cell().Element(HeaderCell).AlignCenter().Text("Naissance").Bold().FontSize(8);
                header.Cell().Element(HeaderCell).AlignCenter().Text("Genre").Bold().FontSize(8);
                header.Cell().Element(HeaderCell).Text("Tuteur").Bold().FontSize(8);
                header.Cell().Element(HeaderCell).Text("Tél. tuteur").Bold().FontSize(8);
            });

            if (model.Students.Count == 0)
            {
                table.Cell().ColumnSpan(7).Element(BodyCell).AlignCenter().PaddingVertical(8)
                    .Text("Aucun élève pour ce périmètre.").FontColor(Colors.Grey.Darken1);
                return;
            }

            foreach (var s in model.Students)
            {
                table.Cell().Element(BodyCell).Text(NoBreakText.NoBreak(s.Matricule)).FontSize(8);
                table.Cell().Element(BodyCell).Text(s.FullName);
                table.Cell().Element(BodyCell).Text(s.ClassroomName);
                table.Cell().Element(BodyCell).AlignCenter().Text(Format(s.BirthDate));
                table.Cell().Element(BodyCell).AlignCenter().Text(s.Gender);
                table.Cell().Element(BodyCell).Text(s.GuardianName ?? "—");
                table.Cell().Element(BodyCell).Text(NoBreakText.NoBreak(PhoneFormatter.FormatSenegalOr(s.GuardianPhone)));
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
}
