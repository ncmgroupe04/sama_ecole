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
        // Date pure (saisie "JJ/MM/AAAA" côté vue), pas un instant : mappée en "date" plutôt que le
        // "timestamp with time zone" par défaut de Npgsql pour DateTime, qui rejette tout DateTime dont
        // le Kind n'est pas explicitement Utc (le JSON entrant a Kind=Unspecified) — voir LateArrival.
        builder.Property(e => e.Date).HasColumnType("date");
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
        builder.Property(e => e.Observations).HasMaxLength(1000);
        // Voir DisciplineRecordConfiguration.Date : une date pure, mappée en "date" pour éviter le rejet
        // Npgsql "Cannot write DateTime with Kind=Unspecified to timestamp with time zone" (le JSON
        // entrant "2026-07-26" n'a pas de Kind Utc explicite).
        builder.Property(e => e.Date).HasColumnType("date");
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
