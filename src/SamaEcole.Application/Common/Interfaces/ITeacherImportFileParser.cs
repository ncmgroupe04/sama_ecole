using SamaEcole.Application.Teachers;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Lit un fichier d'import d'enseignants (CSV ou Excel, six colonnes fixes — voir
/// <see cref="TeacherImportFileRow"/>) et en extrait les lignes BRUTES, sans validation métier.
///
/// Même convention que <see cref="IStudentImportFileParser"/> : la première ligne est TOUJOURS
/// traitée comme un en-tête et ignorée — le fichier attendu est celui téléchargé depuis
/// GetTeacherImportTemplateQuery, qui en comporte toujours un.
///
/// Implémentation côté Infrastructure (ClosedXML) : Application ne référence aucune bibliothèque de
/// troisième partie.
/// </summary>
public interface ITeacherImportFileParser
{
    /// <exception cref="Exceptions.ValidationException">
    /// Extension non prise en charge, fichier illisible/corrompu, ou aucune ligne de données (au-delà
    /// de l'en-tête).
    /// </exception>
    IReadOnlyList<TeacherImportFileRow> Parse(byte[] fileContent, string fileName);
}
