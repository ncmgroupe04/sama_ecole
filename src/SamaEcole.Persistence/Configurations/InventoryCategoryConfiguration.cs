using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL équivalente est posée par la migration AddInventoryModule (AGENTS.md
/// règle #2 : les DEUX protections, jamais une seule).
/// </summary>
public class InventoryCategoryConfiguration : IEntityTypeConfiguration<InventoryCategory>
{
    public void Configure(EntityTypeBuilder<InventoryCategory> builder)
    {
        builder.ToTable("inventory_categories");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.SchoolId).IsRequired();

        // Verrou optimiste xmin (AGENTS.md règle #5), même convention que Building/Room.
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(c => c.Name).IsRequired().HasMaxLength(100);
        builder.Property(c => c.Description).HasMaxLength(300);

        // IsDeleted dans la clé : une catégorie archivée ne doit pas bloquer la réutilisation de son nom.
        builder.HasIndex(c => new { c.SchoolId, c.Name, c.IsDeleted }).IsUnique();

        // Restrict : on ne supprime jamais physiquement une école (AGENTS.md règle #6).
        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(c => c.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
