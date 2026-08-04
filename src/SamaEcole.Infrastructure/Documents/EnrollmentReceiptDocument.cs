using System.Globalization;
using SamaEcole.Application.Classrooms;
using SamaEcole.Application.Common;
using SamaEcole.Application.Enrollments;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Attestation d'inscription &amp; d'admission officielle en PDF (ticket JGK-E02). Suit la référence de
/// design docs/design-references/README.md §1 (AGENTS.md règle #12) : document strictement
/// administratif, noir et blanc, bordures simples, aucune couleur — A5 PAYSAGE tenant sur une seule
/// page, en-tête légal (NINEA/RCCM), déclaration officielle, puis un bloc unique (pas de colonnes)
/// aligné dessous : identité élève, tuteur, engagement financier global de l'année.
///
/// Document pédagogique et administratif, pas une pièce comptable : il atteste d'une INSCRIPTION, pas
/// d'un encaissement. La ventilation détaillée de ce qui est réellement entré en caisse (et la mention
/// obligatoire qui l'accompagne) vit désormais sur le reçu de caisse (<see cref="PaymentReceiptDocument"/>,
/// AGENTS.md règle #12).
///
/// Deux écarts assumés vis-à-vis des pixels de la maquette d'origine, qui n'est qu'un gabarit
/// illustratif :
///   * les montants suivent la convention sénégalaise (séparateur de milliers par espace, sans
///     décimales), et non le « 25,000 » anglophone de la capture ;
///   * les dates sont au format jj/MM/aaaa (convention de l'application, SchoolSettings.DateFormat),
///     et non l'ISO de la capture.
/// </summary>
public class EnrollmentReceiptDocument(EnrollmentReceiptDto receipt, byte[]? logo) : IDocument
{
    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Attestation d'inscription {receipt.ReceiptNumber}",
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
                    .Text($"ATTESTATION D'INSCRIPTION & D'ADMISSION n° {NoBreakText.NoBreak(receipt.ReceiptNumber)}")
                    .Bold().Italic().FontSize(11);

                column.Item().PaddingTop(8).Element(ComposeDeclaration);

                column.Item().PaddingTop(8).Element(ComposeBody);

                column.Item().PaddingTop(16).Element(ComposeSignatures);
            });
        });
    }

    /// <summary>
    /// Le cœur de l'attestation : la phrase que le tuteur présente comme preuve d'inscription (visa,
    /// bourse, changement d'établissement…). Année scolaire et classe y figurent déjà en toutes lettres,
    /// donc pas répétées dans la grille d'informations qui suit.
    /// </summary>
    private void ComposeDeclaration(IContainer container)
    {
        container.Background(Colors.Grey.Lighten4).Border(0.75f).BorderColor(Colors.Grey.Darken1)
            .Padding(8).Text(text =>
        {
            text.DefaultTextStyle(style => style.FontSize(9));
            text.Justify();
            text.Span("L'administration de l'établissement atteste que l'élève ");
            text.Span(receipt.StudentFullName).SemiBold();
            text.Span(" (Matricule : ");
            text.Span(NoBreakText.NoBreak(receipt.Matricule)).SemiBold();
            text.Span(") est régulièrement inscrit(e) au sein de notre établissement en classe de ");
            text.Span(ClassroomPromotion.DisplayName(receipt.ClassroomName, receipt.IsAcceleratedClass)).SemiBold();
            text.Span(" pour l'année scolaire ");
            text.Span(receipt.SchoolYearLabel).SemiBold();
            text.Span(".");
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

    /// <summary>
    /// Bloc unique, aéré, sous la déclaration : identité de l'élève, tuteur, puis engagement financier
    /// global de l'année — jamais la ventilation du jour, qui est désormais réservée au reçu de caisse
    /// (<see cref="PaymentReceiptDocument"/>). Une attestation ne prouve pas un encaissement.
    /// </summary>
    private void ComposeBody(IContainer container)
    {
        container.Padding(4).Column(column =>
        {
            InfoRow(column, "Matricule", NoBreakText.NoBreak(receipt.Matricule));
            InfoRow(column, "Nom complet", receipt.StudentFullName);
            // Classe passerelle / accélérée : le nom porte la mention du dispositif, parce que c'est SUR
            // CE PAPIER que le tuteur constate que l'année qu'il règle en couvre deux niveaux. Classe
            // ordinaire : la ligne est celle d'origine, au caractère près (AGENTS.md règle #12).
            InfoRow(column, "Classe & Niveau",
                $"{ClassroomPromotion.DisplayName(receipt.ClassroomName, receipt.IsAcceleratedClass)} — {receipt.ClassroomLevel}");

            if (!string.IsNullOrWhiteSpace(receipt.GuardianName) || !string.IsNullOrWhiteSpace(receipt.GuardianPhone))
            {
                var phone = PhoneFormatter.FormatSenegal(receipt.GuardianPhone);
                InfoRow(column, "Tuteur & Contact",
                    (receipt.GuardianName ?? "—") + (phone is null ? string.Empty : $" ({phone})"));
            }

            InfoRow(column, "Frais d'inscription annuels engagés", $"{FormatMoney(receipt.TotalDue)} FCFA");
            InfoRow(column, "Reste à payer global sur l'année", $"{FormatMoney(receipt.RemainingBalance)} FCFA", bold: true);
        });
    }

    private static void InfoRow(ColumnDescriptor column, string label, string value, bool bold = false)
    {
        column.Item().PaddingVertical(1.5f).Row(row =>
        {
            row.ConstantItem(160).Text($"{label} :").FontColor(Colors.Grey.Darken2);
            var valueText = row.RelativeItem().AlignRight().Text(value);
            if (bold)
            {
                valueText.Bold();
            }
            else
            {
                valueText.SemiBold();
            }
        });
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

            row.RelativeItem().AlignRight().Text("Signature du Directeur").Italic();
        });
    }

    private string FaitA()
    {
        var date = FormatDate(receipt.EnrolledAt);
        return string.IsNullOrWhiteSpace(receipt.SchoolCity)
            ? $"Fait le {date}"
            : $"Fait à {receipt.SchoolCity}, le {date}";
    }

    private static string JoinPresent(params string?[] parts) =>
        string.Join("  ·  ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    /// <summary>FCFA : entiers, séparateur de milliers par espace, sans décimales — la monnaie n'en a pas.</summary>
    private static string FormatMoney(decimal amount) =>
        amount.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ");

    private static string FormatDate(DateTimeOffset moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
