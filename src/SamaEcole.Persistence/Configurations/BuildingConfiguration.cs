using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL équivalente est posée par la migration AddBuildingsAndRooms (AGENTS.md
/// règle #2 : les DEUX protections, jamais une seule).
/// </summary>
public class BuildingConfiguration : IEntityTypeConfiguration<Building>
{
    public void Configure(EntityTypeBuilder<Building> builder)
    {
        builder.ToTable("buildings");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.SchoolId).IsRequired();

        // Verrou optimiste xmin (AGENTS.md règle #5), même convention que Classroom/Grade/Enrollment.
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(b => b.Name).IsRequired().HasMaxLength(100);
        builder.Property(b => b.Description).HasMaxLength(500);

        builder.HasMany(b => b.Rooms)
            .WithOne(r => r.Building)
            .HasForeignKey(r => r.BuildingId)
            .OnDelete(DeleteBehavior.Restrict);

        // Deux bâtiments ne peuvent pas porter le même nom dans la même école. Le soft delete fait
        // partie de la clé : sans lui, on ne pourrait jamais recréer un bâtiment portant le nom d'un
        // bâtiment archivé.
        builder.HasIndex(b => new { b.SchoolId, b.Name, b.IsDeleted }).IsUnique();
        builder.HasIndex(b => b.SchoolId);

        // Restrict : on ne supprime jamais physiquement une école (AGENTS.md règle #6).
        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(b => b.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
