using MediatR;

namespace SamaEcole.Application.Schools.Commands.UpdateSchoolSettings;

/// <summary>
/// PUT /schools/current/settings — ticket JGK-B02. Seul le Directeur peut modifier.
/// Aucun SchoolId ici : l'établissement vient du JWT, jamais du corps de la requête.
///
/// Inclut AllowSecretaryToManageGrading (JGK-G02) : le Directeur y active/coupe la délégation au
/// Secrétariat du barème, des matières/coefficients et des mentions — lu par
/// CanManageGradingScaleHandler, jamais par PUT /grading-scale ni /subjects ni /grades/mentions
/// eux-mêmes, qui restent Directeur+Secrétariat sans condition sur CETTE commande-ci.
///
/// Inclut aussi AllowFinanceToModifyFees et AllowFinanceToDeleteFees (matrice d'autorisation
/// "Photoshop") : même mécanique, lues par CanModifyFeesHandler/CanDeleteFeesHandler, jamais par
/// PUT/DELETE /finance/fees eux-mêmes.
/// </summary>
public record UpdateSchoolSettingsCommand(
    string GradingScale,
    string StudentMatriculeFormat,
    string TeacherMatriculeFormat,
    int AutoLogoutMinutes,
    string DateFormat,
    int TuitionMonthsPerYear,
    bool AllowSecretaryToManageGrading,
    bool AllowFinanceToModifyFees,
    bool AllowFinanceToDeleteFees,
    string? DirectorSignatureUrl = null,
    string? SecretarySignatureUrl = null,
    string? CashierSignatureUrl = null,
    string? OfficialStampUrl = null,
    string TypeEtablissement = "Prive") : IRequest<SchoolSettingsDto>;

