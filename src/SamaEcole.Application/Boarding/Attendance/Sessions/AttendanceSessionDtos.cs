using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding.Attendance.Sessions;

/// <summary>
/// Session de pointage telle que la configuration et les écrans la lisent. <paramref name="Days"/> : jours ISO (1 = lundi … 7 =
/// dimanche). <paramref name="HasRecords"/> : au moins un pointage existe — l'audience n'est alors plus modifiable et la session
/// ne se supprime plus (on la désactive).
/// </summary>
public record AttendanceSessionDto(
    Guid Id,
    string Name,
    AttendanceAudience Audience,
    IReadOnlyList<int> Days,
    string? TimeLabel,
    bool IsActive,
    int SortOrder,
    bool HasRecords);

/// <summary>Modèle de session proposé en un clic. La Direction le modifie ou l'ignore : rien n'est imposé.</summary>
public record AttendanceSessionTemplateDto(
    string Code, string Name, AttendanceAudience Audience, string? TimeLabel, string Description);

/// <summary>Modèles proposés (source unique côté serveur : l'écran les liste, il ne les code pas en dur).</summary>
public static class AttendanceSessionTemplates
{
    public static readonly IReadOnlyList<AttendanceSessionTemplateDto> All =
    [
        new("night", "Appel de nuit", AttendanceAudience.Interns, "21 h 00", "Les internes présents au dortoir, par pavillon."),
        new("morning", "Appel du matin", AttendanceAudience.Interns, "06 h 00", "Les internes au réveil, par pavillon."),
        new("breakfast", "Petit-déjeuner", AttendanceAudience.AllBoarders, "07 h 00", "Tous les pensionnaires, par classe."),
        new("lunch", "Déjeuner", AttendanceAudience.AllBoarders, "12 h 30", "Tous les pensionnaires, par classe."),
        new("dinner", "Dîner", AttendanceAudience.AllBoarders, "19 h 30", "Tous les pensionnaires, par classe."),
        new("halqa", "Appel de la Halqa", AttendanceAudience.Halqa, null, "Les élèves d'un Oustaz, pointés par l'Oustaz.")
    ];
}

internal static class AttendanceSessionMapper
{
    public static AttendanceSessionDto ToDto(AttendanceSession s, bool hasRecords) =>
        new(s.Id, s.Name, s.Audience, AttendanceDays.ToIsoDays(s.Days), s.TimeLabel, s.IsActive, s.SortOrder, hasRecords);
}
