using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Attribution d'un enseignant à une classe, pour une matière, sur une année scolaire donnée
/// (ticket JGK-D04, docs/Volume_3_DDS.md « TeacherAssignments »).
///
/// Toujours l'année ACTIVE au moment de l'attribution (voir AssignTeacherCommandHandler, même
/// convention que CreateEnrollmentCommandHandler) : jamais une année choisie par le client. C'est ce
/// qui transforme la table en HISTORIQUE au fil du temps — les lignes des années passées ne sont
/// jamais réécrites, seule une nouvelle attribution s'ajoute quand l'établissement change d'année
/// active. La fiche enseignant (GET /teachers/{id}) les restitue donc toutes, groupées par année.
/// </summary>
public class TeacherAssignment : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid TeacherId { get; set; }
    public Guid ClassroomId { get; set; }
    public Guid SubjectId { get; set; }
    public Guid SchoolYearId { get; set; }
}
