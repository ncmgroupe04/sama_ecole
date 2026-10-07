using SamaEcole.Application.Boarding.Documents.DormitoryRegister;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Rend le registre imprimable d'un pavillon pour une nuit (A4 paysage), sur le même contrat que
/// <see cref="IBoarderSheetPdfGenerator"/>. Document posé dans le pavillon : <see cref="DormitoryRegisterDto"/> ne porte
/// ni donnée de santé ni téléphone, le générateur n'en a donc jamais à imprimer. <paramref name="logo"/> est <c>null</c>
/// quand l'établissement n'en a pas.
/// </summary>
public interface IDormitoryRegisterPdfGenerator
{
    byte[] Generate(DormitoryRegisterDto register, byte[]? logo);
}
