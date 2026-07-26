using System.Globalization;
using SamaEcole.Application.Absences.Queries.GetEntryTicket;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Billet d'entrée en classe (A5). Remis par la Surveillance à un élève arrivé en retard pour
/// l'autoriser à rejoindre son cours ; l'enseignant le conserve. Reproduit la même charte que les
/// autres documents officiels (en-tête établissement, numéro, cadre motif, signature).
/// </summary>
public class EntryTicketDocument(EntryTicketDto ticket, byte[]? logo) : IDocument
{
    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Billet d'entrée {ticket.TicketNumber}",
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
                    .Text("BILLET D'ENTRÉE EN CLASSE").Bold().FontSize(15);
                column.Item().AlignCenter()
                    .Text($"N° {ticket.TicketNumber}").Italic().FontSize(9).FontColor(Colors.Grey.Darken2);

                column.Item().PaddingTop(10).Element(ComposeInfoBlock);

                column.Item().PaddingTop(8).Element(ComposeMotiveBlock);

                column.Item().PaddingTop(14).Element(ComposeSignatures);
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

                var legal = JoinPresent(
                    ticket.SchoolNinea is null ? null : $"NINEA : {ticket.SchoolNinea}",
                    ticket.SchoolRegistreCommerce is null ? null : $"RCCM : {ticket.SchoolRegistreCommerce}");
                if (legal.Length > 0)
                {
                    header.Item().Text(legal).FontSize(7).FontColor(Colors.Grey.Darken2);
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
            InfoRow(column, "Retard constaté", $"{ticket.Minutes} minute(s)");
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
            column.Item().Text("MOTIF / OBSERVATION").Bold().FontSize(9).FontColor(Colors.Grey.Darken3);
            column.Item().PaddingTop(4).Text(string.IsNullOrWhiteSpace(ticket.Reason) ? "—" : ticket.Reason)
                .FontSize(9);
            column.Item().PaddingTop(6).Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(8).Italic().LineHeight(1.3f));
                text.Span("L'élève désigné ci-dessus est autorisé à rejoindre sa classe. Ce billet est à remettre à l'enseignant.");
            });
        });
    }

    private void ComposeSignatures(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text(FaitA()).Italic();
                left.Item().PaddingTop(6).Text("[Cachet]").FontSize(7).FontColor(Colors.Grey.Medium);
            });

            row.RelativeItem().AlignRight().Text("Signature du Surveillant").Italic();
        });
    }

    private string FaitA()
    {
        var date = FormatDate(ticket.Date);
        return string.IsNullOrWhiteSpace(ticket.SchoolCity)
            ? $"Fait le {date}"
            : $"Fait à {ticket.SchoolCity}, le {date}";
    }

    private static string JoinPresent(params string?[] parts) =>
        string.Join("  ·  ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static string FormatDate(DateTime moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
