using System.Globalization;
using SamaEcole.Application.Enrollments;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Reçu d'inscription officiel en PDF (ticket JGK-E02). Reproduction FIDÈLE de
/// docs/design-references/receipt-reference.png (AGENTS.md règle #12) : document strictement
/// administratif, noir et blanc, bordures simples, aucune couleur.
///
/// Deux écarts assumés vis-à-vis des pixels de la maquette, qui n'est qu'un gabarit illustratif :
///   * les montants suivent la convention sénégalaise (séparateur de milliers par espace, sans
///     décimales), et non le « 25,000 » anglophone de la capture ;
///   * les dates sont au format jj/MM/aaaa (convention de l'application, SchoolSettings.DateFormat),
///     et non l'ISO de la capture.
///
/// La mention obligatoire est une CONSTANTE ici (AGENTS.md règle #12) : aucun appelant ne peut
/// l'altérer ni l'omettre.
/// </summary>
public class EnrollmentReceiptDocument(EnrollmentReceiptDto receipt) : IDocument
{
    private const string MandatoryMention =
        "Il est demandé aux parents de garder minutieusement leur reçu après le paiement.";

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Reçu d'inscription {receipt.ReceiptNumber}",
        Author = receipt.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimetre);
            page.DefaultTextStyle(text => text.FontSize(11).FontColor(Colors.Black));

            page.Content().Column(column =>
            {
                ComposeHeader(column);
                column.Item().PaddingTop(18).AlignCenter().Text($"REÇU D'INSCRIPTION n° {receipt.ReceiptNumber}")
                    .Bold().Italic().FontSize(15);
                column.Item().PaddingTop(18).Element(ComposeInfoBlock);
                column.Item().PaddingTop(14).Element(ComposeFeesTable);
                column.Item().PaddingTop(20).AlignCenter().Text(MandatoryMention).Italic();
                column.Item().PaddingTop(36).Element(ComposeSignatures);
            });
        });
    }

    private void ComposeHeader(ColumnDescriptor column)
    {
        column.Item().BorderBottom(1).BorderColor(Colors.Grey.Medium).PaddingBottom(6).Row(row =>
        {
            row.RelativeItem().Column(header =>
            {
                header.Item().Text(receipt.SchoolName.ToUpperInvariant()).Bold().FontSize(20);
                header.Item().Text($"{receipt.SchoolName} | Téléphone : {receipt.SchoolPhone ?? "—————"}")
                    .FontSize(9).FontColor(Colors.Grey.Medium);
                header.Item().PaddingTop(2).Text("[Emplacement Logo Officiel]")
                    .FontSize(9).FontColor(Colors.Grey.Medium);
            });
        });
    }

    private void ComposeInfoBlock(IContainer container)
    {
        container.Column(column =>
        {
            InfoRow(column, "Matricule", receipt.Matricule);
            InfoRow(column, "Nom complet", receipt.StudentFullName);
            InfoRow(column, "Classe d'affectation", receipt.ClassroomName);
            InfoRow(column, "Année scolaire", receipt.SchoolYearLabel);
            InfoRow(column, "Type de mouvement", TypeLabel(receipt.Type));
            InfoRow(column, "Date de l'opération", FormatDate(receipt.EnrolledAt));
        });
    }

    private static void InfoRow(ColumnDescriptor column, string label, string value)
    {
        column.Item().PaddingVertical(2).Row(row =>
        {
            row.ConstantItem(180).Text($"{label} :").FontColor(Colors.Grey.Darken2);
            row.RelativeItem().Text(value).SemiBold();
        });
    }

    private void ComposeFeesTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(3);
                columns.RelativeColumn(1);
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("Désignation des frais").Bold();
                header.Cell().Element(HeaderCell).AlignRight().Text("Montant (FCFA)").Bold();
            });

            if (receipt.Lines.Count == 0)
            {
                table.Cell().Element(BodyCell).Text("Aucun frais paramétré pour cette classe").Italic()
                    .FontColor(Colors.Grey.Darken1);
                table.Cell().Element(BodyCell).AlignRight().Text(FormatMoney(0));
            }
            else
            {
                foreach (var line in receipt.Lines)
                {
                    table.Cell().Element(BodyCell).Text(line.Designation + RecurringSuffix(line));
                    table.Cell().Element(BodyCell).AlignRight().Text(FormatMoney(line.LineTotal));
                }
            }

            table.Cell().Element(TotalCell).Text("TOTAL ENCAISSÉ").Bold();
            table.Cell().Element(TotalCell).AlignRight().Text(FormatMoney(receipt.TotalDue)).Bold();
        });

        static IContainer HeaderCell(IContainer c) =>
            c.Border(0.75f).BorderColor(Colors.Grey.Darken1).Background(Colors.Grey.Lighten3).PaddingVertical(5).PaddingHorizontal(8);
        static IContainer BodyCell(IContainer c) =>
            c.Border(0.75f).BorderColor(Colors.Grey.Darken1).PaddingVertical(5).PaddingHorizontal(8);
        static IContainer TotalCell(IContainer c) =>
            c.Border(0.75f).BorderColor(Colors.Grey.Darken1).PaddingVertical(5).PaddingHorizontal(8);
    }

    private void ComposeSignatures(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text(FaitA()).Italic();
                left.Item().PaddingTop(10).Text("[Cadre Cachet Officiel]").FontSize(9).FontColor(Colors.Grey.Medium);
            });

            row.RelativeItem().AlignRight().Text("Signature du Directeur / Service Financier").Italic();
        });
    }

    private string FaitA()
    {
        var date = FormatDate(receipt.EnrolledAt);
        return string.IsNullOrWhiteSpace(receipt.SchoolCity)
            ? $"Fait le {date}"
            : $"Fait à {receipt.SchoolCity}, le {date}";
    }

    private static string TypeLabel(string type) =>
        type == "ReEnrollment" ? "Réinscription" : "Nouvelle inscription";

    private static string RecurringSuffix(EnrollmentFeeLineDto line) =>
        line.IsRecurring ? $" (× {line.Months} mois)" : string.Empty;

    /// <summary>FCFA : entiers, séparateur de milliers par espace, sans décimales — la monnaie n'en a pas.</summary>
    private static string FormatMoney(decimal amount) =>
        amount.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ");

    private static string FormatDate(DateTimeOffset moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
