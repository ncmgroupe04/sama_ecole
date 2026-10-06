using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par ApplicationDbContext.OnModelCreating
/// à toute entité ITenantEntity. La policy RLS équivalente vit dans la migration AddDaaraHalqaAndHizbTracking
/// (AGENTS.md règle #2 : les DEUX protections, jamais une seule).
/// </summary>
public class InstructorConfiguration : IEntityTypeConfiguration<Instructor>
{
    public void Configure(EntityTypeBuilder<Instructor> builder)
    {
        builder.ToTable("instructors");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.SchoolId).IsRequired();

        // Verrou optimiste xmin (AGENTS.md règle #5) : UpdateInstructorCommand en dépend pour refuser en 409 une
        // fiche modifiée entre-temps (statut, compte lié), comme Teacher et QuranProgress.
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(i => i.FullName).IsRequired().HasMaxLength(200);
        builder.Property(i => i.FullNameAr).HasMaxLength(200);
        builder.Property(i => i.Phone).HasMaxLength(30);

        builder.Property(i => i.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(EntityStatus.Active);

        builder.HasIndex(i => i.SchoolId);

        // Un compte de connexion ne peut être rattaché qu'à UN Oustaz vivant (même règle que
        // TeacherConfiguration, JGK-D06). Index PARTIEL : plusieurs fiches sans compte (UserId NULL), et
        // un Oustaz supprimé logiquement libère son compte (règle #6).
        builder.HasIndex(i => i.UserId)
            .IsUnique()
            .HasDatabaseName("UX_instructors_UserId")
            .HasFilter("\"UserId\" IS NOT NULL AND NOT \"IsDeleted\"");

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(i => i.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
