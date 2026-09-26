using System.Globalization;
using SamaEcole.Application.Attendance;
using SamaEcole.Application.Common;
using SamaEcole.Application.Absences.Queries.GetEntryTicket;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Billet d'autorisation d'entrée en classe (A5 paysage). Remis par la Surveillance à un élève arrivé
/// en retard pour l'autoriser à rejoindre son cours ; l'enseignant le conserve. Mise en page calquée à
/// l'identique sur la maquette de référence fournie (bannière de titre, grille d'informations
/// 2 colonnes x 3 rangées, pied de page à deux visas).
/// </summary>
public class EntryTicketDocument(EntryTicketDto ticket, byte[]? logo, byte[]? surveillantSignature = null) : IDocument
{
    private const string HeadingColor = "#111827";
    private const string AccentColor = "#475569";
    private const string BannerBackground = "#F1F5F9";
    private const string BannerBorder = "#E2E8F0";
    private const string RuleColor = "#E2E8F0";

    private const string DefaultObservationsNotice =
        "L'élève désigné ci-dessus est autorisé à rejoindre sa classe. Ce billet est à remettre à l'enseignant.";

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Billet d'autorisation d'entrée {ticket.TicketNumber}",
        Author = ticket.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A5.Landscape());
            page.Margin(15, Unit.Millimetre);
            page.DefaultTextStyle(text => text.FontSize(9).FontColor(HeadingColor));

            page.Content().Column(column =>
            {
                ComposeHeader(column);
                column.Item().PaddingTop(10).Element(ComposeTitleBanner);
                column.Item().PaddingTop(10).Element(ComposeInfoGrid);
                column.Item().PaddingTop(14).Element(ComposeFooter);
            });
        });
    }

    private void ComposeHeader(ColumnDescriptor column)
    {
        column.Item().Row(row =>
        {
            row.ConstantItem(50).Element(ComposeLogoBox);

            row.RelativeItem().PaddingLeft(10).Column(header =>
            {
                header.Item().Text(ticket.SchoolName.ToUpperInvariant()).Bold().FontSize(14).FontColor(HeadingColor);

                if (!string.IsNullOrWhiteSpace(ticket.SchoolAddress))
                {
                    header.Item().Text(ticket.SchoolAddress!).FontSize(8.5f).FontColor(AccentColor);
                }

                var contact = JoinPresent(
                    ticket.SchoolPhone is null ? null : $"Tél: {PhoneFormatter.FormatSenegal(ticket.SchoolPhone)}",
                    ticket.SchoolEmail is null ? null : $"Email: {ticket.SchoolEmail}");
                if (contact.Length > 0)
                {
                    header.Item().Text(contact).FontSize(8.5f).FontColor(AccentColor);
                }

                var legal = JoinPresent(
                    ticket.SchoolNinea is null ? null : $"NINEA: {ticket.SchoolNinea}",
                    ticket.SchoolRegistreCommerce is null ? null : $"RCCM: {ticket.SchoolRegistreCommerce}");
                if (legal.Length > 0)
                {
                    header.Item().PaddingTop(1).Text(legal).FontSize(7).FontColor(Colors.Grey.Medium);
                }
            });

            row.ConstantItem(130).AlignRight()
                .Text("SURVEILLANCE GÉNÉRALE").Bold().FontSize(9).FontColor(AccentColor);
        });
    }

    private void ComposeLogoBox(IContainer container)
    {
        if (logo is not null)
        {
            container.Border(1).BorderColor(Colors.Grey.Lighten1).Padding(3).Height(44).Image(logo).FitArea();
        }
        else
        {
            container.Border(1).BorderColor(Colors.Grey.Lighten1).Padding(3).Height(44).AlignCenter().AlignMiddle()
                .Text("[Logo]").FontSize(6).FontColor(Colors.Grey.Medium);
        }
    }

    private void ComposeTitleBanner(IContainer container)
    {
        container.Column(column =>
        {
            column.Item()
                .Background(BannerBackground)
                .Border(0.75f)
                .BorderColor(BannerBorder)
                .Padding(8)
                .AlignCenter()
                .Text("BILLET D'AUTORISATION D'ENTRÉE EN CLASSE").Bold().FontSize(14).FontColor(HeadingColor);

            column.Item().PaddingTop(4).AlignCenter().Text(text =>
            {
                text.Span("N°  ").FontSize(9).FontColor(AccentColor);
                text.Span(NoBreakText.NoBreak(ticket.TicketNumber)).FontSize(9).FontColor(AccentColor).SemiBold();
            });
        });
    }

    private void ComposeInfoGrid(IContainer container)
    {
        container.BorderTop(1).BorderColor(Colors.Grey.Darken1).Column(column =>
        {
            ComposeGridRow(column, showRule: true,
                left: c => ComposeCell(c, "Date & heure d'émission :", inner =>
                    inner.Item().Text(FormatIssuedAt(ticket.IssuedAt)).Bold().FontSize(10).FontColor(HeadingColor)),
                right: c => ComposeCell(c, "Identité de l'élève", inner =>
                {
                    inner.Item().Text(ticket.StudentFullName).Bold().FontSize(11).FontColor(HeadingColor);
                    inner.Item().PaddingTop(1)
                        .Text($"Matricule: {NoBreakText.NoBreak(ticket.Matricule)}").FontSize(9).FontColor(AccentColor);
                }));

            ComposeGridRow(column, showRule: true,
                left: c => ComposeCell(c, "Classe", inner =>
                {
                    inner.Item().Text(ticket.ClassroomName).Bold().FontSize(11).FontColor(HeadingColor);

                    // Cours visé (Évolution N°5) : ce que l'enseignant reconnaît au premier coup d'œil. Absent
                    // pour un billet sans cours visé, qui s'imprime alors comme avant.
                    if (!string.IsNullOrWhiteSpace(ticket.TargetSubjectName))
                    {
                        inner.Item().PaddingTop(2)
                            .Text($"Cours : {ticket.TargetSubjectName}").FontSize(9).FontColor(HeadingColor).SemiBold();

                        var detail = JoinPresent(ticket.TargetTimeRange, ticket.TargetTeacherName);
                        if (detail.Length > 0)
                        {
                            inner.Item().Text(detail).FontSize(8.5f).FontColor(AccentColor);
                        }
                    }
                }),
                right: c => ComposeCell(c, "Motif du billet", inner =>
                {
                    if (ticket.ArrivalTime is { } arrival)
                    {
                        // Billet par heure d'arrivée (Complément N°5 bis) : l'heure, la durée régularisée, puis le détail
                        // — cours manqués (nommé s'il est seul, résumé sinon) et retard éventuel.
                        var total = ticket.TotalMinutes is { } t ? $" — {DurationText.Human(t)}" : "";
                        inner.Item().Text($"ARRIVÉE À {arrival.ToString("HH:mm", CultureInfo.InvariantCulture)}{total}").Bold().FontSize(10).FontColor(HeadingColor);

                        // Un seul cours manqué : il est nommé ; plusieurs : un résumé (nombre et durée) — le billet A5 ne doit
                        // JAMAIS déborder sur une deuxième page, quel que soit le nombre de cours manqués.
                        var missed = ticket.MissedSlots ?? [];
                        if (missed.Count == 1)
                        {
                            inner.Item().PaddingTop(1)
                                .Text($"Cours manqué : {missed[0].SubjectName} {missed[0].TimeRange}").FontSize(8.5f).FontColor(AccentColor);
                        }
                        else if (missed.Count > 1)
                        {
                            inner.Item().PaddingTop(1)
                                .Text($"{missed.Count} cours manqués ({DurationText.Human(missed.Sum(m => m.Minutes))})").FontSize(8.5f).FontColor(AccentColor);
                        }

                        if (ticket.Minutes > 0)
                        {
                            inner.Item().PaddingTop(1)
                                .Text($"Retard de {ticket.Minutes} min au cours visé").FontSize(8.5f).FontColor(AccentColor);
                        }
                    }
                    else
                    {
                        inner.Item().Text($"RETARD DE {ticket.Minutes} MIN").Bold().FontSize(10).FontColor(HeadingColor);
                    }

                    if (!string.IsNullOrWhiteSpace(ticket.Reason))
                    {
                        inner.Item().PaddingTop(1)
                            .Text($"Raison déclarée : {ticket.Reason}").FontSize(9).FontColor(Colors.Grey.Darken2);
                    }
                }));

            ComposeGridRow(column, showRule: false,
                left: c => ComposeCell(c, "Décision de la surveillance", inner =>
                {
                    // Un billet annulé n'autorise plus rien : il ne doit JAMAIS se lire « ADMIS EN CLASSE ».
                    var cancelled = ticket.Status == "Cancelled";
                    inner.Item().Text(cancelled ? "BILLET ANNULÉ" : "ADMIS EN CLASSE").Bold().FontSize(10).FontColor(HeadingColor);

                    var statusLine = ticket.Status switch
                    {
                        "Issued" => "En attente d'acceptation par l'enseignant",
                        "Accepted" => "Accepté en classe par l'enseignant",
                        _ => null
                    };
                    if (statusLine is not null)
                    {
                        inner.Item().PaddingTop(1).Text(statusLine).FontSize(8.5f).FontColor(AccentColor);
                    }
                }),
                right: c => ComposeCell(c, "Observations", inner =>
                    inner.Item().Text(string.IsNullOrWhiteSpace(ticket.Observations) ? DefaultObservationsNotice : ticket.Observations)
                        .Italic().FontSize(9).FontColor(Colors.Grey.Darken2)));
        });
    }

    private static void ComposeGridRow(ColumnDescriptor column, bool showRule, Action<IContainer> left, Action<IContainer> right)
    {
        var item = showRule
            ? column.Item().BorderBottom(0.5f).BorderColor(RuleColor).PaddingVertical(6)
            : column.Item().PaddingVertical(6);

        item.Row(row =>
        {
            row.RelativeItem().Element(left);
            row.RelativeItem().PaddingLeft(16).Element(right);
        });
    }

    private static void ComposeCell(IContainer container, string label, Action<ColumnDescriptor> content)
    {
        container.Column(inner =>
        {
            inner.Item().PaddingBottom(2).Text(label.ToUpperInvariant()).FontSize(8).FontColor(AccentColor).SemiBold();
            content(inner);
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.BorderTop(0.75f).BorderColor(Colors.Grey.Darken1).PaddingTop(10).Row(row =>
        {
            row.RelativeItem().Text("Visa du Professeur (à la réception)").FontSize(8).FontColor(Colors.Grey.Darken2);

            row.RelativeItem().Column(right =>
            {
                // Signature réelle si le Surveillant Général l'a téléversée (Paramètres → Établissement) ;
                // sinon simple libellé, comme avant (même patron que ReportCardDocument).
                if (surveillantSignature is not null)
                {
                    right.Item().AlignRight().Height(20).Image(surveillantSignature).FitArea();
                }

                right.Item().PaddingTop(surveillantSignature is not null ? 1 : 0).AlignRight()
                    .Text("Cachet & Signature du Surveillant").SemiBold().FontSize(8).FontColor(HeadingColor);
            });
        });
    }

    private static string JoinPresent(params string?[] parts) =>
        string.Join(" | ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static string FormatIssuedAt(DateTimeOffset issuedAt) =>
        $"{issuedAt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} à {issuedAt.ToString("HH:mm", CultureInfo.InvariantCulture)}";
}
