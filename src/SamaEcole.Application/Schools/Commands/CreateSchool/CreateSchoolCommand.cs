using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Schools.Commands.CreateSchool;

/// <summary>
/// POST /schools — ticket JGK-B01 (openapi.yaml §SchoolCreateRequest).
/// Le mot de passe du Directeur n'est PAS un paramètre : il est généré côté serveur.
///
/// PAS IAuditableRequest (JGK-H01) : l'acteur (Super Admin) n'a aucun SchoolId, et la policy RLS de
/// `audit_logs` rejette donc TOUT INSERT tenté depuis sa session, quelle que soit la valeur de
/// SchoolId visée — même contrainte que `users` (voir AddSchoolProvisioning). Une entrée d'audit pour
/// « actions Super Admin » exigerait sa propre fonction SECURITY DEFINER (comme
/// provision_school_director) ; scope volontairement différé, voir AuditLoggingBehavior.
/// </summary>
public record CreateSchoolCommand(
    string Name,
    string Address,
    string? Phone,
    string DirectorEmail,
    string? DirectorFullName,
    SubscriptionPlan Plan) : IRequest<CreateSchoolResult>;

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
