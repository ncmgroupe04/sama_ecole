namespace SamaEcole.Domain.Enums;

/// <summary>
/// Demi-journée d'un jour de classe — l'unité du cahier de roulement (Franco-Arabe Bloc 5). Stockée en
/// string (convention du projet). La coupure matin / après-midi est celle de l'alternance français / arabe :
/// 13 h, à l'heure de DÉBUT du créneau (voir <c>SectionScheduleAnalyzer.HalfOf</c>).
/// </summary>
public enum HalfDay
{
    Morning = 0,
    Afternoon = 1
}
