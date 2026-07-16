using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.Grades.Commands.UpdateGrade;

/// <summary>
/// PUT /grades/{id} — ticket JGK-G01. Corrige une note déjà saisie. Ouvert au Directeur ET à
/// l'Enseignant (docs/Volume_7_Security.md « Notes » : Modifier = les deux), à la différence de
/// CreateGradeCommand (Saisir = Enseignant seul).
///
/// <see cref="RowVersion"/> est le jeton xmin lu à la dernière consultation : verrouillage optimiste
/// (AGENTS.md règle #5), comme UpdateClassFeeCommand. Un jeton périmé (la note a changé entre-temps —
/// un autre enseignant, ou le Directeur) fait échouer SaveChangesAsync en 409, jamais un écrasement
/// silencieux.
///
/// IAuditableRequest (JGK-H01) : la correction de notes fait partie des écritures sensibles.
/// </summary>
public record UpdateGradeCommand(Guid Id, decimal Value, uint RowVersion) : IRequest<GradeResult>, IAuditableRequest;
