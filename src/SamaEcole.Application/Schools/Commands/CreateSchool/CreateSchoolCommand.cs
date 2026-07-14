using MediatR;

namespace SamaEcole.Application.Schools.Commands.CreateSchool;

/// <summary>
/// POST /schools — ticket JGK-B01 (openapi.yaml §SchoolCreateRequest).
/// Le mot de passe du Directeur n'est PAS un paramètre : il est généré côté serveur.
/// </summary>
public record CreateSchoolCommand(
    string Name,
    string Address,
    string? Phone,
    string DirectorEmail,
    string? DirectorFullName) : IRequest<CreateSchoolResult>;

/// <summary>
/// Ne contient volontairement AUCUN mot de passe : celui du Directeur ne transite que par l'e-mail
/// qui lui est adressé. Le renvoyer ici le ferait apparaître dans les journaux d'accès, le cache du
/// navigateur et l'historique de l'outil qui appelle l'API.
/// </summary>
public record CreateSchoolResult(
    Guid SchoolId,
    string Name,
    Guid DirectorUserId,
    string DirectorEmail);
