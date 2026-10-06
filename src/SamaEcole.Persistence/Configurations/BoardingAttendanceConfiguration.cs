using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Persistence.Configurations;

public class BoardingAttendanceConfiguration : IEntityTypeConfiguration<BoardingAttendance>
{
    public void Configure(EntityTypeBuilder<BoardingAttendance> builder)
    {
        builder.ToTable("boarding_attendances");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.SchoolId).IsRequired();
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(a => a.Note).HasMaxLength(300);

        // Un pointage par pensionnaire et par nuit (index partiel : une saisie corrigée se recrée).
        builder.HasIndex(a => new { a.BoardingEnrollmentId, a.Date }).IsUnique()
            .HasDatabaseName("UX_boarding_attendances_night")
            .HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(a => new { a.SchoolId, a.Date });

        builder.HasOne<School>().WithMany().HasForeignKey(a => a.SchoolId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<BoardingEnrollment>().WithMany()
            .HasForeignKey(a => new { a.SchoolId, a.BoardingEnrollmentId })
            .HasPrincipalKey(b => new { b.SchoolId, b.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
