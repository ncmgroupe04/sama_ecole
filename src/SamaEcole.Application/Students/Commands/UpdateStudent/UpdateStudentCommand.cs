using MediatR;

namespace SamaEcole.Application.Students.Commands.UpdateStudent;

/// <summary>
/// PUT /api/v1/students/{id} — corrige les informations non financières et non sécurisées d'une fiche
/// élève déjà créée (état civil, classe, coordonnées du tuteur). Réservé au Directeur et au
/// Secrétariat (StudentsController.ManageRoles) : ce sont eux qui saisissent et corrigent les fiches.
///
/// Volontairement ABSENT de ce contrat : <c>Matricule</c> — il est généré une seule fois, à
/// l'enregistrement, jamais réémis ni modifiable (AGENTS.md règle #3). Toute donnée financière (solde,
/// historique de paiement) reste hors de portée : elle n'existe pas sur Student mais sur Enrollment
/// (AGENTS.md règle #4).
///
/// <see cref="RowVersion"/> est le jeton xmin lu à la dernière consultation
/// (StudentIdentityDto.RowVersion) : verrouillage optimiste (règle #5).
/// </summary>
public record UpdateStudentCommand(
    Guid Id,
    string FullName,
    DateOnly BirthDate,
    string? BirthPlace,
    string Gender,
    Guid ClassroomId,
    string? PhotoUrl,
    string? GuardianName,
    string? GuardianPhone,
    uint RowVersion) : IRequest<UpdateStudentResult>;

public record UpdateStudentResult(Guid Id, uint RowVersion);
