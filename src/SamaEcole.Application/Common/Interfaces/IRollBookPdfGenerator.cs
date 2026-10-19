using SamaEcole.Application.RollBook.Pdf;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>Met en page le cahier de roulement (A4 paysage, FR / AR / bilingue) à partir d'un modèle déjà calculé.</summary>
public interface IRollBookPdfGenerator
{
    byte[] Generate(RollBookPdfModel model, RollBookPdfAssets assets);
}
