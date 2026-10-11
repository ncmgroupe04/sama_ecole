using SamaEcole.Application.Boarding.Documents.BoarderSheet;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Rend la fiche individuelle d'un pensionnaire en PDF A4, sur le même contrat que <see cref="IHizbReportPdfGenerator"/> :
/// mise en page en Infrastructure (QuestPDF, police arabe embarquée), donnée en Application, génération déterministe et
/// synchrone. Le générateur imprime ce que le DTO contient — c'est <see cref="BoarderSheetDto"/> qui est masqué par rôle
/// (fiche médicale), jamais le document. <paramref name="logo"/> est <c>null</c> quand l'établissement n'en a pas.
/// </summary>
public interface IBoarderSheetPdfGenerator
{
    byte[] Generate(BoarderSheetDto sheet, byte[]? logo);
}
