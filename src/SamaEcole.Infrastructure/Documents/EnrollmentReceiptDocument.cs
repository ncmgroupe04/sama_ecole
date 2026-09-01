using System.Globalization;
using SamaEcole.Application.Classrooms;
using SamaEcole.Application.Common;
using SamaEcole.Application.Enrollments;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Attestation d'inscription &amp; d'admission officielle en PDF (ticket JGK-E02). Pièce ADMINISTRATIVE
/// délivrée par le SECRÉTARIAT, qui n'encaisse aucun fonds : elle atteste d'une inscription et annonce
/// ce qu'il y a à régler auprès de la comptabilité. Le constat d'encaissement, lui, appartient
/// exclusivement au reçu de caisse (<see cref="PaymentReceiptDocument"/>), seule pièce comptable.
///
/// Refonte validée le 25/08/2026 (maquette « Pièces de caisse repensées », docs/design-references/
/// README.md §1). Ce qui a changé, et pourquoi :
///
///   * Le tableau du CUMUL ANNUEL a disparu. « Mensualité (× 9 mois) = 135 000 », puis un total de
///     271 000, effrayaient le tuteur sans l'informer. Deux blocs le remplacent :
///       — « Règlement à effectuer » : l'engagement initial
///         (<see cref="EnrollmentReceiptDto.InitialSettlementTotal"/>), soit le frais ponctuel entier
///         ou UNE mensualité par ligne récurrente ;
///       — « Échéancier mensuel » : les tarifs mensuels UNITAIRES et leur total
///         (<see cref="EnrollmentReceiptDto.MonthlyTotal"/>), jamais multipliés.
///   * <c>TotalDue</c> et <c>RemainingBalance</c> ne sont PLUS imprimés. Ils restent au DTO pour la
///     Finance ; le tuteur, lui, ne lit plus le cumul annuel sur son papier.
///   * <c>CollectedLines</c> / <c>TotalCollected</c> ne sont plus lus du tout : le secrétariat
///     n'encaisse pas, ils valent zéro à l'instant où ce document est délivré.
///
/// COULEUR : aucune touche de vert nulle part. Le vert signifie « acquitté » dans
/// <see cref="ReceiptTheme"/>, et cette pièce n'acquitte rien. Le total à régler reste à l'encre, et
/// seul l'échéancier — prospectif — porte l'indigo.
///
/// Aucun badge d'état de paiement non plus : réimprimée trois mois plus tard, l'attestation
/// afficherait un état faux. Le badge ne qualifie que l'acte administratif, vrai à toute date.
/// </summary>
public class EnrollmentReceiptDocument(EnrollmentReceiptDto receipt, byte[]? logo) : IDocument
{
    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Attestation d'inscription — {ReceiptReference()}",
        Author = receipt.SchoolName
    };

    /// <summary>
    /// Référence affichée sur le badge « N° … » et dans le titre du document. Sur une inscription
    /// « envoyée en Caisse » (paiement différé), AUCUN numéro de reçu officiel n'a été consommé
    /// (AGENTS.md règle #3 — le registre gapless est réservé à un mouvement d'argent réel) : la
    /// colonne porte un jeton provisoire <c>EN-ATTENTE-{guid}</c>. On l'affiche alors
    /// « en attente de règlement », comme l'écran (<c>receiptReference()</c> dans enrollments.js) ;
    /// le numéro officiel apparaît au premier encaissement, à la Caisse.
    /// </summary>
    private string ReceiptReference()
    {
        var pending =
            (receipt.ReceiptNumber?.StartsWith("EN-ATTENTE-", StringComparison.OrdinalIgnoreCase) ?? false)
            || string.Equals(receipt.Status, "PendingPayment", StringComparison.OrdinalIgnoreCase);

        return pending ? "en attente de règlement" : receipt.ReceiptNumber;
    }

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
                    ("Inscription enregistrée", ReceiptTheme.BadgeStyle.Info),
                    ($"N° {ReceiptReference()}", ReceiptTheme.BadgeStyle.Neutral),
                    ($"Année {receipt.SchoolYearLabel}", ReceiptTheme.BadgeStyle.Neutral));

                column.Item().PaddingTop(7).Element(ComposeCartouche);
                column.Item().PaddingTop(7).Element(ComposeDeclaration);

                column.Item().PaddingTop(6).Row(row =>
                {
                    row.RelativeItem().Element(ComposeSettlementBlock);
                    row.ConstantItem(16);
                    row.RelativeItem().Element(ComposeMonthlyPlanBlock);
                });

                column.Item().PaddingTop(9).BorderTop(0.75f).BorderColor(ReceiptTheme.Rule).PaddingTop(7)
                    .Element(c => ReceiptTheme.ComposeSignatures(c, FaitA(), "Le Secrétariat", "Le Directeur"));

                column.Item().PaddingTop(6).Element(c => ReceiptTheme.Footnote(c,
                    "Le secrétariat n'encaisse aucun fonds : tout règlement s'effectue auprès de la comptabilité, "
                    + "seule habilitée à délivrer un reçu de caisse valant validation définitive."));
            });
        });
    }

    private void ComposeCartouche(IContainer container)
    {
        var cells = new List<(string, string)>
        {
            ("Élève", receipt.StudentFullName),
            ("Matricule", NoBreakText.NoBreak(receipt.Matricule)),
            // Classe passerelle / accélérée : le nom porte la mention du dispositif, parce que c'est SUR
            // CE PAPIER que le tuteur constate que l'année qu'il règle en couvre deux niveaux.
            ("Classe & niveau",
                $"{ClassroomPromotion.DisplayName(receipt.ClassroomName, receipt.IsAcceleratedClass)} — {receipt.ClassroomLevel}")
        };

        // Cellule omise quand aucun tuteur n'est renseigné : une étiquette sans valeur ne dit rien.
        if (!string.IsNullOrWhiteSpace(receipt.GuardianName) || !string.IsNullOrWhiteSpace(receipt.GuardianPhone))
        {
            var phone = PhoneFormatter.FormatSenegal(receipt.GuardianPhone);
            cells.Add(("Tuteur", ReceiptTheme.JoinPresent(receipt.GuardianName, phone)));
        }

        ReceiptTheme.ComposeCartouche(container, cells.ToArray());
    }

    /// <summary>
    /// Le cœur juridique de l'attestation : la phrase que le tuteur présente comme preuve d'inscription
    /// (visa, bourse, changement d'établissement…).
    /// </summary>
    private void ComposeDeclaration(IContainer container)
    {
        container.BorderLeft(1.5f).BorderColor(ReceiptTheme.Rule).PaddingLeft(7).Text(text =>
        {
            text.DefaultTextStyle(style => style.FontSize(7.5f).FontColor(ReceiptTheme.InkSoft).LineHeight(1.3f));
            text.Justify();
            text.Span("L'administration de l'établissement atteste que l'élève ");
            text.Span(receipt.StudentFullName).SemiBold().FontColor(ReceiptTheme.Ink);
            text.Span(" (matricule ");
            text.Span(NoBreakText.NoBreak(receipt.Matricule)).SemiBold().FontColor(ReceiptTheme.Ink);
            text.Span(") est régulièrement inscrit(e) au sein de notre établissement en classe de ");
            text.Span(ClassroomPromotion.DisplayName(receipt.ClassroomName, receipt.IsAcceleratedClass))
                .SemiBold().FontColor(ReceiptTheme.Ink);
            text.Span(" pour l'année scolaire ");
            text.Span(receipt.SchoolYearLabel).SemiBold().FontColor(ReceiptTheme.Ink);
            text.Span(".");
        });
    }

    /// <summary>
    /// Bloc 1 — ce qu'il y a à régler MAINTENANT, auprès de la comptabilité. Montants unitaires : le
    /// frais ponctuel entier, une seule mensualité pour une ligne récurrente. Total à l'ENCRE, jamais
    /// en vert : rien n'est acquitté au moment où le secrétariat remet cette feuille.
    /// </summary>
    private void ComposeSettlementBlock(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Element(c => ReceiptTheme.SectionLabel(
                c, "Règlement à effectuer", "auprès du service de la comptabilité, ce jour"));

            column.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.ConstantColumn(PdfColumnWidths.Amount);
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("Poste à régler").Bold().FontSize(6.5f)
                        .FontColor(ReceiptTheme.InkSoft).LetterSpacing(0.06f);
                    header.Cell().Element(HeaderCell).AlignRight().Text("Montant").Bold().FontSize(6.5f)
                        .FontColor(ReceiptTheme.InkSoft).LetterSpacing(0.06f);
                });

                if (receipt.Lines.Count == 0)
                {
                    table.Cell().Element(BodyCell).Text("Aucun frais engagé")
                        .Italic().FontColor(ReceiptTheme.Muted);
                    table.Cell().Element(BodyCell).AlignRight().Text(FormatMoney(0));
                }
                else
                {
                    foreach (var line in receipt.Lines)
                    {
                        table.Cell().Element(BodyCell).Text(text =>
                        {
                            text.Span(line.Designation);
                            // « (1 mois) » : le tuteur doit voir qu'il ne règle QU'UN mois, pas l'année.
                            if (line.IsRecurring)
                            {
                                text.Span("  (1 mois)").FontSize(7).FontColor(ReceiptTheme.Muted);
                            }
                        });
                        table.Cell().Element(BodyCell).AlignRight().Text(FormatMoney(line.UnitAmount));
                    }
                }

                table.Cell().Element(TotalCell).Text("TOTAL À RÉGLER").Bold().FontSize(7.5f)
                    .FontColor(ReceiptTheme.InkSoft).LetterSpacing(0.04f);
                table.Cell().Element(TotalCell).AlignRight()
                    .Text($"{FormatMoney(receipt.InitialSettlementTotal)} FCFA")
                    .Bold().FontSize(10.5f).FontColor(ReceiptTheme.Ink);
            });
        });
    }

    /// <summary>
    /// Bloc 2 — l'échéancier, prospectif et indicatif. Tarifs UNITAIRES seuls : c'est la multiplication
    /// par le nombre de mois que la refonte supprime. Le total mensuel est le chiffre le plus lisible de
    /// la page, à la place du cumul annuel qu'il remplace.
    /// </summary>
    private void ComposeMonthlyPlanBlock(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Element(c => ReceiptTheme.SectionLabel(c, "Échéancier mensuel", "à titre indicatif"));

            column.Item().PaddingTop(4).Column(list =>
            {
                if (receipt.MonthlyLines.Count == 0)
                {
                    list.Item().Text("Aucun frais mensuel souscrit.").Italic().FontColor(ReceiptTheme.Muted);
                    return;
                }

                foreach (var line in receipt.MonthlyLines)
                {
                    list.Item().BorderBottom(0.5f).BorderColor(ReceiptTheme.Rule)
                        .PaddingVertical(2.5f).Row(row =>
                    {
                        row.RelativeItem().AlignMiddle().Text(line.Designation);
                        row.ConstantItem(88).AlignRight().AlignMiddle().Text(text =>
                        {
                            text.Span(FormatMoney(line.UnitAmount)).SemiBold().FontSize(8.5f);
                            text.Span(" FCFA / mois").FontSize(6.5f).FontColor(ReceiptTheme.Muted);
                        });
                    });
                }
            });

            column.Item().PaddingTop(5).Background(ReceiptTheme.PlanFill)
                .Border(0.5f).BorderColor(ReceiptTheme.PlanRule).Padding(5).Row(row =>
            {
                row.RelativeItem().AlignMiddle().Column(label =>
                {
                    label.Item().Text("TOTAL À PRÉVOIR CHAQUE MOIS")
                        .Bold().FontSize(6.5f).FontColor(ReceiptTheme.Plan).LetterSpacing(0.07f);
                    label.Item().PaddingTop(1).Text("Règlement du 1er au 5 de chaque mois")
                        .FontSize(6).FontColor(ReceiptTheme.PlanAccent);
                });

                row.ConstantItem(96).AlignRight().AlignMiddle().Text(text =>
                {
                    text.Span(FormatMoney(receipt.MonthlyTotal)).Bold().FontSize(13).FontColor(ReceiptTheme.Plan);
                    text.Span(" FCFA / mois").FontSize(7).FontColor(ReceiptTheme.PlanAccent);
                });
            });
        });
    }

    private static IContainer HeaderCell(IContainer c) =>
        c.Background(ReceiptTheme.HeadFill).BorderBottom(0.5f).BorderColor(ReceiptTheme.Rule)
         .PaddingVertical(2.5f).PaddingHorizontal(6);

    private static IContainer BodyCell(IContainer c) =>
        c.BorderBottom(0.5f).BorderColor(ReceiptTheme.Rule).PaddingVertical(2.5f).PaddingHorizontal(6);

    private static IContainer TotalCell(IContainer c) =>
        c.BorderTop(1f).BorderColor(ReceiptTheme.RuleStrong).PaddingTop(4).PaddingBottom(1).PaddingHorizontal(6);

    private string FaitA()
    {
        var date = FormatDate(receipt.EnrolledAt);
        return string.IsNullOrWhiteSpace(receipt.SchoolCity)
            ? $"Fait le {date}"
            : $"Fait à {receipt.SchoolCity}, le {date}";
    }

    /// <summary>FCFA : entiers, séparateur de milliers par espace, sans décimales — la monnaie n'en a pas.</summary>
    private static string FormatMoney(decimal amount) =>
        amount.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ");

    private static string FormatDate(DateTimeOffset moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
