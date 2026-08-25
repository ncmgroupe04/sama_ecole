using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Palette et blocs communs des deux pièces A5 remises au tuteur — le reçu de caisse
/// (<see cref="PaymentReceiptDocument"/>) et l'attestation d'inscription
/// (<see cref="EnrollmentReceiptDocument"/>). Refonte validée le 25/08/2026 : les documents ne sont
/// plus en noir et blanc strict, ils portent une couleur SÉMANTIQUE et rien d'autre
/// (docs/design-references/README.md §1 et §1bis, mis à jour en conséquence).
///
/// Trois familles, jamais mélangées :
///   * neutres — encre, filets, fonds de tableau ;
///   * <see cref="Paid"/> (vert) — EXCLUSIVEMENT ce qui est acquitté. Un montant dû ne porte
///     jamais cette couleur : sur l'attestation, le vert dirait « payé » alors que le secrétariat
///     n'encaisse rien ;
///   * <see cref="Plan"/> (indigo) — le prospectif : tarifs mensuels à venir, état administratif.
///
/// Source de vérité unique des valeurs : régler une couleur « à l'œil » dans un document isolé
/// réintroduit la dérive que ce fichier supprime (même principe que <see cref="PdfColumnWidths"/>).
///
/// POLICE : la maquette HTML est composée en Inter, absente du pipeline PDF — aucune police n'est
/// enregistrée auprès de QuestPDF. Les documents utilisent donc sa police par défaut (Lato), une
/// grotesque humaniste de proportions voisines. Passer réellement à Inter suppose d'embarquer le
/// fichier de police et de l'enregistrer au démarrage ; ce n'est pas fait ici.
/// </summary>
internal static class ReceiptTheme
{
    // ----------------------------------------------------------------- neutres --
    public static readonly Color Ink        = Color.FromHex("#111827");
    public static readonly Color InkSoft    = Color.FromHex("#374151");
    public static readonly Color Muted      = Color.FromHex("#6B7280");
    public static readonly Color Faint      = Color.FromHex("#9CA3AF");
    public static readonly Color Rule       = Color.FromHex("#E5E7EB");
    public static readonly Color RuleStrong = Color.FromHex("#D1D5DB");
    public static readonly Color HeadFill   = Color.FromHex("#F3F4F6");
    public static readonly Color PanelFill  = Color.FromHex("#F9FAFB");

    // ------------------------------------------------- acquitté (vert) — encaissé --
    public static readonly Color Paid     = Color.FromHex("#1E8E3E");
    public static readonly Color PaidFill = Color.FromHex("#EAF7EE");
    public static readonly Color PaidRule = Color.FromHex("#BFE3CB");

    // ------------------------------------------ prospectif (indigo) — à venir / état --
    public static readonly Color Plan       = Color.FromHex("#4338CA");
    public static readonly Color PlanAccent = Color.FromHex("#6366F1");
    public static readonly Color PlanFill   = Color.FromHex("#EEF2FF");
    public static readonly Color PlanRule   = Color.FromHex("#C7D2FE");

    /// <summary>Style d'un badge d'en-tête. <see cref="Paid"/> est réservé à un encaissement effectif.</summary>
    public enum BadgeStyle { Neutral, Paid, Info }

    /// <summary>
    /// En-tête commun : logo optionnel, identité de l'établissement à gauche, badges empilés à droite,
    /// filet de séparation. Chaque mention absente est simplement OMISE — jamais de séparateur
    /// orphelin ni de valeur inventée (convention posée par la référence de design §1.1).
    /// </summary>
    public static void ComposeHeader(
        ColumnDescriptor column,
        string schoolName,
        string? address,
        string? phone,
        string? email,
        string? ninea,
        string? registreCommerce,
        byte[]? logo,
        params (string Text, BadgeStyle Style)[] badges)
    {
        column.Item().BorderBottom(0.75f).BorderColor(Rule).PaddingBottom(6).Row(row =>
        {
            // Logo à GAUCHE du nom, discret : l'angle droit est occupé par les badges. Absent, on
            // n'imprime aucun cadre témoin — un en-tête propre vaut mieux qu'un trou légendé.
            if (logo is not null)
            {
                row.ConstantItem(30).MaxHeight(30).AlignMiddle().Image(logo).FitArea();
                row.ConstantItem(10);
            }

            row.RelativeItem().Column(identity =>
            {
                identity.Item().Text(schoolName).Bold().FontSize(12.5f).FontColor(Ink);

                var contact = JoinPresent(address, phone, email);
                if (contact.Length > 0)
                {
                    identity.Item().PaddingTop(2).Text(contact).FontSize(6.5f).FontColor(Muted);
                }

                var legal = JoinPresent(
                    ninea is null ? null : $"NINEA {ninea}",
                    registreCommerce is null ? null : $"RCCM {registreCommerce}");
                if (legal.Length > 0)
                {
                    identity.Item().Text(legal).FontSize(6.5f).FontColor(Muted);
                }
            });

            row.ConstantItem(12);
            row.ConstantItem(120).Column(stack =>
            {
                foreach (var (text, style) in badges)
                {
                    stack.Item().PaddingBottom(2.5f).AlignRight().Element(c => Badge(c, text, style));
                }
            });
        });
    }

    /// <summary>
    /// Pastille d'en-tête. Coins droits : <c>CornerRadius</c> n'existe pas dans QuestPDF 2024.10.3,
    /// contrairement à la maquette HTML — seul écart assumé vis-à-vis d'elle.
    /// </summary>
    private static void Badge(IContainer container, string text, BadgeStyle style)
    {
        var (fill, border, ink) = style switch
        {
            BadgeStyle.Paid => (PaidFill, PaidRule, Paid),
            BadgeStyle.Info => (PlanFill, PlanRule, Plan),
            _ => (HeadFill, Rule, InkSoft)
        };

        container
            .Background(fill).Border(0.5f).BorderColor(border)
            .PaddingVertical(2).PaddingHorizontal(5)
            .Text(text.ToUpperInvariant()).Bold().FontSize(6.5f).FontColor(ink).LetterSpacing(0.06f);
    }

    /// <summary>
    /// Cartouche d'identité : panneau gris clair, grille de 2 colonnes, étiquette capitale au-dessus
    /// de sa valeur. Les cellules sont fournies dans l'ordre de lecture (gauche, droite, gauche…).
    /// </summary>
    public static void ComposeCartouche(IContainer container, params (string Label, string Value)[] cells)
    {
        container.Background(PanelFill).Border(0.5f).BorderColor(Rule).Padding(5).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.RelativeColumn();
            });

            foreach (var (label, value) in cells)
            {
                table.Cell().PaddingVertical(1.5f).PaddingRight(10).Column(cell =>
                {
                    cell.Item().Text(label.ToUpperInvariant())
                        .Bold().FontSize(6).FontColor(Muted).LetterSpacing(0.08f);
                    cell.Item().Text(value).SemiBold().FontSize(8).FontColor(Ink);
                });
            }
        });
    }

    /// <summary>Intitulé de section : libellé capitale, précision facultative en gris clair.</summary>
    public static void SectionLabel(IContainer container, string label, string? hint = null)
    {
        container.Text(text =>
        {
            text.Span(label.ToUpperInvariant()).Bold().FontSize(6.5f).FontColor(Muted).LetterSpacing(0.1f);

            if (!string.IsNullOrWhiteSpace(hint))
            {
                text.Span($"   {hint}").FontSize(6.5f).FontColor(Faint);
            }
        });
    }

    /// <summary>
    /// Pied de page : « Fait à … » et cachet à gauche, puis deux traits de signature. Les rôles
    /// diffèrent d'une pièce à l'autre — le secrétariat délivre l'attestation, la caisse encaisse.
    /// </summary>
    public static void ComposeSignatures(IContainer container, string faitA, string leftRole, string rightRole)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text(faitA).Italic().FontSize(7).FontColor(Muted);
                left.Item().PaddingTop(10).Text("Cachet officiel")
                    .Bold().FontSize(6.5f).FontColor(Faint).LetterSpacing(0.06f);
            });

            row.ConstantItem(14);
            row.RelativeItem().Element(c => SignatureLine(c, leftRole));
            row.ConstantItem(14);
            row.RelativeItem().Element(c => SignatureLine(c, rightRole));
        });
    }

    /// <summary>
    /// Trait de signature. Trait CONTINU : QuestPDF ne sait pas tracer un filet pointillé, là où la
    /// maquette HTML en pose un. Sans incidence à l'usage — la signature se pose par-dessus.
    /// </summary>
    private static void SignatureLine(IContainer container, string role)
    {
        container.Column(column =>
        {
            column.Item().Height(12);
            column.Item().BorderTop(0.5f).BorderColor(Faint).PaddingTop(2)
                .Text(role.ToUpperInvariant()).Bold().FontSize(6.5f).FontColor(Muted).LetterSpacing(0.06f);
        });
    }

    /// <summary>Note de bas de page : la plus petite ligne du document, jamais une mention obligatoire.</summary>
    public static void Footnote(IContainer container, string text) =>
        container.AlignCenter().Text(text).FontSize(5.8f).FontColor(Faint);

    /// <summary>Concatène les mentions RENSEIGNÉES, séparées par un point médian. Vide si tout manque.</summary>
    public static string JoinPresent(params string?[] parts) =>
        string.Join("  ·  ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
