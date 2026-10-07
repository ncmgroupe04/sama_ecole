using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SamaEcole.Application.Boarding.Documents.DormitoryRegister;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Registre imprimable d'un pavillon pour UNE nuit (A4 paysage) : chambre, lit, nom (arabe en dessous via
/// <see cref="Bilingual"/>), matricule, classe, présence et observations. Pensé pour être annoté à la main par le surveillant :
/// tant que rien n'est pointé, la colonne « Présence » porte deux cases vides à cocher ; une nuit pointée imprime « Présent »
/// ou « Absent ». Les pensionnaires en permission sont signalés.
///
/// CONFIDENTIALITÉ : document posé dans le pavillon, donc AUCUNE donnée de santé et AUCUN téléphone — le DTO n'en porte pas
/// (verrou par réflexion dans <c>DormitoryRegisterDtoShapeTests</c>) et ce document n'a pas de colonne pour en afficher.
/// </summary>
public class DormitoryRegisterDocument(DormitoryRegisterDto register, byte[]? logo) : IDocument
{
    private static readonly CultureInfo French = new("fr-FR");

    private const string Accent = "#1E3A5F";
    private const string OnLeaveLabel = "En permission";

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Registre du pavillon {register.DormitoryName} — {register.Date.ToString("dd/MM/yyyy", French)}",
        Author = register.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(1.2f, Unit.Centimetre);
            page.DefaultTextStyle(text => text.FontFamily("Times New Roman").FontSize(9.5f).FontColor(Colors.Black));

            page.Header().Element(ComposeHeader);
            page.Content().PaddingTop(8).Element(ComposeTable);

            page.Footer().Row(row =>
            {
                row.RelativeItem().Text(
                        $"Document interne — ne pas afficher — édité le {register.GeneratedAt.ToString("dd/MM/yyyy", French)} — {register.SchoolName}")
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
                    info.Item().Text(register.SchoolName).Bold().FontSize(12);
                    if (!string.IsNullOrWhiteSpace(register.SchoolAddress))
                    {
                        info.Item().Text(register.SchoolAddress).FontSize(8.5f).FontColor(Colors.Grey.Darken2);
                    }
                });

                row.RelativeItem().AlignRight().AlignMiddle().Text(
                        register.SchoolYearLabel is { Length: > 0 } year ? $"Année scolaire {year}" : string.Empty)
                    .FontSize(9).FontColor(Colors.Grey.Darken2);
            });

            column.Item().PaddingTop(6).LineHorizontal(1.2f).LineColor(Accent);
            column.Item().PaddingTop(5).AlignCenter().Text($"REGISTRE DU PAVILLON — {register.DormitoryName}").Bold().FontSize(15);
            column.Item().AlignCenter().Text(text =>
            {
                text.Span($"Nuit du {register.Date.ToString("dd/MM/yyyy", French)}").FontSize(10);
                if (!string.IsNullOrWhiteSpace(register.SupervisorName))
                {
                    text.Span($"   ·   Surveillant : {register.SupervisorName}").FontSize(10);
                }
            });
        });

    private void ComposeTable(IContainer container)
    {
        if (register.Rows.Count == 0)
        {
            container.PaddingTop(20).AlignCenter().Text("Aucun pensionnaire dans ce pavillon.").Italic().FontSize(11);
            return;
        }

        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(1.5f);   // Chambre
                c.RelativeColumn(0.7f);   // Lit
                c.RelativeColumn(3.4f);   // Nom
                c.RelativeColumn(1.6f);   // Matricule
                c.RelativeColumn(1.3f);   // Classe
                c.RelativeColumn(1.7f);   // Présence
                c.RelativeColumn(2.6f);   // Observations
            });

            // L'en-tête se répète sur chaque page : le registre se lit et se remplit page par page.
            table.Header(header =>
            {
                HeaderCell(header.Cell(), "Chambre");
                HeaderCell(header.Cell(), "Lit");
                HeaderCell(header.Cell(), "Nom");
                HeaderCell(header.Cell(), "Matricule");
                HeaderCell(header.Cell(), "Classe");
                HeaderCell(header.Cell(), "Présence");
                HeaderCell(header.Cell(), "Observations");
            });

            foreach (var row in register.Rows)
            {
                BodyCell(table.Cell()).Text(row.RoomName);
                BodyCell(table.Cell()).Text(row.BedNumber.ToString(French));
                BodyCell(table.Cell()).Column(name =>
                {
                    name.Item().Text(row.StudentName).Bold();
                    name.Item().Element(c => Bilingual.ArabicBlock(c, row.StudentNameAr, 10));
                });
                BodyCell(table.Cell()).Text(row.Matricule);
                BodyCell(table.Cell()).Text(row.ClassroomName);
                BodyCell(table.Cell()).Element(c => ComposePresence(c, row));
                BodyCell(table.Cell()).Text(Observations(row));
            }
        });
    }

    private static void ComposePresence(IContainer cell, DormitoryRegisterRowDto row)
    {
        if (row.Recorded && row.IsPresent is { } present)
        {
            cell.Text(present ? "Présent" : "Absent").Bold();
            return;
        }

        // Rien de pointé : deux cases vides, à cocher à la main.
        cell.Row(boxes =>
        {
            boxes.ConstantItem(11).Height(11).Border(0.8f).BorderColor(Colors.Grey.Darken2);
            boxes.ConstantItem(14);
            boxes.ConstantItem(11).Height(11).Border(0.8f).BorderColor(Colors.Grey.Darken2);
        });
    }

    /// <summary>« En permission » est toujours imprimé pour un pensionnaire en sortie, sans écraser la note saisie par l'opérateur.</summary>
    private static string Observations(DormitoryRegisterRowDto row)
    {
        var note = row.Note?.Trim();

        if (!row.OnLeave)
        {
            return note ?? string.Empty;
        }

        if (string.IsNullOrEmpty(note))
        {
            return OnLeaveLabel;
        }

        return note.Contains("permission", StringComparison.OrdinalIgnoreCase) ? note : $"{OnLeaveLabel} — {note}";
    }

    private static void HeaderCell(IContainer cell, string label) =>
        cell.Background(Colors.Grey.Lighten3).Border(0.4f).BorderColor(Colors.Grey.Lighten1).Padding(3)
            .Text(label).Bold().FontSize(9);

    private static IContainer BodyCell(IContainer cell) =>
        cell.Border(0.4f).BorderColor(Colors.Grey.Lighten1).Padding(3).MinHeight(22).AlignMiddle();
}
