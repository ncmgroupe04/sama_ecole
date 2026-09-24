using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.Grades.Commands.UpdateGrade;

/// <summary>
/// PUT /grades/{id} — ticket JGK-G01. Corrige une note déjà saisie. Directeur et Secrétariat corrigent
/// sans limite de délai. L'Enseignant n'y est autorisé que dans la fenêtre
/// SchoolSettings.GradeEditWindowDays (7 jours par défaut, réglée par le Directeur) ET s'il est
/// l'auteur de la note ou affecté à sa classe et à sa matière — 403 sinon (GradeEditPolicy,
/// Évolution N°1). Cette évolution remplace l'ancien contrôle « Photoshop », qui lui interdisait toute
/// correction.
///
/// <see cref="RowVersion"/> est le jeton xmin lu à la dernière consultation : verrouillage optimiste
/// (AGENTS.md règle #5), comme UpdateClassFeeCommand. Un jeton périmé (la note a changé entre-temps)
/// fait échouer SaveChangesAsync en 409, jamais un écrasement silencieux.
///
/// IAuditableRequest (JGK-H01) : la correction de notes fait partie des écritures sensibles.
/// </summary>
public record UpdateGradeCommand(Guid Id, decimal Value, uint RowVersion) : IRequest<GradeResult>, IAuditableRequest;
