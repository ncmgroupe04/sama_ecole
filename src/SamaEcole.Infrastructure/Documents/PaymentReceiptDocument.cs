using System.Globalization;
using SamaEcole.Application.Finance;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Reçu de PAIEMENT officiel en PDF (ticket JGK-F02). Même référence de design que le reçu d'inscription
/// (docs/design-references/receipt-reference.png, AGENTS.md règle #12) : document strictement
/// administratif, noir et blanc, bordures simples. Seuls le titre et le tableau des montants diffèrent —
/// ici on certifie un versement et l'on rappelle le solde figé à cet instant.
///
/// La mention obligatoire est une CONSTANTE (règle #12) : aucun appelant ne peut l'altérer ni l'omettre.
/// </summary>
public class PaymentReceiptDocument(PaymentReceiptDto receipt, byte[]? logo) : IDocument
{
    private const string MandatoryMention =
        "Il est demandé aux parents de garder minutieusement leur reçu après le paiement.";

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Reçu de paiement {receipt.ReceiptNumber}",
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
                column.Item().PaddingTop(18).AlignCenter().Text($"REÇU DE PAIEMENT n° {receipt.ReceiptNumber}")
                    .Bold().Italic().FontSize(15);
                column.Item().PaddingTop(18).Element(ComposeInfoBlock);
                column.Item().PaddingTop(14).Element(ComposeAmountsTable);
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

                if (logo is not null)
                {
                    header.Item().PaddingTop(4).MaxHeight(48).MaxWidth(170).Image(logo).FitArea();
                }
                else
                {
                    header.Item().PaddingTop(2).Text("[Emplacement Logo Officiel]")
                        .FontSize(9).FontColor(Colors.Grey.Medium);
                }
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
            InfoRow(column, "Date du paiement", FormatDate(receipt.PaidAt));
            InfoRow(column, "Moyen de paiement", MethodLabel(receipt.Method));
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

    private void ComposeAmountsTable(IContainer container)
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
                header.Cell().Element(HeaderCell).Text("Désignation").Bold();
                header.Cell().Element(HeaderCell).AlignRight().Text("Montant (FCFA)").Bold();
            });

            table.Cell().Element(BodyCell).Text("Versement reçu").Bold();
            table.Cell().Element(BodyCell).AlignRight().Text(FormatMoney(receipt.Amount)).Bold();

            table.Cell().Element(BodyCell).Text("Montant total dû");
            table.Cell().Element(BodyCell).AlignRight().Text(FormatMoney(receipt.TotalDue));

            table.Cell().Element(BodyCell).Text("Déjà réglé à ce jour");
            table.Cell().Element(BodyCell).AlignRight().Text(FormatMoney(receipt.AlreadyPaid));

            table.Cell().Element(TotalCell).Text("RESTE À PAYER").Bold();
            table.Cell().Element(TotalCell).AlignRight().Text(FormatMoney(receipt.RemainingBalance)).Bold();
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
        var date = FormatDate(receipt.PaidAt);
        return string.IsNullOrWhiteSpace(receipt.SchoolCity)
            ? $"Fait le {date}"
            : $"Fait à {receipt.SchoolCity}, le {date}";
    }

    private static string MethodLabel(string method) => method switch
    {
        "Cash" => "Espèces",
        "Cheque" => "Chèque",
        "Transfer" => "Virement",
        "MobileMoney" => "Mobile Money (Wave / Orange Money)",
        _ => method
    };

    /// <summary>FCFA : entiers, séparateur de milliers par espace, sans décimales — la monnaie n'en a pas.</summary>
    private static string FormatMoney(decimal amount) =>
        amount.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ");

    private static string FormatDate(DateTimeOffset moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
