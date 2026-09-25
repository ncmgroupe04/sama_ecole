using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Une unité du programme traitée pendant une séance du cahier de texte (Évolution N°7). Retirer une unité d'une séance
/// archive la ligne (suppression logique, règle #6) ; l'avancement ne compte que les liens actifs.
/// </summary>
public class ClassJournalEntryUnit : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid ClassJournalEntryId { get; set; }

    public Guid SyllabusUnitId { get; set; }
}
