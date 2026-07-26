using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL équivalente est posée par la migration AddBuildingsAndRooms (AGENTS.md
/// règle #2 : les DEUX protections, jamais une seule).
/// </summary>
public class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> builder)
    {
        builder.ToTable("rooms");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.SchoolId).IsRequired();

        // Verrou optimiste xmin (AGENTS.md règle #5), même convention que Classroom/Grade/Enrollment.
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(r => r.Name).IsRequired().HasMaxLength(100);

        // Persisté en string, comme tous les enums métier (cf. ClassroomConfiguration.Cycle).
        builder.Property(r => r.Type)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(RoomType.SalleDeClasse);

        // Deux salles ne peuvent pas porter le même nom DANS LE MÊME BÂTIMENT — mais « Salle 1 » peut
        // exister dans deux bâtiments différents de la même école.
        builder.HasIndex(r => new { r.SchoolId, r.BuildingId, r.Name, r.IsDeleted }).IsUnique();
        builder.HasIndex(r => new { r.SchoolId, r.BuildingId });

        // Restrict : on ne supprime jamais physiquement une école (AGENTS.md règle #6).
        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(r => r.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
