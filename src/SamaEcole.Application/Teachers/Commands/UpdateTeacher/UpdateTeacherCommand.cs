using MediatR;

namespace SamaEcole.Application.Teachers.Commands.UpdateTeacher;

/// <summary>
/// PUT /api/v1/teachers/{id} — corrige les informations non financières et non sécurisées d'une fiche
/// enseignant déjà créée (état civil, contact, matières qualifiées). Réservé au Directeur et au
/// Secrétariat (TeachersController.ManageRoles), comme la création.
///
/// Volontairement ABSENTS de ce contrat : <c>Matricule</c> (généré une seule fois, règle #3) et
/// <c>UserId</c> — le rattachement d'un compte de connexion est une opération SÉCURISÉE (elle change
/// à qui appartient l'accès à la saisie de l'appel), hors du périmètre « correction de fiche
/// administrative » de ce ticket.
///
/// <see cref="RowVersion"/> est le jeton xmin lu à la dernière consultation
/// (TeacherProfileDto.RowVersion) : verrouillage optimiste (AGENTS.md règle #5).
/// </summary>
public record UpdateTeacherCommand(
    Guid Id,
    string FullName,
    string Email,
    string? Phone,
    string? BirthPlace,
    string? PhotoUrl,
    IReadOnlyList<Guid> SubjectIds,
    uint RowVersion) : IRequest<UpdateTeacherResult>;

public record UpdateTeacherResult(Guid Id, uint RowVersion);
