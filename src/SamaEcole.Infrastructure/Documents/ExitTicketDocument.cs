using System.Globalization;
using SamaEcole.Application.Absences.Queries.GetExitTicket;
using SamaEcole.Infrastructure.Documents.Components;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Billet de sortie en classe (A5), pendant de <see cref="EntryTicketDocument"/> pour une sortie
/// anticipée déjà enregistrée par la Surveillance. Remis au parent/à la personne qui récupère l'élève.
/// </summary>
public class ExitTicketDocument(ExitTicketDto ticket, byte[]? logo, byte[] qrCodeImage) : IDocument
{
    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Billet de sortie {ticket.TicketNumber}",
        Author = ticket.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A5.Landscape());
            page.Margin(8, Unit.Millimetre);
            page.DefaultTextStyle(text => text.FontSize(9).FontColor(Colors.Black));

            page.Content().Column(column =>
            {
                ComposeHeader(column);

                column.Item().PaddingTop(8).AlignCenter()
                    .Text("BILLET DE SORTIE").Bold().FontSize(15);
                column.Item().AlignCenter()
                    .Text($"N° {ticket.TicketNumber}").Italic().FontSize(9).FontColor(Colors.Grey.Darken2);

                column.Item().PaddingTop(10).Element(ComposeInfoBlock);
                column.Item().PaddingTop(8).Element(ComposeMotiveBlock);
                column.Item().PaddingTop(10).Element(ComposeSignatures);
                column.Item().PaddingTop(8).Element(c => OfficialHeaderComponent.ComposeAuthenticityFooter(c, qrCodeImage, ticket.TicketNumber));
            });
        });
    }

    private void ComposeHeader(ColumnDescriptor column)
    {
        column.Item().BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingBottom(4).Row(row =>
        {
            row.RelativeItem().Column(header =>
            {
                header.Item().Text(ticket.SchoolName.ToUpperInvariant()).Bold().FontSize(13);

                var contact = JoinPresent(ticket.SchoolAddress, ticket.SchoolPhone, ticket.SchoolEmail);
                if (contact.Length > 0)
                {
                    header.Item().Text(contact).FontSize(7).FontColor(Colors.Grey.Darken2);
                }
            });

            if (logo is not null)
            {
                row.ConstantItem(60).MaxHeight(42).Image(logo).FitArea();
            }
            else
            {
                row.ConstantItem(60).AlignRight().AlignMiddle()
                    .Text("[Logo officiel]").FontSize(7).FontColor(Colors.Grey.Medium);
            }
        });
    }

    private void ComposeInfoBlock(IContainer container)
    {
        container.Column(column =>
        {
            InfoRow(column, "Élève", ticket.StudentFullName);
            InfoRow(column, "Matricule", MatriculeText.NoBreak(ticket.Matricule));
            InfoRow(column, "Classe", $"{ticket.ClassroomName} — {ticket.ClassroomLevel}");
            InfoRow(column, "Date", FormatDate(ticket.Date));
            InfoRow(column, "Heure de sortie", ticket.DepartureTime.ToString("HH:mm", CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(ticket.PickedUpBy))
            {
                InfoRow(column, "Récupéré par", ticket.PickedUpBy);
            }
        });
    }

    private static void InfoRow(ColumnDescriptor column, string label, string value)
    {
        column.Item().PaddingVertical(1).Row(row =>
        {
            row.ConstantItem(120).Text($"{label} :").FontColor(Colors.Grey.Darken2);
            row.RelativeItem().Text(value).SemiBold();
        });
    }

    private void ComposeMotiveBlock(IContainer container)
    {
        container.Border(0.75f).BorderColor(Colors.Grey.Darken1).Background(Colors.Grey.Lighten4).Padding(8).Column(column =>
        {
            column.Item().Text("MOTIF").Bold().FontSize(9).FontColor(Colors.Grey.Darken3);
            column.Item().PaddingTop(4).Text(ticket.Reason).FontSize(9);
        });
    }

    private void ComposeSignatures(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text(FaitA()).Italic();
            });

            row.RelativeItem().AlignRight().Text("Signature du Surveillant").Italic();
        });
    }

    private string FaitA() => $"Fait le {FormatDate(ticket.Date)}";

    private static string JoinPresent(params string?[] parts) =>
        string.Join("  ·  ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static string FormatDate(DateTime moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
