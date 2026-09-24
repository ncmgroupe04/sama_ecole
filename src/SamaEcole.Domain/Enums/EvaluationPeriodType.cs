namespace SamaEcole.Domain.Enums;

/// <summary>
/// Découpage de l'année scolaire en périodes d'évaluation, choisi par le Directeur (SchoolSettings).
/// Ne s'applique qu'aux années CRÉÉES ensuite ou explicitement rejouées : les notes pointent un
/// TermId, une année déjà notée garde son découpage. Stocké en string (convention du projet).
/// </summary>
public enum EvaluationPeriodType
{
    /// <summary>Trois trimestres — le système sénégalais standard, et le défaut.</summary>
    Trimester = 0,

    /// <summary>Deux semestres.</summary>
    Semester = 1,

    /// <summary>Un nombre de périodes choisi par l'école (SchoolSettings.CustomPeriodCount).</summary>
    Custom = 2
}
