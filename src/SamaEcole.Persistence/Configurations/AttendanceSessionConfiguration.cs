using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Persistence.Configurations;

public class AttendanceSessionConfiguration : IEntityTypeConfiguration<AttendanceSession>
{
    public void Configure(EntityTypeBuilder<AttendanceSession> builder)
    {
        builder.ToTable("attendance_sessions", t =>
        {
            t.HasCheckConstraint("CK_attendance_sessions_days", "\"Days\" BETWEEN 1 AND 127");
        });

        builder.HasKey(s => s.Id);
        builder.Property(s => s.SchoolId).IsRequired();
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(s => s.Name).HasMaxLength(60).IsRequired();
        builder.Property(s => s.Audience).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.TimeLabel).HasMaxLength(40);

        builder.HasAlternateKey(s => new { s.SchoolId, s.Id });

        // Un nom par école parmi les sessions vivantes (une session supprimée libère son nom).
        builder.HasIndex(s => new { s.SchoolId, s.Name }).IsUnique()
            .HasDatabaseName("UX_attendance_sessions_name")
            .HasFilter("\"IsDeleted\" = false");

        builder.HasOne<School>().WithMany().HasForeignKey(s => s.SchoolId).OnDelete(DeleteBehavior.Restrict);
    }
}
