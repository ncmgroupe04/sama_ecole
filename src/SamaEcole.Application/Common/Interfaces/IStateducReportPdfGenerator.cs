using SamaEcole.Application.StateIntegration;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Rapport annuel STATEDUC en PDF, **A4 PAYSAGE** — le format du formulaire officiel, imposé par ses
/// tableaux à dix colonnes et non par une préférence esthétique : en portrait, la pyramide des âges et
/// le croisement diplôme académique × diplôme professionnel se replient sur deux lignes et deviennent
/// illisibles pour l'agent qui les saisit à l'IEF.
///
/// Implémentation côté Infrastructure (QuestPDF), même convention que
/// <see cref="IInventoryReportPdfGenerator"/> : Application ne référence aucune bibliothèque tierce.
/// </summary>
public interface IStateducReportPdfGenerator
{
    /// <summary>
    /// <paramref name="officialStamp"/> et <paramref name="directorSignature"/> viennent de
    /// Paramètres → Établissement. Null s'imprime comme un emplacement vide — jamais une image
    /// inventée, même convention que le bulletin de notes.
    /// </summary>
    byte[] Generate(
        StateducReportDto report,
        byte[]? directorSignature = null,
        byte[]? officialStamp = null);
}
