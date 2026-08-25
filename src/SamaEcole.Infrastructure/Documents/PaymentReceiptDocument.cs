using System.Globalization;
using SamaEcole.Application.Classrooms;
using SamaEcole.Application.Common;
using SamaEcole.Application.Finance;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Reçu de CAISSE officiel en PDF (ticket JGK-F02) — justificatif comptable immédiat. C'est la SEULE
/// pièce qui atteste d'un encaissement : l'attestation d'inscription
/// (<see cref="EnrollmentReceiptDocument"/>) annonce ce qu'il y a à régler, ce document constate ce qui
/// est entré en caisse. Aucun recouvrement entre les deux.
///
/// Refonte validée le 25/08/2026 (maquette « Pièces de caisse repensées », docs/design-references/
/// README.md §1bis) : A5 paysage à marge de 10 mm, en-tête à badges, cartouche d'identité, et surtout
/// une VENTILATION du versement — une ligne par poste réglé, au lieu de l'unique « Versement reçu »
/// d'avant. Palette et blocs communs dans <see cref="ReceiptTheme"/>.
///
/// Ce que le document n'imprime toujours PAS, et ne doit jamais imprimer : le dû annuel, le déjà-réglé
/// et le solde (<see cref="PaymentReceiptDto.TotalDue"/>, <see cref="PaymentReceiptDto.AlreadyPaid"/>,
/// <see cref="PaymentReceiptDto.RemainingBalance"/>). Ces champs restent au DTO pour la Finance ; un
/// reçu atteste d'un encaissement, pas d'une dette (règle comptable non négociable, AGENTS.md règle #12).
///
/// La mention obligatoire est une CONSTANTE : aucun appelant ne peut l'altérer ni l'omettre.
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
            // A5 paysage, marge 10 mm : zone utile de 190 × 128 mm, identique à la maquette validée.
            page.Size(PageSizes.A5.Landscape());
            page.Margin(10, Unit.Millimetre);
            page.DefaultTextStyle(text => text.FontSize(7.5f).FontColor(ReceiptTheme.Ink));

            page.Content().Column(column =>
            {
                ReceiptTheme.ComposeHeader(
                    column,
                    receipt.SchoolName,
                    receipt.SchoolAddress,
                    PhoneFormatter.FormatSenegal(receipt.SchoolPhone),
                    receipt.SchoolEmail,
                    receipt.SchoolNinea,
                    receipt.SchoolRegistreCommerce,
                    logo,
                    ("Payé", ReceiptTheme.BadgeStyle.Paid),
                    ($"Reçu n° {receipt.ReceiptNumber}", ReceiptTheme.BadgeStyle.Neutral),
                    (FormatDate(receipt.PaidAt), ReceiptTheme.BadgeStyle.Neutral));

                column.Item().PaddingTop(6).Element(ComposeCartouche);

                column.Item().PaddingTop(8).Element(c => ReceiptTheme.SectionLabel(
                    c, "Détail du règlement", PeriodHint()));

                column.Item().PaddingTop(4).Element(ComposeAmountsTable);

                column.Item().PaddingTop(8).AlignCenter()
                    .Text(MandatoryMention).Italic().FontSize(7).FontColor(ReceiptTheme.InkSoft);

                // Pied ancré en bas de la feuille, quel que soit le nombre de lignes ventilées.
                column.Item().PaddingTop(8).BorderTop(0.75f).BorderColor(ReceiptTheme.Rule).PaddingTop(7)
                    .Element(c => ReceiptTheme.ComposeSignatures(c, FaitA(), "Le Caissier", "Le Directeur"));

                column.Item().PaddingTop(6).Element(c => ReceiptTheme.Footnote(c,
                    $"Ce reçu atteste uniquement des sommes encaissées le {FormatDate(receipt.PaidAt)}. Il ne constitue pas un relevé de compte."));
            });
        });
    }

    private void ComposeCartouche(IContainer container) =>
        ReceiptTheme.ComposeCartouche(container,
            ("Élève", receipt.StudentFullName),
            ("Matricule", NoBreakText.NoBreak(receipt.Matricule)),
            ("Classe & année",
                $"{ClassroomPromotion.DisplayName(receipt.ClassroomName, receipt.IsAcceleratedClass)} — {receipt.SchoolYearLabel}"),
            ("Mode de règlement", MethodLabel(receipt.Method)));

    /// <summary>
    /// Précision du bandeau de section : la période couverte par le versement entier, quand elle est
    /// renseignée. Rappelée ici même si chaque ligne porte son propre libellé — le tuteur doit pouvoir
    /// rattacher le versement à un mois sans lire le tableau ligne à ligne.
    /// </summary>
    private string? PeriodHint() =>
        string.IsNullOrWhiteSpace(receipt.ReferencePeriod)
            ? null
            : $"Période de référence : {receipt.ReferencePeriod}";

    /// <summary>
    /// Tableau du règlement. Deux formes, choisies par <see cref="PaymentReceiptDto.HasBalancedLines"/> :
    ///
    ///   * VENTILÉE — une ligne par poste imputé, avec sa période ou sa note. C'est la forme normale
    ///     depuis la refonte : le tuteur voit à quoi servent les francs qu'il vient de remettre.
    ///   * UNIQUE — la ligne « Versement reçu » d'origine, servie quand la caisse n'a rien ventilé,
    ///     ou quand la ventilation saisie ne totalise pas le montant encaissé. Un tableau dont le
    ///     détail contredit le total est un faux : mieux vaut moins de détail qu'un document faux.
    /// </summary>
    private void ComposeAmountsTable(IContainer container)
    {
        if (receipt.HasBalancedLines)
        {
            ComposeVentilatedTable(container);
            return;
        }

        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.ConstantColumn(PdfColumnWidths.Amount);
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("Désignation").Bold().FontSize(6.5f)
                    .FontColor(ReceiptTheme.InkSoft).LetterSpacing(0.06f);
                header.Cell().Element(HeaderCell).AlignRight().Text("Montant (FCFA)").Bold().FontSize(6.5f)
                    .FontColor(ReceiptTheme.InkSoft).LetterSpacing(0.06f);
            });

            table.Cell().Element(BodyCell).Text("Versement reçu");
            table.Cell().Element(BodyCell).AlignRight().Text(FormatMoney(receipt.Amount));

            table.Cell().Element(TotalCell).Text("TOTAL VERSÉ").Bold().FontSize(7.5f)
                .FontColor(ReceiptTheme.InkSoft).LetterSpacing(0.04f);
            table.Cell().Element(TotalCell).AlignRight()
                .Text($"{FormatMoney(receipt.Amount)} FCFA").Bold().FontSize(10.5f).FontColor(ReceiptTheme.Paid);
        });
    }

    private void ComposeVentilatedTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(46);                     // Désignation du service
                columns.RelativeColumn(32);                     // Période / Note
                columns.ConstantColumn(PdfColumnWidths.Amount); // Montant — largeur fixe, jamais de repli
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("Désignation / Service").Bold().FontSize(6.5f)
                    .FontColor(ReceiptTheme.InkSoft).LetterSpacing(0.06f);
                header.Cell().Element(HeaderCell).Text("Période / Note").Bold().FontSize(6.5f)
                    .FontColor(ReceiptTheme.InkSoft).LetterSpacing(0.06f);
                header.Cell().Element(HeaderCell).AlignRight().Text("Montant réglé").Bold().FontSize(6.5f)
                    .FontColor(ReceiptTheme.InkSoft).LetterSpacing(0.06f);
            });

            foreach (var line in receipt.Lines)
            {
                table.Cell().Element(BodyCell).Text(line.Designation);

                // Libellé de ligne, à défaut période du versement, à défaut CELLULE VIDE — la cascade
                // est résolue par le DTO (Application), le document ne fait que la mettre en page.
                var label = receipt.ResolveLineLabel(line);
                table.Cell().Element(BodyCell).Text(label ?? string.Empty)
                    .FontSize(7.5f).FontColor(ReceiptTheme.Muted);

                table.Cell().Element(BodyCell).AlignRight().Text(FormatMoney(line.Amount));
            }

            table.Cell().Element(TotalCell).Text("TOTAL VERSÉ").Bold().FontSize(7.5f)
                .FontColor(ReceiptTheme.InkSoft).LetterSpacing(0.04f);
            table.Cell().Element(TotalCell);
            table.Cell().Element(TotalCell).AlignRight()
                .Text($"{FormatMoney(receipt.Amount)} FCFA").Bold().FontSize(10.5f).FontColor(ReceiptTheme.Paid);
        });
    }

    // Filets HORIZONTAUX seuls (maquette validée) : un tableau à grille pleine alourdit une pièce
    // qui ne compte que cinq lignes.
    private static IContainer HeaderCell(IContainer c) =>
        c.Background(ReceiptTheme.HeadFill).BorderBottom(0.5f).BorderColor(ReceiptTheme.Rule)
         .PaddingVertical(2.5f).PaddingHorizontal(6);

    private static IContainer BodyCell(IContainer c) =>
        c.BorderBottom(0.5f).BorderColor(ReceiptTheme.Rule).PaddingVertical(2.5f).PaddingHorizontal(6);

    private static IContainer TotalCell(IContainer c) =>
        c.BorderTop(1f).BorderColor(ReceiptTheme.RuleStrong).PaddingTop(4).PaddingBottom(1).PaddingHorizontal(6);

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
