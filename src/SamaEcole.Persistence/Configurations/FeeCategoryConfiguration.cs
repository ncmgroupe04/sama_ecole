using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL équivalente est posée par la migration AddFees (AGENTS.md règle #2).
/// </summary>
public class FeeCategoryConfiguration : IEntityTypeConfiguration<FeeCategory>
{
    public void Configure(EntityTypeBuilder<FeeCategory> builder)
    {
        builder.ToTable("fee_categories");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.SchoolId).IsRequired();
        builder.Property(c => c.Name).IsRequired().HasMaxLength(60);
        builder.Property(c => c.IsRecurring).IsRequired();

        // Une catégorie est unique par nom au sein de l'école. Le soft delete fait partie de la clé :
        // sans lui, une catégorie archivée interdirait d'en recréer une de même nom.
        builder.HasIndex(c => new { c.SchoolId, c.Name, c.IsDeleted }).IsUnique();

        // Clé alternative (SchoolId, Id) : elle sert de cible à la FK COMPOSITE de class_fees, qui
        // référence une catégorie ET son école à la fois. Sans elle, une ligne de barème pourrait
        // pointer une catégorie d'une AUTRE école (la RLS masque, mais n'empêche pas d'exister).
        builder.HasAlternateKey(c => new { c.SchoolId, c.Id });

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(c => c.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
