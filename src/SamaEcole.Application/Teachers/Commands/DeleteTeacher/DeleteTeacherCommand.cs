using MediatR;

namespace SamaEcole.Application.Teachers.Commands.DeleteTeacher;

/// <summary>
/// DELETE /api/v1/teachers/{id} — archive (soft delete) une fiche enseignant créée par pure erreur de
/// saisie. Toujours un soft delete (AGENTS.md règle #6). Réservé au Directeur et au Secrétariat.
///
/// Le Handler DOIT rejeter la suppression si un historique critique est déjà attaché : une attribution
/// classe/matière/année (<see cref="Domain.Entities.TeacherAssignment"/>, ticket JGK-D04). Sans
/// attribution, la fiche n'a encore produit aucun effet pédagogique — c'est le cas d'une « erreur
/// visuelle » que cette commande cible. Les qualifications (TeacherSubject) ne bloquent PAS la
/// suppression : elles sont librement modifiables (voir UpdateTeacherCommand), pas un historique.
///
/// <paramref name="RowVersion"/> : même verrouillage optimiste que UpdateTeacherCommand (règle #5).
/// </summary>
public record DeleteTeacherCommand(Guid Id, uint RowVersion) : IRequest<Unit>;
