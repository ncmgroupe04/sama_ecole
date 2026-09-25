using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par ApplicationDbContext.OnModelCreating
/// à toute entité ITenantEntity. La policy RLS équivalente vit dans la migration AddStudentSubjectExemptions
/// (AGENTS.md règle #2).
/// </summary>
public class StudentSubjectExemptionConfiguration : IEntityTypeConfiguration<StudentSubjectExemption>
{
    public void Configure(EntityTypeBuilder<StudentSubjectExemption> builder)
    {
        builder.ToTable("student_subject_exemptions");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.SchoolId).IsRequired();

        // Même borne que ExemptionRules.MaxReasonLength.
        builder.Property(e => e.Reason).IsRequired().HasMaxLength(200);

        // Une dispense par (élève, matière, année). Index PARTIEL : refaire la liste ne se heurte pas à la ligne archivée.
        builder.HasIndex(e => new { e.SchoolId, e.StudentId, e.SubjectId, e.SchoolYearId })
            .IsUnique()
            .HasDatabaseName("UX_student_subject_exemptions_key")
            .HasFilter("NOT \"IsDeleted\"");

        // « Quels élèves sont dispensés de cette matière cette année ? » (grilles, import, fiches).
        builder.HasIndex(e => new { e.SchoolId, e.SubjectId, e.SchoolYearId });

        builder.HasOne<School>().WithMany().HasForeignKey(e => e.SchoolId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Student>().WithMany()
            .HasForeignKey(e => new { e.SchoolId, e.StudentId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Subject>().WithMany()
            .HasForeignKey(e => new { e.SchoolId, e.SubjectId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SchoolYear>().WithMany()
            .HasForeignKey(e => new { e.SchoolId, e.SchoolYearId })
            .HasPrincipalKey(y => new { y.SchoolId, y.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
