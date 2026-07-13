namespace Jangalekat.Domain.Common;

/// <summary>
/// Classe de base de toute entité métier. Voir docs/Volume_3_DDS.md et AGENTS.md règle #6.
/// Identifiant en UUID v7 (ordonnable), champs d'audit obligatoires, soft delete uniquement.
/// </summary>
public abstract class AuditableEntity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public string? DeletedBy { get; private set; }

    /// <summary>
    /// Suppression logique uniquement. Ne jamais supprimer physiquement une entité métier (AGENTS.md règle #6).
    /// </summary>
    public void SoftDelete(string deletedBy)
    {
        IsDeleted = true;
        DeletedAt = DateTimeOffset.UtcNow;
        DeletedBy = deletedBy;
    }
}
