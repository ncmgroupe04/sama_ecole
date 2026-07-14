using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Entité de référence à suivre pour tout nouveau module tenant : voir AGENTS.md règle #2 et #3.
/// Le matricule est renseigné uniquement par le Handler de création, dans la même transaction
/// que l'insertion (jamais pré-généré à l'ouverture d'un formulaire) — voir ticket JGK-D01.
/// </summary>
public class Student : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public required string Matricule { get; set; }
    public required string FullName { get; set; }
    public DateOnly BirthDate { get; set; }
    public required string Gender { get; set; } // "M" | "F"
    public Guid ClassroomId { get; set; }

    public string? GuardianName { get; set; }
    public string? GuardianPhone { get; set; }
}
