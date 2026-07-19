namespace SamaEcole.Application.Schools;

/// <summary>
/// openapi.yaml §SchoolSettings. `gradingScale` y est déclaré en CHAÎNE (« 10 » / « 20 ») alors que
/// le domaine le manipule en entier — le barème sert à calculer des moyennes, pas à être affiché.
/// La conversion est faite ici, une fois, plutôt que d'imposer un int.Parse à chaque module de notes.
/// </summary>
public record SchoolSettingsDto(
    string GradingScale,
    string StudentMatriculeFormat,
    string TeacherMatriculeFormat,
    int AutoLogoutMinutes,
    string DateFormat,
    int TuitionMonthsPerYear,
    bool AllowSecretaryToManageGrading,
    bool AllowFinanceToModifyFees,
    bool AllowFinanceToDeleteFees);
