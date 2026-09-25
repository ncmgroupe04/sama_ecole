using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity. La policy RLS PostgreSQL
/// équivalente vit dans la migration AddClassSubjectsAndOptions (AGENTS.md règle #2).
/// </summary>
public class ClassSubjectConfiguration : IEntityTypeConfiguration<ClassSubject>
{
    public void Configure(EntityTypeBuilder<ClassSubject> builder)
    {
        builder.ToTable("class_subjects");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.SchoolId).IsRequired();

        // Verrou optimiste xmin (règle #5) : désactiver une matière ou changer son groupe d'options modifie les
        // bulletins ; deux Directeurs concurrents obtiennent un 409, jamais un écrasement silencieux.
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(c => c.OptionGroup).HasMaxLength(40);
        builder.Property(c => c.IsActive).HasDefaultValue(true);

        // Une matière figure une seule fois au programme d'une classe. Index PARTIEL : une ligne archivée ne
        // bloque pas un nouvel ajout (même convention que SubjectConfiguration).
        builder.HasIndex(c => new { c.ClassroomId, c.SubjectId })
            .IsUnique()
            .HasDatabaseName("UX_class_subjects_classroom_subject")
            .HasFilter("NOT \"IsDeleted\"");

        builder.HasIndex(c => c.SchoolId);

        // FK COMPOSITES tenant-safe (même motif que SubjectCoefficientOverrideConfiguration).
        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(c => c.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Classroom>()
            .WithMany()
            .HasForeignKey(c => new { c.SchoolId, c.ClassroomId })
            .HasPrincipalKey(c => new { c.SchoolId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Subject>()
            .WithMany()
            .HasForeignKey(c => new { c.SchoolId, c.SubjectId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
