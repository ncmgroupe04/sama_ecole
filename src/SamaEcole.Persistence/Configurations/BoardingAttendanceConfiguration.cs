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

        // Un pointage par élève, par session et par jour (index partiel : une saisie corrigée se recrée).
        builder.HasIndex(a => new { a.StudentId, a.SessionId, a.Date }).IsUnique()
            .HasDatabaseName("UX_boarding_attendances_session_day")
            .HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(a => new { a.SchoolId, a.SessionId, a.Date });
        builder.HasIndex(a => new { a.SchoolId, a.Date });
        builder.HasIndex(a => new { a.SchoolId, a.BoardingEnrollmentId });

        builder.HasOne<School>().WithMany().HasForeignKey(a => a.SchoolId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AttendanceSession>().WithMany()
            .HasForeignKey(a => new { a.SchoolId, a.SessionId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Student>().WithMany()
            .HasForeignKey(a => new { a.SchoolId, a.StudentId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<BoardingEnrollment>().WithMany()
            .HasForeignKey(a => new { a.SchoolId, a.BoardingEnrollmentId })
            .HasPrincipalKey(b => new { b.SchoolId, b.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
