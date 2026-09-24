using SamaEcole.Application.Grades.Queries.GetGradeSheetPdf;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Rend la fiche de saisie PAPIER des notes en PDF (A4) : une grille vierge que l'enseignant remplit au
/// stylo, avant de reporter les notes à l'écran. <paramref name="logo"/> porte les octets déjà validés du
/// logo de l'école (via <see cref="ISchoolLogoProvider"/>), ou <c>null</c> : la fiche s'émet alors sans logo.
/// </summary>
public interface IGradeSheetPdfGenerator
{
    byte[] Generate(GradeSheetPdfDto sheet, byte[]? logo);
}
