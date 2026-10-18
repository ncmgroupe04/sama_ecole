using System.Reflection;

// Namespace délibérément SamaEcole.Infrastructure.Documents (pas un sous-namespace .Fonts, malgré le
// dossier Fonts/ où vit ce fichier) : QuestPDF.Helpers expose déjà une classe statique `Fonts`
// (Fonts.Arial, etc.), consommée par plusieurs documents de ce même namespace — un sous-namespace
// `.Fonts` entre en collision avec cette classe pour toute résolution `using QuestPDF.Helpers;` faite
// depuis SamaEcole.Infrastructure.Documents (voir SchoolCardDocument.cs).
namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Point UNIQUE d'enregistrement des polices custom auprès de QuestPDF — pour la police arabe
/// (module Coran/Franco-Arabe), déclarée en <c>EmbeddedResource</c> dans
/// <c>SamaEcole.Infrastructure.csproj</c> plutôt que déposée dans <c>wwwroot</c> : Infrastructure n'a
/// pas de site web propre, et l'embarquement voyage avec l'assembly quelle que soit la disposition du
/// système de fichiers du conteneur.
///
/// <see cref="QuestPDF.Drawing.FontManager.RegisterFont(System.IO.Stream)"/> charge les octets de la
/// police directement dans le moteur — un mécanisme DIFFÉRENT de l'alias fontconfig « Times New
/// Roman » → Liberation Serif posé dans le Dockerfile (une police SYSTÈME) : aucune modification du
/// Dockerfile n'est nécessaire pour l'arabe.
///
/// Appelée UNE SEULE FOIS depuis <see cref="DependencyInjection.AddInfrastructure"/>, au démarrage —
/// délibérément PAS répliquée dans le constructeur statique de chaque <c>*PdfGenerator.cs</c> comme
/// <c>QuestPDF.Settings.License</c> l'est aujourd'hui (26 constructeurs statiques identiques, un
/// oubli a déjà causé un bug en production sur <c>DailyClosingReportPdfGenerator</c>). Aucun
/// générateur n'a besoin de savoir que cette classe existe.
/// </summary>
public static class PdfFonts
{
    /// <summary>Nom de famille sous lequel la police arabe est enregistrée — à utiliser avec <c>FontFamily(...)</c>.</summary>
    public const string Arabic = "Noto Naskh Arabic";

    private const string EmbeddedResourceName = "SamaEcole.Infrastructure.Fonts.NotoNaskhArabic.ttf";

    private static readonly object Lock = new();
    private static bool _registered;

    /// <summary>
    /// Idempotente : un second appel (tests, ou un futur second point d'entrée) ne réenregistre rien.
    /// Lève si la ressource embarquée est introuvable — une police manquante doit faire échouer le
    /// démarrage bruyamment, jamais produire des bulletins bilingues silencieusement dépourvus d'arabe.
    /// </summary>
    public static void EnsureRegistered()
    {
        if (_registered)
        {
            return;
        }

        lock (Lock)
        {
            if (_registered)
            {
                return;
            }

            var assembly = typeof(PdfFonts).Assembly;
            using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
                ?? throw new InvalidOperationException(
                    $"Police arabe introuvable : ressource embarquée « {EmbeddedResourceName} » absente de {assembly.GetName().Name}.");

            QuestPDF.Drawing.FontManager.RegisterFont(stream);
            _registered = true;
        }
    }
}

/// <summary>
/// Enregistre la police arabe dès le chargement de l'assembly, AVANT tout rendu PDF du processus.
///
/// Pourquoi : sous Linux (sans police arabe système — conteneur de production, runner CI), un PDF arabe généré AVANT
/// l'enregistrement empoisonne le processus : QuestPDF mémorise l'absence de police et l'arabe reste vide (glyphes
/// « NUL » à l'extraction) même après <see cref="PdfFonts.EnsureRegistered"/>. Dépendre d'un appel fait « au démarrage »
/// rendait le résultat tributaire de l'ordre d'exécution : tests de documents ne passant pas par
/// <c>AddInfrastructure</c>, ou rendu lancé avant lui. Un initialiseur de module s'exécute avant le premier accès à
/// l'assembly, donc avant tout générateur — dans l'application comme dans chaque processus de test.
/// </summary>
internal static class PdfFontsModuleInitializer
{
#pragma warning disable CA2255 // Initialiseur de module volontaire dans une bibliothèque : voir le résumé de la classe.
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void RegisterArabicFont() => PdfFonts.EnsureRegistered();
#pragma warning restore CA2255
}
