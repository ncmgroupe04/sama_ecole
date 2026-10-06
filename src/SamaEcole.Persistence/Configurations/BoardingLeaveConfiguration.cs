using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Persistence.Configurations;

public class BoardingLeaveConfiguration : IEntityTypeConfiguration<BoardingLeave>
{
    public void Configure(EntityTypeBuilder<BoardingLeave> builder)
    {
        builder.ToTable("boarding_leaves", t =>
        {
            t.HasCheckConstraint("CK_boarding_leaves_expected", "\"ExpectedReturnDate\" >= \"LeaveDate\"");
            t.HasCheckConstraint("CK_boarding_leaves_actual", "\"ActualReturnDate\" IS NULL OR \"ActualReturnDate\" >= \"LeaveDate\"");
        });

        builder.HasKey(l => l.Id);
        builder.Property(l => l.SchoolId).IsRequired();
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(l => l.Reason).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(l => l.AccompaniedBy).IsRequired().HasMaxLength(150);
        builder.Property(l => l.ReasonDetail).HasMaxLength(500);

        // Une seule sortie ouverte à la fois par pensionnaire.
        builder.HasIndex(l => l.BoardingEnrollmentId).IsUnique()
            .HasDatabaseName("UX_boarding_leaves_open")
            .HasFilter("\"ActualReturnDate\" IS NULL AND NOT \"IsDeleted\"");
        builder.HasIndex(l => new { l.SchoolId, l.LeaveDate });

        builder.HasOne<School>().WithMany().HasForeignKey(l => l.SchoolId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<BoardingEnrollment>().WithMany()
            .HasForeignKey(l => new { l.SchoolId, l.BoardingEnrollmentId })
            .HasPrincipalKey(b => new { b.SchoolId, b.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
