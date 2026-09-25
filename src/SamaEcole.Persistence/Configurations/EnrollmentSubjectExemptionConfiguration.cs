using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity. La policy RLS PostgreSQL
/// équivalente vit dans la migration AddOptionalSubjects (AGENTS.md règle #2).
/// </summary>
public class EnrollmentSubjectExemptionConfiguration : IEntityTypeConfiguration<EnrollmentSubjectExemption>
{
    public void Configure(EntityTypeBuilder<EnrollmentSubjectExemption> builder)
    {
        builder.ToTable("enrollment_subject_exemptions");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.SchoolId).IsRequired();

        // Motif d'une dispense de matière obligatoire — même borne que OptionSelectionRules.MaxReasonLength.
        builder.Property(e => e.Reason).HasMaxLength(200);

        // Une dispense par (inscription, matière). Index PARTIEL (« NOT IsDeleted ») — même convention que
        // SubjectCoefficientOverrideConfiguration : refaire son choix ne doit pas se heurter à la ligne archivée.
        builder.HasIndex(e => new { e.SchoolId, e.EnrollmentId, e.SubjectId })
            .IsUnique()
            .HasDatabaseName("UX_enrollment_subject_exemptions_key")
            .HasFilter("NOT \"IsDeleted\"");

        // « Quels élèves sont dispensés de cette matière cette année ? » (feuilles de notes).
        builder.HasIndex(e => new { e.SchoolId, e.SubjectId });

        // FK COMPOSITES tenant-safe : le croisement de tenants devient structurellement impossible.
        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(e => e.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Enrollment>()
            .WithMany()
            .HasForeignKey(e => new { e.SchoolId, e.EnrollmentId })
            .HasPrincipalKey(x => new { x.SchoolId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Subject>()
            .WithMany()
            .HasForeignKey(e => new { e.SchoolId, e.SubjectId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
