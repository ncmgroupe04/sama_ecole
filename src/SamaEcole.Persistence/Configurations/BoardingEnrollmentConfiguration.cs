using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Persistence.Configurations;

public class BoardingEnrollmentConfiguration : IEntityTypeConfiguration<BoardingEnrollment>
{
    public void Configure(EntityTypeBuilder<BoardingEnrollment> builder)
    {
        builder.ToTable("boarding_enrollments", t =>
        {
            t.HasCheckConstraint("CK_boarding_enrollments_dates", "\"EndDate\" IS NULL OR \"EndDate\" >= \"StartDate\"");
            // Un demi-pensionnaire ne dort pas à l'internat (spec N7).
            t.HasCheckConstraint("CK_boarding_enrollments_half_board_no_bed", "\"Regime\" <> 'DemiPensionnaire' OR \"BedId\" IS NULL");
            // Un séjour clos libère son lit.
            t.HasCheckConstraint("CK_boarding_enrollments_inactive_no_bed", "\"IsActive\" OR \"BedId\" IS NULL");
            t.HasCheckConstraint("CK_boarding_enrollments_ended_has_end", "\"IsActive\" OR \"EndDate\" IS NOT NULL");
        });

        builder.HasKey(b => b.Id);
        builder.Property(b => b.SchoolId).IsRequired();
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(b => b.Regime).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(b => b.MedicalNotes).HasMaxLength(2000);
        builder.Property(b => b.EmergencyContactName).HasMaxLength(150);
        builder.Property(b => b.EmergencyContactPhone).HasMaxLength(30);

        // Personnes habilitées : petite liste sans cycle de vie propre → JSON dans la ligne du séjour.
        builder.OwnsMany(b => b.AllowedExitPersons, o => o.ToJson("AllowedExitPersons"));

        builder.HasAlternateKey(b => new { b.SchoolId, b.Id });

        // Un séjour actif par inscription, un séjour actif par lit : tenus par la BASE (spec N3).
        builder.HasIndex(b => new { b.SchoolId, b.EnrollmentId }).IsUnique()
            .HasDatabaseName("UX_boarding_enrollments_active_enrollment")
            .HasFilter("\"IsActive\" AND NOT \"IsDeleted\"");
        builder.HasIndex(b => b.BedId).IsUnique()
            .HasDatabaseName("UX_boarding_enrollments_active_bed")
            .HasFilter("\"IsActive\" AND NOT \"IsDeleted\" AND \"BedId\" IS NOT NULL");
        builder.HasIndex(b => new { b.SchoolId, b.StudentId });

        builder.HasOne<School>().WithMany().HasForeignKey(b => b.SchoolId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Student>().WithMany()
            .HasForeignKey(b => new { b.SchoolId, b.StudentId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Enrollment>().WithMany()
            .HasForeignKey(b => new { b.SchoolId, b.EnrollmentId })
            .HasPrincipalKey(e => new { e.SchoolId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Bed>().WithMany()
            .HasForeignKey(b => new { b.SchoolId, b.BedId })
            .HasPrincipalKey(x => new { x.SchoolId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
