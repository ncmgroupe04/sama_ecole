using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity. La policy RLS PostgreSQL
/// équivalente vit dans la migration AddClassSubjectsAndOptions (AGENTS.md règle #2).
/// </summary>
public class StudentSubjectEnrollmentConfiguration : IEntityTypeConfiguration<StudentSubjectEnrollment>
{
    public void Configure(EntityTypeBuilder<StudentSubjectEnrollment> builder)
    {
        builder.ToTable("student_subject_enrollments");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.SchoolId).IsRequired();

        // Verrou optimiste xmin (règle #5), comme toute table qui pilote un bulletin.
        builder.Property<uint>("xmin").IsRowVersion();

        // Une option retenue une seule fois par élève et par année. Index PARTIEL : changer d'avis puis revenir
        // au premier choix ne se heurte pas à la ligne archivée.
        builder.HasIndex(e => new { e.StudentId, e.ClassSubjectId, e.SchoolYearId })
            .IsUnique()
            .HasDatabaseName("UX_student_subject_enrollments_choice")
            .HasFilter("NOT \"IsDeleted\"");

        builder.HasIndex(e => e.SchoolId);

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(e => e.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Student>()
            .WithMany()
            .HasForeignKey(e => new { e.SchoolId, e.StudentId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ClassSubject>()
            .WithMany()
            .HasForeignKey(e => new { e.SchoolId, e.ClassSubjectId })
            .HasPrincipalKey(c => new { c.SchoolId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SchoolYear>()
            .WithMany()
            .HasForeignKey(e => new { e.SchoolId, e.SchoolYearId })
            .HasPrincipalKey(y => new { y.SchoolId, y.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
