using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SamaEcole.Application.Boarding.Documents.BoarderSheet;
using SamaEcole.Application.Boarding.Leaves;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Fiche individuelle d'un pensionnaire (A4 portrait) : identité (nom arabe via <see cref="Bilingual"/>), hébergement,
/// contact d'urgence, personnes habilitées, fiche médicale et historique des sorties de l'année.
///
/// CONFIDENTIALITÉ : la fiche médicale est une donnée de santé d'un mineur. Ce document ne décide JAMAIS de la montrer : il
/// l'imprime si, et seulement si, <see cref="BoarderSheetDto.MedicalNotes"/> est renseigné — c'est la requête qui le met à
/// <c>null</c> pour tout rôle autre que Directeur et Surveillant. Quand il est vide, le bloc entier (titre compris) disparaît.
/// </summary>
public class BoarderSheetDocument(BoarderSheetDto sheet, byte[]? logo) : IDocument
{
    private static readonly CultureInfo French = new("fr-FR");

    private const string Accent = "#1E3A5F";

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Fiche de pensionnaire — {sheet.Boarder.StudentName} ({sheet.Boarder.Matricule})",
        Author = sheet.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1.4f, Unit.Centimetre);
            page.DefaultTextStyle(text => text.FontFamily("Times New Roman").FontSize(9.5f).FontColor(Colors.Black));

            page.Content().Column(column =>
            {
                column.Item().Element(ComposeHeader);
                column.Item().PaddingTop(10).Element(ComposeIdentity);
                column.Item().PaddingTop(8).Element(ComposeHousing);
                column.Item().PaddingTop(8).Element(ComposeContacts);

                if (!string.IsNullOrWhiteSpace(sheet.MedicalNotes))
                {
                    column.Item().PaddingTop(8).ShowEntire().Element(ComposeMedical);
                }

                column.Item().PaddingTop(8).Element(ComposeLeaves);
            });

            page.Footer().Row(row =>
            {
                row.RelativeItem().Text(
                        $"Document confidentiel — édité le {sheet.GeneratedAt.ToString("dd/MM/yyyy", French)} — {sheet.SchoolName}")
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
                    info.Item().Text(sheet.SchoolName).Bold().FontSize(13);
                    if (!string.IsNullOrWhiteSpace(sheet.SchoolAddress))
                    {
                        info.Item().Text(sheet.SchoolAddress).FontSize(8.5f).FontColor(Colors.Grey.Darken2);
                    }
                });
            });

            column.Item().PaddingTop(10).LineHorizontal(1.2f).LineColor(Accent);
            column.Item().PaddingTop(8).AlignCenter().Text("FICHE DE PENSIONNAIRE").Bold().FontSize(16);
            if (!string.IsNullOrWhiteSpace(sheet.SchoolYearLabel))
            {
                column.Item().PaddingTop(2).AlignCenter()
                    .Text($"Année scolaire {sheet.SchoolYearLabel}").FontSize(9).FontColor(Colors.Grey.Darken2);
            }
        });
    }

    private void ComposeIdentity(IContainer container) =>
        Section(container, "Identité", body => body.Column(column =>
        {
            column.Item().Text(sheet.Boarder.StudentName).Bold().FontSize(13);
            column.Item().Element(c => Bilingual.ArabicBlock(c, sheet.FullNameAr, 13, bold: true));
            column.Item().PaddingTop(3).Element(c => Field(c, "Matricule", sheet.Boarder.Matricule));
            column.Item().Element(c => Field(c, "Né(e) le", $"{sheet.BirthDate.ToString("dd/MM/yyyy", French)} à {sheet.BirthPlace}"));
            column.Item().Element(c => Field(c, "Classe", sheet.Boarder.ClassroomName));
        }));

    private void ComposeHousing(IContainer container) =>
        Section(container, "Hébergement", body => body.Column(column =>
        {
            var boarder = sheet.Boarder;
            column.Item().Element(c => Field(c, "Régime", boarder.Regime == BoardingRegime.Interne ? "Interne" : "Demi-pensionnaire"));

            if (boarder.IsActive && boarder.BedNumber is { } bed)
            {
                column.Item().Element(c => Field(
                    c, "Lit", $"{boarder.DormitoryName} — {boarder.RoomName} — lit n° {bed}"));
            }
            else if (boarder.IsActive && boarder.Regime == BoardingRegime.Interne)
            {
                column.Item().Text("Aucun lit attribué pour le moment.").Italic();
            }

            column.Item().Element(c => Field(c, "Séjour depuis le", boarder.StartDate.ToString("dd/MM/yyyy", French)));
            if (!boarder.IsActive && boarder.EndDate is { } end)
            {
                column.Item().Text($"Séjour clos le {end.ToString("dd/MM/yyyy", French)}").Bold();
            }
        }));

    private void ComposeContacts(IContainer container) =>
        Section(container, "Contact d'urgence et personnes habilitées", body => body.Column(column =>
        {
            column.Item().Element(c => Field(c, "Contact d'urgence", Join(sheet.EmergencyContactName, sheet.EmergencyContactPhone)));

            if (sheet.AllowedExitPersons.Count == 0)
            {
                column.Item().PaddingTop(3).Text("Aucune personne habilitée à récupérer le pensionnaire n'est renseignée.").Italic();
                return;
            }

            column.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(3);
                    c.RelativeColumn(2);
                    c.RelativeColumn(2);
                });
                table.Header(h =>
                {
                    HeaderCell(h.Cell(), "Nom");
                    HeaderCell(h.Cell(), "Lien");
                    HeaderCell(h.Cell(), "Téléphone");
                });
                foreach (var person in sheet.AllowedExitPersons)
                {
                    BodyCell(table.Cell(), person.Name);
                    BodyCell(table.Cell(), person.Relationship);
                    BodyCell(table.Cell(), person.Phone);
                }
            });
        }));

    private void ComposeMedical(IContainer container) =>
        Section(container, "Fiche médicale", body => body.Text(sheet.MedicalNotes!.Trim()));

    private void ComposeLeaves(IContainer container) =>
        Section(container, "Sorties de l'année", body =>
        {
            if (sheet.Leaves.Count == 0)
            {
                body.Text("Aucune sortie enregistrée cette année.").Italic();
                return;
            }

            body.Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(2);
                    c.RelativeColumn(2);
                    c.RelativeColumn(2);
                    c.RelativeColumn(2);
                    c.RelativeColumn(3);
                    c.RelativeColumn(2);
                });
                table.Header(h =>
                {
                    HeaderCell(h.Cell(), "Sortie");
                    HeaderCell(h.Cell(), "Retour prévu");
                    HeaderCell(h.Cell(), "Retour réel");
                    HeaderCell(h.Cell(), "Motif");
                    HeaderCell(h.Cell(), "Accompagnateur");
                    HeaderCell(h.Cell(), "Statut");
                });

                foreach (var leave in sheet.Leaves)
                {
                    BodyCell(table.Cell(), Date(leave.LeaveDate));
                    BodyCell(table.Cell(), Date(leave.ExpectedReturnDate));
                    BodyCell(table.Cell(), leave.ActualReturnDate is { } back ? Date(back) : "—");
                    BodyCell(table.Cell(), Reason(leave.Reason));
                    BodyCell(table.Cell(), leave.IsCompanionUnlisted
                        ? $"{leave.AccompaniedBy} — Non habilité"
                        : leave.AccompaniedBy);
                    BodyCell(table.Cell(), Status(leave.Status));
                }
            });
        });

    // ------------------------------------------------------------------ Aides de mise en page

    private static void Section(IContainer container, string title, Action<IContainer> body) =>
        container.Border(0.6f).BorderColor(Colors.Grey.Lighten1).Padding(8).Column(column =>
        {
            column.Item().Text(title.ToUpper(French)).FontSize(8).Bold().FontColor(Accent);
            column.Item().PaddingTop(3).Element(body);
        });

    private static void Field(IContainer container, string label, string value) =>
        container.Text(text =>
        {
            text.Span($"{label} : ").FontColor(Colors.Grey.Darken1);
            text.Span(string.IsNullOrWhiteSpace(value) ? "Non renseigné" : value).Bold();
        });

    private static void HeaderCell(IContainer cell, string label) =>
        cell.Background(Colors.Grey.Lighten3).Border(0.4f).BorderColor(Colors.Grey.Lighten1).Padding(3)
            .Text(label).Bold().FontSize(8.5f);

    private static void BodyCell(IContainer cell, string value) =>
        cell.Border(0.4f).BorderColor(Colors.Grey.Lighten1).Padding(3).Text(value).FontSize(8.5f);

    private static string Join(string? name, string? phone) =>
        string.Join(" — ", new[] { name, phone }.Where(v => !string.IsNullOrWhiteSpace(v)));

    private static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", French);

    private static string Reason(BoardingLeaveReason reason) => reason switch
    {
        BoardingLeaveReason.Weekend => "Week-end",
        BoardingLeaveReason.Sante => "Santé",
        BoardingLeaveReason.Famille => "Famille",
        _ => "Autre"
    };

    private static string Status(LeaveStatus status) => status switch
    {
        LeaveStatus.Pending => "À venir",
        LeaveStatus.Active => "En cours",
        LeaveStatus.Overdue => "En retard",
        _ => "Rentré"
    };
}
