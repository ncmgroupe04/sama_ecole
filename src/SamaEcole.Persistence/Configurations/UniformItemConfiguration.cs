using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par ApplicationDbContext.OnModelCreating
/// à toute entité ITenantEntity. La policy RLS équivalente vit dans la migration AddUniformsModule (AGENTS.md règle #2).
/// </summary>
public class UniformItemConfiguration : IEntityTypeConfiguration<UniformItem>
{
    public void Configure(EntityTypeBuilder<UniformItem> builder)
    {
        builder.ToTable("uniform_items");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.SchoolId).IsRequired();

        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(i => i.Name).IsRequired().HasMaxLength(100);
        builder.Property(i => i.Notes).HasMaxLength(300);

        // Persistés en chaîne, comme tous les enums métier.
        builder.Property(i => i.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(i => i.Gender).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(i => i.Cycle).HasConversion<string>().HasMaxLength(20);

        builder.Property(i => i.QuantityIncluded).IsRequired();

        builder.ToTable(t => t.HasCheckConstraint("CK_uniform_items_quantity_included", "\"QuantityIncluded\" >= 1"));

        // Identité réutilisable : unique sur les seules lignes ACTIVES (conception soft delete 2026-10-01 §4.1).
        builder.HasIndex(i => new { i.SchoolId, i.Name, i.Gender })
            .IsUnique()
            .HasDatabaseName("UX_uniform_items_name_gender")
            .HasFilter("\"IsDeleted\" = false");

        builder.HasIndex(i => new { i.SchoolId, i.FeeCategoryId });

        builder.HasOne<School>().WithMany().HasForeignKey(i => i.SchoolId).OnDelete(DeleteBehavior.Restrict);

        // FK composite tenant-safe : un modèle ne peut pas désigner le frais d'une autre école. Nullable → non
        // appliquée tant que FeeCategoryId est NULL (PostgreSQL, MATCH SIMPLE).
        builder.HasOne<FeeCategory>().WithMany()
            .HasForeignKey(i => new { i.SchoolId, i.FeeCategoryId })
            .HasPrincipalKey(c => new { c.SchoolId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
