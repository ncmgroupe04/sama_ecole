using SamaEcole.Application.StateIntegration;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Livret de compétences en PDF, A4 PORTRAIT (Volume 1 §23.6).
///
/// Portrait, contrairement au STATEDUC : le livret est une LISTE longue (une ligne par compétence,
/// souvent 40 à 60) avec peu de colonnes — deux ou trois trimestres. C'est la hauteur qui manque, pas
/// la largeur. Le document accepte donc PLUSIEURS pages, là où le bulletin n'en tolère qu'une : une
/// grille APC complète ne tient pas sur une page A4 et la comprimer la rendrait illisible.
///
/// Implémentation côté Infrastructure (QuestPDF).
/// </summary>
public interface ISkillsBookletPdfGenerator
{
    byte[] Generate(SkillsBookletModel model);
}
