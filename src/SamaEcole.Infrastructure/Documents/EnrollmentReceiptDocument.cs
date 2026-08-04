using System.Globalization;
using SamaEcole.Application.Common;
using SamaEcole.Application.Enrollments;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Reçu d'inscription officiel en PDF (ticket JGK-E02). Suit la référence de design
/// docs/design-references/README.md §1 (AGENTS.md règle #12) : document strictement administratif,
/// noir et blanc, bordures simples, aucune couleur — et depuis la refonte, A5 PAYSAGE tenant sur une
/// seule page, en-tête légal (NINEA/RCCM) puis corps en deux colonnes (identité à gauche, ventilation
/// de l'encaissement à droite).
///
/// Ce document n'imprime QUE ce qui est réellement entré en caisse le jour de l'inscription
/// (<see cref="EnrollmentReceiptDto.CollectedLines"/>) : un reçu atteste d'un encaissement, jamais
/// d'une dette. Le dû annuel et le reste à payer ne figurent qu'en rappel, sous le total.
///
/// Deux écarts assumés vis-à-vis des pixels de la maquette d'origine, qui n'est qu'un gabarit
/// illustratif :
///   * les montants suivent la convention sénégalaise (séparateur de milliers par espace, sans
///     décimales), et non le « 25,000 » anglophone de la capture ;
///   * les dates sont au format jj/MM/aaaa (convention de l'application, SchoolSettings.DateFormat),
///     et non l'ISO de la capture.
///
/// La mention obligatoire est une CONSTANTE ici (AGENTS.md règle #12) : aucun appelant ne peut
/// l'altérer ni l'omettre.
/// </summary>
public class EnrollmentReceiptDocument(EnrollmentReceiptDto receipt, byte[]? logo) : IDocument
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
            // A5 paysage : le format d'un reçu de caisse d'école, deux par feuille A4 à l'impression.
            page.Size(PageSizes.A5.Landscape());
            page.Margin(8, Unit.Millimetre);
            page.DefaultTextStyle(text => text.FontSize(8).FontColor(Colors.Black));

            page.Content().Column(column =>
            {
                ComposeHeader(column);

                column.Item().PaddingTop(6).AlignCenter()
                    .Text($"REÇU D'INSCRIPTION n° {NoBreakText.NoBreak(receipt.ReceiptNumber)}").Bold().Italic().FontSize(11);

                // Corps en deux colonnes, l'écart central évitant que les deux blocs ne se touchent.
                column.Item().PaddingTop(6).Row(row =>
                {
                    row.RelativeItem().Element(ComposeInfoBlock);
                    row.ConstantItem(14);
                    row.RelativeItem().Element(ComposeCollectedTable);
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

                // Coordonnées puis mentions légales : chaque ligne n'affiche que ce qui est renseigné,
                // sans séparateur orphelin ni « — » de remplissage.
                var contact = JoinPresent(receipt.SchoolAddress, receipt.SchoolPhone, receipt.SchoolEmail);
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

            // Logo officiel à droite de l'en-tête. Faute de logo lisible, on garde le libellé témoin
            // plutôt qu'un trou — le générateur ne nous passe que des octets déjà validés.
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
            InfoRow(column, "Classe d'affectation", $"{receipt.ClassroomName} — {receipt.ClassroomLevel}");
            InfoRow(column, "Année scolaire", receipt.SchoolYearLabel);
            InfoRow(column, "Type de mouvement", TypeLabel(receipt.Type));
            InfoRow(column, "Date de l'opération", FormatDate(receipt.EnrolledAt));

            if (!string.IsNullOrWhiteSpace(receipt.GuardianName))
            {
                InfoRow(column, "Tuteur", receipt.GuardianName);
            }

            if (!string.IsNullOrWhiteSpace(receipt.GuardianPhone))
            {
                InfoRow(column, "Téléphone du tuteur", PhoneFormatter.FormatSenegal(receipt.GuardianPhone)!);
            }

            InfoRow(column, "Mode de règlement", PaymentMethodLabel(receipt.PaymentMethod));
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

    /// <summary>
    /// Ventilation de l'encaissement du jour : une ligne par frais réglé, puis le TOTAL ENCAISSÉ. Le
    /// dû annuel et le reste à payer sont relégués sous le tableau, en petit — ils informent le tuteur
    /// sans jamais pouvoir être lus comme le montant versé.
    /// </summary>
    private void ComposeCollectedTable(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3);
                    columns.RelativeColumn(2);
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("Désignation des frais").Bold();
                    header.Cell().Element(HeaderCell).AlignRight().Text("Montant (FCFA)").Bold();
                });

                if (receipt.CollectedLines.Count == 0)
                {
                    table.Cell().Element(BodyCell).Text("Aucun frais encaissé ce jour").Italic()
                        .FontColor(Colors.Grey.Darken1);
                    table.Cell().Element(BodyCell).AlignRight().Text(FormatMoney(0));
                }
                else
                {
                    foreach (var line in receipt.CollectedLines)
                    {
                        table.Cell().Element(BodyCell).Text(CollectedLabel(line));
                        table.Cell().Element(BodyCell).AlignRight().Text(FormatMoney(line.Amount));
                    }
                }

                table.Cell().Element(TotalCell).Text("TOTAL ENCAISSÉ").Bold();
                table.Cell().Element(TotalCell).AlignRight().Text(FormatMoney(receipt.TotalCollected)).Bold();
            });

            column.Item().PaddingTop(3).Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(7).FontColor(Colors.Grey.Darken2));
                text.Span($"Frais annuels : {FormatMoney(receipt.TotalDue)}  ·  Reste à payer : ");
                text.Span(FormatMoney(receipt.RemainingBalance)).SemiBold();
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

    /// <summary>
    /// Une mensualité porte le nombre de mois RÉELLEMENT réglés (« Mensualité (× 1 mois) ») : c'est
    /// vérifiable et jamais faux, là où nommer le mois couvert (« Mensualité d'octobre ») supposerait
    /// un échéancier que l'application ne tient pas encore.
    /// </summary>
    private static string CollectedLabel(CollectedFeeLineDto line) =>
        line.IsRecurring ? $"{line.Designation} (× {line.Months} mois)" : line.Designation;

    /// <summary>Null = aucun versement ce jour-là : on l'écrit « — » plutôt que d'inventer un mode.</summary>
    private static string PaymentMethodLabel(string? method) => method switch
    {
        "Cash" => "Espèces",
        "Cheque" => "Chèque",
        "Transfer" => "Virement",
        "MobileMoney" => "Mobile Money",
        _ => "—"
    };

    private static string JoinPresent(params string?[] parts) =>
        string.Join("  ·  ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    /// <summary>FCFA : entiers, séparateur de milliers par espace, sans décimales — la monnaie n'en a pas.</summary>
    private static string FormatMoney(decimal amount) =>
        amount.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ");

    private static string FormatDate(DateTimeOffset moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
