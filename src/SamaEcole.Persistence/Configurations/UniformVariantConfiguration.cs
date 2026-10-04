using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Voir <see cref="UniformItemConfiguration"/> pour le filtre tenant et la policy RLS. Pas de verrou xmin : une
/// taille se renomme ou se réordonne sans enjeu d'écrasement ; le stock, lui, vit sur le lot d'inventaire.
/// </summary>
public class UniformVariantConfiguration : IEntityTypeConfiguration<UniformVariant>
{
    public void Configure(EntityTypeBuilder<UniformVariant> builder)
    {
        builder.ToTable("uniform_variants");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.SchoolId).IsRequired();

        builder.Property(v => v.SizeLabel).IsRequired().HasMaxLength(30);

        // Une taille par libellé et par modèle ; un lot d'inventaire ne porte qu'une taille. Index PARTIELS :
        // archiver une taille libère son libellé et son lot sans toucher à l'historique.
        builder.HasIndex(v => new { v.SchoolId, v.UniformItemId, v.SizeLabel })
            .IsUnique()
            .HasDatabaseName("UX_uniform_variants_item_size")
            .HasFilter("\"IsDeleted\" = false");

        builder.HasIndex(v => new { v.SchoolId, v.InventoryItemId })
            .IsUnique()
            .HasDatabaseName("UX_uniform_variants_inventory_item")
            .HasFilter("\"IsDeleted\" = false");

        builder.HasOne<School>().WithMany().HasForeignKey(v => v.SchoolId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UniformItem>().WithMany()
            .HasForeignKey(v => new { v.SchoolId, v.UniformItemId })
            .HasPrincipalKey(i => new { i.SchoolId, i.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<InventoryItem>().WithMany()
            .HasForeignKey(v => new { v.SchoolId, v.InventoryItemId })
            .HasPrincipalKey(i => new { i.SchoolId, i.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
