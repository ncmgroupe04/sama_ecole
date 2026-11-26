using System.Globalization;
using SamaEcole.Application.Common;
using SamaEcole.Application.Absences.Queries.GetAbsenceTicket;
using SamaEcole.Infrastructure.Documents.Components;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Billet d'absence (A5), pendant des billets d'entrée et de sortie, pour une absence de plusieurs jours
/// enregistrée par la Surveillance. Remis à la famille et versé au dossier de l'élève.
/// </summary>
public class AbsenceTicketDocument(AbsenceTicketDto ticket, byte[]? logo, byte[] qrCodeImage, byte[]? surveillantSignature = null) : IDocument
{
    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Billet d'absence {ticket.TicketNumber}",
        Author = ticket.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A5.Landscape());
            page.Margin(8, Unit.Millimetre);
            page.DefaultTextStyle(text => text.FontSize(9).FontColor(Colors.Black).DisableFontFeature(FontFeatures.StandardLigatures));

            page.Content().Column(column =>
            {
                ComposeHeader(column);

                column.Item().PaddingTop(8).AlignCenter()
                    .Text("BILLET D'ABSENCE").Bold().FontSize(15);
                column.Item().AlignCenter()
                    .Text($"N° {NoBreakText.NoBreak(ticket.TicketNumber)}").Italic().FontSize(9).FontColor(Colors.Grey.Darken2);

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

                var contact = JoinPresent(ticket.SchoolAddress, PhoneFormatter.FormatSenegal(ticket.SchoolPhone), ticket.SchoolEmail);
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
            InfoRow(column, "Matricule", NoBreakText.NoBreak(ticket.Matricule));
            InfoRow(column, "Classe", $"{ticket.ClassroomName} — {ticket.ClassroomLevel}");
            InfoRow(column, "Absence", ticket.StartDate == ticket.EndDate
                ? $"Le {FormatDate(ticket.StartDate)}"
                : $"Du {FormatDate(ticket.StartDate)} au {FormatDate(ticket.EndDate)}");
            InfoRow(column, "Durée", ticket.Days > 1 ? $"{ticket.Days} jours" : "1 jour");
            if (ticket.AuthorizedReturnDate is { } back)
            {
                InfoRow(column, "Retour autorisé le", FormatDate(back));
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

            row.RelativeItem().Column(right =>
            {
                // Signature réelle si le Surveillant Général l'a téléversée (Paramètres → Établissement) ;
                // sinon simple libellé, comme avant (même patron que EntryTicketDocument).
                if (surveillantSignature is not null)
                {
                    right.Item().AlignRight().Height(20).Image(surveillantSignature).FitArea();
                }

                right.Item().PaddingTop(surveillantSignature is not null ? 1 : 0).AlignRight()
                    .Text("Signature du Surveillant").Italic();
            });
        });
    }

    private string FaitA() => $"Fait le {FormatDate(ticket.IssuedOn)}";

    private static string JoinPresent(params string?[] parts) =>
        string.Join("  ·  ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static string FormatDate(DateOnly day) =>
        day.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
