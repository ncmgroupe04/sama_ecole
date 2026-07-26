using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents.Components;

/// <summary>
/// Bandeau République/devise/Ministère + ligne IA/IEF/établissement, partagés par tout nouveau
/// document officiel destiné à l'élève ou à sa famille (Exéat, PV de discipline, Sommation…), sur le
/// même modèle que <see cref="EnrollmentCertificateDocument"/>. Les 12 documents antérieurs à ce
/// composant gardent leur propre méthode privée (pattern historique, non touché ici) : ce composant
/// n'est utilisé que par les documents introduits après lui, pour ne pas produire de diff hors
/// périmètre sur des fichiers déjà validés.
/// </summary>
public static class OfficialHeaderComponent
{
    /// <summary>République du Sénégal / devise / Ministère — bandeau centré, identique sur tout document officiel.</summary>
    public static void ComposeMinistryBanner(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().AlignCenter().Text("RÉPUBLIQUE DU SÉNÉGAL").Bold().FontSize(12);
            column.Item().AlignCenter().Text("Un Peuple - Un But - Une Foi").Italic().FontSize(9);
            column.Item().PaddingTop(6).AlignCenter().Text("MINISTÈRE DE L'ÉDUCATION NATIONALE").Bold().FontSize(11);
        });
    }

    /// <summary>
    /// IA / IEF / établissement — libellé en gras, valeur en normal, ligne vide si non renseignée
    /// plutôt qu'une valeur inventée (AGENTS.md : ne jamais fabriquer une donnée absente).
    /// </summary>
    public static void ComposeEstablishmentBlock(
        IContainer container,
        string? inspectionAcademie,
        string? inspectionEducationFormation,
        string headingPrefix,
        string? headingName,
        byte[]? logo)
    {
        container.BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingBottom(6).Row(row =>
        {
            row.RelativeItem(3).Column(left =>
            {
                left.Item().Element(c => HeaderLine(c, "Inspection d'Académie de", inspectionAcademie));
                left.Item().Element(c => HeaderLine(c, "Inspection de l'Éducation et de la Formation de", inspectionEducationFormation));
                left.Item().Element(c => HeaderLine(c, headingPrefix, headingName));
            });

            if (logo is not null)
            {
                row.ConstantItem(55).MaxHeight(40).AlignRight().Image(logo).FitArea();
            }
        });

        static void HeaderLine(IContainer container, string label, string? value) =>
            container.Text(text =>
            {
                text.Span($"{label} : ").Bold().FontSize(9.5f);
                text.Span(value ?? "").FontSize(9.5f);
            });
    }

    /// <summary>
    /// Bloc de pied de document anti-fraude commun (AGENTS.md §2.5 du cahier des charges Documents) :
    /// QR code de vérification à gauche, numéro de référence et mention de provenance à droite.
    /// </summary>
    public static void ComposeAuthenticityFooter(IContainer container, byte[] qrCodeImage, string referenceNumber)
    {
        container.Row(row =>
        {
            row.ConstantItem(48).Height(48).Image(qrCodeImage).FitArea();
            row.RelativeItem().PaddingLeft(8).AlignMiddle().Column(column =>
            {
                column.Item().Text($"Référence : {referenceNumber}").FontSize(7).FontColor(Colors.Grey.Darken2);
                column.Item().Text("Document généré par Sama École — vérifiable par le QR code ci-contre")
                    .FontSize(7).FontColor(Colors.Grey.Darken2);
            });
        });
    }
}
