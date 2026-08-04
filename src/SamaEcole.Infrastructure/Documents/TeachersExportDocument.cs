using System.Globalization;
using SamaEcole.Application.Common;
using SamaEcole.Application.Teachers;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Export PDF des enseignants (GET /teachers/export/pdf) : « LISTE DES ENSEIGNANTS ». Pendant exact de
/// <see cref="StudentsExportDocument"/> — document de travail interne (pas une pièce officielle), même
/// style épuré : pas de logo ni de cachet.
///
/// Toutes les colonnes à gabarit fixe passent par <see cref="PdfColumnWidths"/> : un matricule occupe
/// ici la même largeur que sur la liste des élèves ou le journal de caisse. L'espace ainsi immobilisé
/// est repris sur Matières et Statut — deux colonnes secondaires dont le contenu est court
/// (« Actif », « Maths, Physique ») et qui, en largeur relative généreuse, laissaient de grandes
/// zones vides pendant que le nom et l'e-mail se repliaient sur deux lignes.
/// </summary>
public class TeachersExportDocument(TeachersExportModel model) : IDocument
{
    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Liste des enseignants — {model.StatusLabel ?? "Tous les statuts"}",
        Author = model.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            // Paysage : neuf colonnes utiles (dont matricule, naissance et téléphone à largeur fixe)
            // ne tiennent pas en portrait sans écraser le nom et l'e-mail.
            page.Size(PageSizes.A4.Landscape());
            page.Margin(1.2f, Unit.Centimetre);
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
                row.RelativeItem().Text(model.SchoolName.ToUpperInvariant()).Bold().FontSize(12);
                row.RelativeItem().AlignRight().Text("LISTE DES ENSEIGNANTS").Bold().FontSize(12);
            });

            column.Item().PaddingTop(3).Row(row =>
            {
                row.RelativeItem().Text(t =>
                {
                    t.Span("Statut : ").SemiBold();
                    t.Span(model.StatusLabel ?? "Tous");
                });
                row.RelativeItem().AlignRight().Text(t =>
                {
                    t.Span("Effectif : ").SemiBold();
                    t.Span(model.Teachers.Count.ToString(CultureInfo.InvariantCulture)).Bold();
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
                columns.ConstantColumn(PdfColumnWidths.Identifier); // Matricule — « ENS‑2025‑0007 »
                columns.RelativeColumn(2.4f);                       // Nom complet — contenu libre
                columns.ConstantColumn(PdfColumnWidths.Date);       // Naissance — l'année ne saute plus à la ligne
                columns.ConstantColumn(PdfColumnWidths.Phone);      // Téléphone — forme longue « +221 77 000 00 00 »
                columns.RelativeColumn(2.4f);                       // E-mail — contenu libre, souvent long
                columns.RelativeColumn(1.9f);                       // Matières — resserrée, libellés courts
                columns.RelativeColumn(0.8f);                       // Statut — « Actif », l'en-tête fait la largeur
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("Matricule").Bold().FontSize(8);
                header.Cell().Element(HeaderCell).Text("Nom complet").Bold().FontSize(8);
                header.Cell().Element(HeaderCell).AlignCenter().Text("Naissance").Bold().FontSize(8);
                header.Cell().Element(HeaderCell).Text("Téléphone").Bold().FontSize(8);
                header.Cell().Element(HeaderCell).Text("E-mail").Bold().FontSize(8);
                header.Cell().Element(HeaderCell).Text("Matières").Bold().FontSize(8);
                header.Cell().Element(HeaderCell).AlignCenter().Text("Statut").Bold().FontSize(8);
            });

            if (model.Teachers.Count == 0)
            {
                table.Cell().ColumnSpan(7).Element(BodyCell).AlignCenter().PaddingVertical(8)
                    .Text("Aucun enseignant pour ce périmètre.").FontColor(Colors.Grey.Darken1);
                return;
            }

            foreach (var t in model.Teachers)
            {
                table.Cell().Element(BodyCell).Text(NoBreakText.NoBreak(t.Matricule)).FontSize(8);
                table.Cell().Element(BodyCell).Text(t.FullName);
                table.Cell().Element(BodyCell).AlignCenter().Text(Format(t.BirthDate));
                table.Cell().Element(BodyCell).Text(NoBreakText.NoBreak(PhoneFormatter.FormatSenegalOr(t.Phone)));
                table.Cell().Element(BodyCell).Text(t.Email).FontSize(8);
                table.Cell().Element(BodyCell).Text(string.IsNullOrWhiteSpace(t.Subjects) ? "—" : t.Subjects).FontSize(8);
                table.Cell().Element(BodyCell).AlignCenter().Text(t.StatusLabel).FontSize(8);
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
