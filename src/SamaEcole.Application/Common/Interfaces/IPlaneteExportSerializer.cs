using SamaEcole.Application.StateIntegration;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Sérialise la matrice « Planète Ready » en JSON ou en CSV (Volume 1 §23.2).
///
/// UN SEUL point de sérialisation pour les deux formats, délibérément : c'est ce qui garantit que le
/// CSV et le JSON du même export portent les mêmes colonnes dans le même ordre. Deux sérialiseurs
/// indépendants auraient divergé à la première colonne ajoutée, et l'IEF — qui rapproche les deux à la
/// main — l'aurait découvert avant nous.
///
/// Le CSV suit RFC 4180 avec un point-virgule : les fichiers de l'administration sénégalaise sont
/// ouverts dans un Excel en locale française, où la virgule est le séparateur DÉCIMAL. Un CSV
/// virgule y arrive sur une seule colonne. BOM UTF-8 en tête, pour la même raison — sans lui, Excel
/// lit « Ndèye Fatou » en « NdÃ¨ye Fatou ».
/// </summary>
public interface IPlaneteExportSerializer
{
    StateExportFile Serialize(PlaneteExportDto export, StateExportFormat format);
}
