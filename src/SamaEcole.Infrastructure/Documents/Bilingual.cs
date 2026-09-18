using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Aide de mise en page bilingue Français/Arabe pour les documents QuestPDF (module Coran/Franco-
/// Arabe). Structure BLOC par BLOC : un bloc arabe est un conteneur RTL COMPLET
/// (<see cref="QuestPDF.Fluent.ContentDirectionExtensions.ContentFromRightToLeft"/>), jamais mélangé
/// mot à mot avec du français dans le même <c>Text()</c> — le réordonnancement bidirectionnel complet
/// (mélange LTR/RTL sur une même ligne) est un problème que QuestPDF n'abstrait pas entièrement.
///
/// Suppose <see cref="PdfFonts.EnsureRegistered"/> déjà appelée (fait une fois au démarrage, voir
/// <c>DependencyInjection.AddInfrastructure</c>) — cette classe ne le fait pas elle-même : elle ne
/// connaît pas le cycle de vie de l'application, seulement la mise en page.
/// </summary>
public static class Bilingual
{
    /// <summary>Taille par défaut d'un bloc arabe accolé à du texte français à 8,5 pt (voir ReportCardDocument).</summary>
    public const float DefaultArabicFontSize = 8.5f;

    /// <summary>
    /// Un paragraphe arabe autonome : direction RTL, police <see cref="PdfFonts.Arabic"/>. À poser SOUS
    /// ou À CÔTÉ d'un bloc français existant (jamais À L'INTÉRIEUR du même <c>Text()</c> — voir la
    /// remarque de classe). Vide si <paramref name="arabicText"/> est vide : jamais une case vide qui
    /// prend de la place, même logique que le reste du bulletin (une valeur non renseignée n'imprime
    /// rien, plutôt qu'un espace réservé trompeur).
    /// </summary>
    public static void ArabicBlock(
        IContainer container, string? arabicText, float fontSize = DefaultArabicFontSize, bool bold = false)
    {
        if (string.IsNullOrWhiteSpace(arabicText))
        {
            return;
        }

        container.ContentFromRightToLeft().Text(text =>
        {
            var span = text.Span(arabicText).FontFamily(PdfFonts.Arabic).FontSize(fontSize);
            if (bold)
            {
                span.Bold();
            }
        });
    }

    /// <summary>
    /// Paire français (gauche, LTR) / arabe (droite, RTL) côte à côte, chacun composant SON PROPRE
    /// bloc — jamais partagé. Pour un en-tête ou un libellé, sur le modèle des « blocs... côte à côte »
    /// de la demande. Si <paramref name="arabic"/> ne produit rien (texte absent), le bloc français
    /// occupe alors toute la largeur plutôt que de laisser une colonne vide.
    /// </summary>
    public static void SideBySide(IContainer container, Action<IContainer> french, bool hasArabic, Action<IContainer> arabic)
    {
        if (!hasArabic)
        {
            french(container);
            return;
        }

        container.Row(row =>
        {
            row.RelativeItem().Element(french);
            row.ConstantItem(8);
            row.RelativeItem().Element(arabic);
        });
    }
}
