using System.Globalization;
using SamaEcole.Application.Classrooms;
using SamaEcole.Application.Common;
using SamaEcole.Application.Finance;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Reçu de CAISSE officiel en PDF (ticket JGK-F02) — justificatif comptable immédiat, épuré à dessein :
/// il ne porte que le flux de trésorerie de l'instant t, jamais l'état du dossier de l'élève (c'est le
/// rôle de l'attestation d'inscription, <see cref="EnrollmentReceiptDocument"/>). Même référence de
/// design que celle-ci (docs/design-references/receipt-reference.png, AGENTS.md règle #12) : document
/// strictement administratif, noir et blanc, bordures simples.
///
/// Le tableau des montants ne porte QUE le versement du jour (motif + montant, puis TOTAL PAYÉ) : le
/// solde du compte (<see cref="PaymentReceiptDto.TotalDue"/>, <see cref="PaymentReceiptDto.AlreadyPaid"/>,
/// <see cref="PaymentReceiptDto.RemainingBalance"/>) reste porté par le DTO pour un usage interne
/// (Finance) mais ne figure plus sur le document remis au parent — un reçu atteste d'un encaissement,
/// pas d'une dette.
///
/// La mention obligatoire est une CONSTANTE (règle #12) : aucun appelant ne peut l'altérer ni l'omettre.
/// </summary>
public class PaymentReceiptDocument(PaymentReceiptDto receipt, byte[]? logo) : IDocument
{
    private const string MandatoryMention =
        "Il est demandé aux parents de garder minutieusement leur reçu après le paiement.";

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Reçu de caisse {receipt.ReceiptNumber}",
        Author = receipt.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A5.Landscape());
            page.Margin(8, Unit.Millimetre);
            page.DefaultTextStyle(text => text.FontSize(8).FontColor(Colors.Black));

            page.Content().Column(column =>
            {
                ComposeHeader(column);

                column.Item().PaddingTop(6).AlignCenter()
                    .Text($"REÇU DE CAISSE n° {NoBreakText.NoBreak(receipt.ReceiptNumber)}").Bold().Italic().FontSize(11);

                column.Item().PaddingTop(6).Row(row =>
                {
                    row.RelativeItem().Element(ComposeInfoBlock);
                    row.ConstantItem(14);
                    row.RelativeItem().Element(ComposeAmountsTable);
                });

                column.Item().PaddingTop(8).AlignCenter().Text(MandatoryMention).Italic().FontSize(8);
                column.Item().PaddingTop(12).Element(ComposeSignatures);
            });
        });
    }

    private void ComposeHeader(ColumnDescriptor column)
    {
        column.Item().BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingBottom(4).Row(row =>
        {
            row.RelativeItem().Column(header =>
            {
                header.Item().Text(receipt.SchoolName.ToUpperInvariant()).Bold().FontSize(13);

                var contact = JoinPresent(receipt.SchoolAddress, PhoneFormatter.FormatSenegal(receipt.SchoolPhone), receipt.SchoolEmail);
                if (contact.Length > 0)
                {
                    header.Item().Text(contact).FontSize(7).FontColor(Colors.Grey.Darken2);
                }

                var legal = JoinPresent(
                    receipt.SchoolNinea is null ? null : $"NINEA : {receipt.SchoolNinea}",
                    receipt.SchoolRegistreCommerce is null ? null : $"RCCM : {receipt.SchoolRegistreCommerce}");
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
            InfoRow(column, "Matricule", NoBreakText.NoBreak(receipt.Matricule));
            InfoRow(column, "Nom complet", receipt.StudentFullName);
            // Même mention que sur le reçu d'inscription pour une classe passerelle, rien de plus qu'avant
            // pour une classe ordinaire — voir ClassroomPromotion.DisplayName.
            InfoRow(column, "Classe d'affectation",
                ClassroomPromotion.DisplayName(receipt.ClassroomName, receipt.IsAcceleratedClass));
            InfoRow(column, "Année scolaire", receipt.SchoolYearLabel);
            InfoRow(column, "Date de règlement", FormatDate(receipt.PaidAt));
            InfoRow(column, "Mode de paiement", MethodLabel(receipt.Method));
        });
    }

    private static void InfoRow(ColumnDescriptor column, string label, string value)
    {
        column.Item().PaddingVertical(1).Row(row =>
        {
            row.ConstantItem(95).Text($"{label} :").FontColor(Colors.Grey.Darken2);
            row.RelativeItem().Text(value).SemiBold();
        });
    }

    private void ComposeAmountsTable(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();                       // Désignation — libellé libre, prend le reste
                    columns.ConstantColumn(PdfColumnWidths.Amount); // Montant — largeur fixe, jamais de repli
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("Désignation").Bold();
                    header.Cell().Element(HeaderCell).AlignRight().Text("Montant (FCFA)").Bold();
                });

                table.Cell().Element(BodyCell).Text("Versement reçu");
                table.Cell().Element(BodyCell).AlignRight().Text(FormatMoney(receipt.Amount));

                table.Cell().Element(TotalCell).Text("TOTAL PAYÉ").Bold();
                table.Cell().Element(TotalCell).AlignRight().Text(FormatMoney(receipt.Amount)).Bold();
            });
        });

        static IContainer HeaderCell(IContainer c) =>
            c.Border(0.75f).BorderColor(Colors.Grey.Darken1).Background(Colors.Grey.Lighten3).PaddingVertical(3).PaddingHorizontal(5);
        static IContainer BodyCell(IContainer c) =>
            c.Border(0.75f).BorderColor(Colors.Grey.Darken1).PaddingVertical(3).PaddingHorizontal(5);
        static IContainer TotalCell(IContainer c) =>
            c.Border(0.75f).BorderColor(Colors.Grey.Darken1).PaddingVertical(3).PaddingHorizontal(5);
    }

    private void ComposeSignatures(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text(FaitA()).Italic();
                left.Item().PaddingTop(6).Text("[Cadre Cachet Officiel]").FontSize(7).FontColor(Colors.Grey.Medium);
            });

            row.RelativeItem().AlignRight().Text("Signature du Caissier / Agent").Italic();
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

    private static string JoinPresent(params string?[] parts) =>
        string.Join("  ·  ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    /// <summary>FCFA : entiers, séparateur de milliers par espace, sans décimales — la monnaie n'en a pas.</summary>
    private static string FormatMoney(decimal amount) =>
        amount.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ");

    private static string FormatDate(DateTimeOffset moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
