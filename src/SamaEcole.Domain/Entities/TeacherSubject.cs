using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Matière qu'un enseignant est qualifié à enseigner (ticket JGK-D03, openapi.yaml
/// TeacherCreateRequest.subjectIds). Renseignée à la création de la fiche, modifiable ensuite.
///
/// Distincte de l'attribution classe/matière/année (ticket JGK-D04, `TeacherAssignments` du DDS) :
/// ici, c'est une QUALIFICATION générale de l'enseignant, pas encore un emploi du temps dans une
/// classe pour une année scolaire donnée.
/// </summary>
public class TeacherSubject : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid TeacherId { get; set; }
    public Guid SubjectId { get; set; }
}
