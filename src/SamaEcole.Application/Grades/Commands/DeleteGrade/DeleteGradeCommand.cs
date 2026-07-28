using MediatR;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Grades.Commands.DeleteGrade;

/// <summary>
/// DELETE /api/v1/grades/{id} — annule (soft delete) une note déjà saisie. Contrôle strict sur la
/// correction des notes (matrice d'autorisation "Photoshop") : réservé au Directeur et au
/// Secrétariat, JAMAIS à l'Enseignant — même s'il est l'auteur de la saisie initiale. Une fois
/// enregistrée, une note ne se corrige/annule plus que via ces deux rôles (voir UpdateGradeCommand
/// pour la même règle appliquée à la correction plutôt qu'à l'annulation).
///
/// Toujours un soft delete (AGENTS.md règle #6) : DeletedAt/DeletedBy (AuditableEntity) tracent
/// l'annulation elle-même, pas de table d'historique dédiée pour les notes (contrairement aux frais).
///
/// <paramref name="RowVersion"/> : même verrouillage optimiste que UpdateGradeCommand (AGENTS.md
/// règle #5) — annuler une note modifiée entre-temps échoue en 409, jamais un écrasement silencieux.
/// </summary>
public record DeleteGradeCommand(Guid Id, uint RowVersion) : IRequest<Unit>, IAuditableRequest;
