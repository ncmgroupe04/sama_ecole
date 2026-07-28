using SamaEcole.Application.Students;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Lit un fichier d'import d'élèves (CSV ou Excel, sept colonnes fixes — voir
/// <see cref="StudentImportFileRow"/>) et en extrait les lignes BRUTES, sans validation métier.
///
/// Contrairement à <see cref="IGradeImportFileParser"/> (2 colonnes, en-tête optionnel auto-détecté),
/// la première ligne est TOUJOURS traitée comme un en-tête et ignorée : le fichier attendu est celui
/// téléchargé depuis GetStudentImportTemplateQuery, qui en comporte toujours un — pas besoin d'une
/// heuristique de détection sur 7 colonnes.
///
/// Implémentation côté Infrastructure (ClosedXML), même convention que IGradeImportFileParser :
/// Application ne référence aucune bibliothèque de troisième partie.
/// </summary>
public interface IStudentImportFileParser
{
    /// <exception cref="Exceptions.ValidationException">
    /// Extension non prise en charge, fichier illisible/corrompu, ou aucune ligne de données (au-delà
    /// de l'en-tête).
    /// </exception>
    IReadOnlyList<StudentImportFileRow> Parse(byte[] fileContent, string fileName);
}
