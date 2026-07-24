using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

public class DisciplineRecordConfiguration : IEntityTypeConfiguration<DisciplineRecord>
{
    public void Configure(EntityTypeBuilder<DisciplineRecord> builder)
    {
        builder.HasIndex(e => new { e.SchoolId, e.StudentId });
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(50);
        builder.Property(e => e.Reason).HasMaxLength(1000);
    }
}

public class AbsenceJustificationConfiguration : IEntityTypeConfiguration<AbsenceJustification>
{
    public void Configure(EntityTypeBuilder<AbsenceJustification> builder)
    {
        builder.HasIndex(e => new { e.SchoolId, e.StudentId });
        builder.Property(e => e.Reason).HasMaxLength(1000);
    }
}

public class LateArrivalConfiguration : IEntityTypeConfiguration<LateArrival>
{
    public void Configure(EntityTypeBuilder<LateArrival> builder)
    {
        builder.HasIndex(e => new { e.SchoolId, e.StudentId });
        builder.Property(e => e.Reason).HasMaxLength(1000);
    }
}

public class TeacherAttendanceConfiguration : IEntityTypeConfiguration<TeacherAttendance>
{
    public void Configure(EntityTypeBuilder<TeacherAttendance> builder)
    {
        builder.HasIndex(e => new { e.SchoolId, e.TeacherId, e.Date }).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(50);
        builder.Property(e => e.Reason).HasMaxLength(1000);
    }
}
