using MediatR;

namespace SamaEcole.Application.Schools.Commands.UpdateGradingScale;

/// <summary>
/// PUT /schools/current/settings/grading-scale — ticket JGK-G02. Endpoint dédié, distinct de
/// UpdateSchoolSettingsCommand : ouvrir CE seul réglage au Secrétariat (délégation en cas d'absence
/// du Directeur, docs/Volume_7_Security.md « Paramètres de l'école ») ne doit pas lui donner accès aux
/// formats de matricule, à la déconnexion automatique ou aux mensualités, qui restent Directeur seul.
/// Aucun SchoolId ici : l'établissement vient du JWT, jamais du corps de la requête.
/// </summary>
public record UpdateGradingScaleCommand(string GradingScale) : IRequest<SchoolSettingsDto>;
