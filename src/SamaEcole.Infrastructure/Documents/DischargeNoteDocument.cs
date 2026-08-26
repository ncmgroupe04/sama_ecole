using System.Globalization;
using SamaEcole.Application.Common;
using SamaEcole.Application.Inventory;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Fiche de décharge / prêt de matériel (GET /inventory/assignments/{id}/pdf) — le papier que
/// l'élève, l'enseignant ou l'agent signe en recevant des manuels ou du matériel.
///
/// A5 PAYSAGE et charte <see cref="ReceiptTheme"/>, comme les deux autres pièces remises en main
/// propre (reçu de caisse, attestation d'inscription) : c'est le même geste et le même objet — un
/// volet qu'on remplit, qu'on signe et qu'on classe, deux par feuille A4 à l'impression. Composer
/// une troisième charte pour ce document aurait produit un troisième format de papier dans le même
/// tiroir.
///
/// COULEUR : jamais de vert. Dans <see cref="ReceiptTheme"/> le vert signifie « acquitté », et une
/// décharge n'acquitte rien — elle constate une remise et annonce un retour. L'état du prêt porte
/// donc l'indigo (le prospectif) tant qu'il est en cours, et le neutre une fois la fiche close.
/// </summary>
public class DischargeNoteDocument(DischargeNoteModel note, byte[]? logo, byte[] qrCodeImage) : IDocument
{
    private static readonly CultureInfo FrenchCulture = CultureInfo.GetCultureInfo("fr-FR");

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Décharge de matériel {note.Reference}",
        Author = note.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            // A5 paysage, marge 10 mm : même zone utile que le reçu de caisse (190 × 128 mm).
            page.Size(PageSizes.A5.Landscape());
            page.Margin(10, Unit.Millimetre);
            page.DefaultTextStyle(text => text.FontSize(7.5f).FontColor(ReceiptTheme.Ink));

            page.Content().Column(column =>
            {
                ReceiptTheme.ComposeHeader(
                    column,
                    note.SchoolName,
                    note.SchoolAddress,
                    PhoneFormatter.FormatSenegal(note.SchoolPhone),
                    email: null,
                    // Ni NINEA ni RCCM : une décharge n'est pas une pièce fiscale, ces mentions
                    // n'auraient rien à y faire (contrairement au reçu de caisse).
                    ninea: null,
                    registreCommerce: null,
                    logo,
                    ("Décharge de matériel", ReceiptTheme.BadgeStyle.Info),
                    ($"N° {NoBreakText.NoBreak(note.Reference)}", ReceiptTheme.BadgeStyle.Neutral),
                    (StatusLabel(note.Status), StatusBadgeStyle(note.Status)));

                column.Item().PaddingTop(7).Element(ComposeCartouche);
                column.Item().PaddingTop(7).Element(ComposeDeclaration);
                column.Item().PaddingTop(7).Element(ComposeDetails);

                if (!string.IsNullOrWhiteSpace(note.Notes))
                {
                    column.Item().PaddingTop(5).Element(ComposeNotes);
                }

                column.Item().PaddingTop(8).BorderTop(0.75f).BorderColor(ReceiptTheme.Rule).PaddingTop(6)
                    .Element(c => ReceiptTheme.ComposeSignatures(
                        c, $"Fait le {FormatDate(note.AssignedOn)}", "Le Bénéficiaire", "Le Responsable du matériel"));

                column.Item().PaddingTop(5).Element(ComposeAuthenticityFooter);
            });
        });
    }

    private void ComposeCartouche(IContainer container)
    {
        var cells = new List<(string, string)>
        {
            ("Bénéficiaire", note.BeneficiaryLabel),
            ("Qualité", ReceiptTheme.JoinPresent(BeneficiaryTypeLabel(note.BeneficiaryType), note.BeneficiaryDetail)),
            ("Bien remis", note.ItemName),
            ("Catégorie", note.CategoryName)
        };

        // Cellule omise quand le lot n'a pas de code d'inventaire : une étiquette sans valeur ne dit
        // rien, et beaucoup d'écoles n'immatriculent pas leurs manuels (saisie libre et facultative).
        if (!string.IsNullOrWhiteSpace(note.ItemCode))
        {
            cells.Add(("Code d'inventaire", NoBreakText.NoBreak(note.ItemCode)));
        }

        ReceiptTheme.ComposeCartouche(container, cells.ToArray());
    }

    /// <summary>
    /// Le cœur juridique de la décharge : la phrase que le bénéficiaire signe. C'est elle, et non le
    /// tableau, qui engage — d'où la formulation à la première personne.
    /// </summary>
    private void ComposeDeclaration(IContainer container)
    {
        container.BorderLeft(1.5f).BorderColor(ReceiptTheme.Rule).PaddingLeft(7).Text(text =>
        {
            text.DefaultTextStyle(style => style.FontSize(7.5f).FontColor(ReceiptTheme.InkSoft).LineHeight(1.3f));
            text.Justify();

            text.Span("Je soussigné(e) ");
            text.Span(note.BeneficiaryLabel).SemiBold().FontColor(ReceiptTheme.Ink);
            text.Span(" reconnais avoir reçu de l'établissement ");
            text.Span($"{note.Quantity.ToString("N0", FrenchCulture)} ").SemiBold().FontColor(ReceiptTheme.Ink);
            text.Span("unité(s) de ");
            text.Span(note.ItemName).SemiBold().FontColor(ReceiptTheme.Ink);
            text.Span($", en {ConditionLabel(note.Condition).ToLowerInvariant()}, ");
            text.Span("et m'engage à en prendre soin et à les restituer ");

            if (note.DueOn is { } dueOn)
            {
                text.Span($"au plus tard le {FormatDate(dueOn)}").SemiBold().FontColor(ReceiptTheme.Ink);
                text.Span(". ");
            }
            else
            {
                text.Span("à la première demande de l'administration. ");
            }

            text.Span(
                "Toute unité perdue ou détériorée pourra faire l'objet d'un remplacement ou d'un remboursement, "
                + "selon le règlement intérieur de l'établissement.");
        });
    }

    private void ComposeDetails(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Element(c => ReceiptTheme.SectionLabel(c, "Détail de la remise"));

            column.Item().PaddingTop(3).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    HeaderCell(header.Cell(), "Quantité remise");
                    HeaderCell(header.Cell(), "État à la remise");
                    HeaderCell(header.Cell(), "Date de remise");
                    HeaderCell(header.Cell(), "Retour prévu");
                });

                BodyCell(table.Cell(), note.Quantity.ToString("N0", FrenchCulture));
                BodyCell(table.Cell(), ConditionLabel(note.Condition));
                BodyCell(table.Cell(), FormatDate(note.AssignedOn));
                BodyCell(table.Cell(), note.DueOn is { } due ? FormatDate(due) : "Non fixé");
            });

            // Ligne de retour imprimée UNIQUEMENT sur une réimpression après restitution : sur le
            // volet qu'on fait signer le jour de la remise, elle serait vide et prêterait à confusion.
            if (note.ReturnedOn is { } returnedOn)
            {
                column.Item().PaddingTop(3).Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(7).FontColor(ReceiptTheme.InkSoft));
                    text.Span("Restitution constatée le ").SemiBold();
                    text.Span($"{FormatDate(returnedOn)} — ");
                    text.Span($"{note.ReturnedQuantity.ToString("N0", FrenchCulture)} unité(s) rendue(s) sur {note.Quantity.ToString("N0", FrenchCulture)}.");
                });
            }
        });
    }

    private void ComposeNotes(IContainer container) =>
        container.Text(text =>
        {
            text.DefaultTextStyle(style => style.FontSize(7).FontColor(ReceiptTheme.Muted));
            text.Span("Observations : ").SemiBold();
            text.Span(note.Notes!);
        });

    /// <summary>
    /// QR de vérification et numéro de référence, dimensionnés pour l'A5 — l'équivalent de
    /// <c>OfficialHeaderComponent.ComposeAuthenticityFooter</c>, dont la taille est calibrée pour l'A4.
    /// </summary>
    private void ComposeAuthenticityFooter(IContainer container)
    {
        container.Row(row =>
        {
            row.ConstantItem(30).Height(30).Image(qrCodeImage).FitArea();
            row.RelativeItem().PaddingLeft(6).AlignMiddle().Column(column =>
            {
                column.Item().Text($"Référence : {NoBreakText.NoBreak(note.Reference)}")
                    .FontSize(6).FontColor(ReceiptTheme.Faint);
                column.Item().Text("Document généré par Unikol — vérifiable par le QR code ci-contre")
                    .FontSize(6).FontColor(ReceiptTheme.Faint);
            });

            row.ConstantItem(150).AlignRight().AlignMiddle().Element(c => ReceiptTheme.Footnote(c,
                "Exemplaire à conserver par le bénéficiaire jusqu'à la restitution du matériel."));
        });
    }

    private static void HeaderCell(IContainer container, string label) =>
        container.Background(ReceiptTheme.HeadFill).BorderBottom(0.5f).BorderColor(ReceiptTheme.RuleStrong)
            .PaddingVertical(2.5f).PaddingHorizontal(4)
            .Text(label.ToUpperInvariant()).Bold().FontSize(6).FontColor(ReceiptTheme.Muted).LetterSpacing(0.06f);

    private static void BodyCell(IContainer container, string value) =>
        container.BorderBottom(0.25f).BorderColor(ReceiptTheme.Rule)
            .PaddingVertical(3).PaddingHorizontal(4)
            .Text(value).SemiBold().FontSize(8).FontColor(ReceiptTheme.Ink);

    private static string BeneficiaryTypeLabel(string beneficiaryType) => beneficiaryType switch
    {
        "Eleve" => "Élève",
        "Enseignant" => "Enseignant(e)",
        "Personnel" => "Personnel administratif",
        _ => beneficiaryType
    };

    private static string ConditionLabel(string condition) => condition switch
    {
        "Neuf" => "Neuf",
        "Bon" => "Bon état",
        "AReparer" => "À réparer",
        "HorsService" => "Hors service",
        _ => condition
    };

    private static string StatusLabel(string status) => status switch
    {
        "EnCours" => "Prêt en cours",
        "Restitue" => "Restitué",
        "PartiellementRestitue" => "Partiellement restitué",
        "Perdu" => "Déclaré perdu",
        _ => status
    };

    /// <summary>Indigo tant qu'un retour est attendu, neutre une fois la fiche close. Jamais de vert — voir la remarque de classe.</summary>
    private static ReceiptTheme.BadgeStyle StatusBadgeStyle(string status) => status switch
    {
        "EnCours" or "PartiellementRestitue" => ReceiptTheme.BadgeStyle.Info,
        _ => ReceiptTheme.BadgeStyle.Neutral
    };

    private static string FormatDate(DateOnly date) => date.ToString("dd/MM/yyyy", FrenchCulture);
}
