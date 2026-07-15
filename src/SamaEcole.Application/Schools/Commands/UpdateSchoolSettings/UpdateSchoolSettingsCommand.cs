using MediatR;

namespace SamaEcole.Application.Schools.Commands.UpdateSchoolSettings;

/// <summary>
/// PUT /schools/current/settings — ticket JGK-B02. Seul le Directeur peut modifier.
/// Aucun SchoolId ici : l'établissement vient du JWT, jamais du corps de la requête.
/// </summary>
public record UpdateSchoolSettingsCommand(
    string GradingScale,
    string StudentMatriculeFormat,
    string TeacherMatriculeFormat,
    int AutoLogoutMinutes,
    string DateFormat,
    int TuitionMonthsPerYear) : IRequest<SchoolSettingsDto>;
