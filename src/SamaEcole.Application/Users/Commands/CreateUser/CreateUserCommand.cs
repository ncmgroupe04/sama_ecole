using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Users.Commands.CreateUser;

/// <summary>
/// POST /users — un Directeur crée un compte pour son personnel (Secrétariat, Finance, Enseignant).
///
/// Le mot de passe est saisi DIRECTEMENT par le Directeur, pas généré ni envoyé par e-mail : contrairement
/// à JGK-B01 (création d'établissement), l'adaptateur SMTP réel relève du ticket JGK-G03, hors périmètre
/// MVP — voir LoggingEmailSender. Le Directeur communique lui-même le mot de passe à la personne concernée.
///
/// IAuditableRequest (JGK-H01) : créer un compte est une action administrative sensible, dans le même
/// esprit que « changements de statut utilisateur » du journal d'audit centralisé.
/// </summary>
public record CreateUserCommand(string FullName, string Email, string Password, Role Role)
    : IRequest<CreateUserResult>, IAuditableRequest;

public record CreateUserResult(Guid UserId, string FullName, string Email, Role Role, EntityStatus Status);
