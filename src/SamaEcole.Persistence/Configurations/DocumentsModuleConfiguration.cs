using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

// Module Documents administratifs — même convention que SurveillantConfiguration : index composite
// (SchoolId, clé étrangère) pour les lectures tenant, longueurs bornées, précision monétaire (12,2)
// comme Enrollment.TotalDue. La policy RLS de chaque table est posée dans la migration
// AddDocumentsModuleEntities (AGENTS.md règle #2 : filtre EF + RLS, jamais un seul).

public class EarlyDepartureConfiguration : IEntityTypeConfiguration<EarlyDeparture>
{
    public void Configure(EntityTypeBuilder<EarlyDeparture> builder)
    {
        builder.HasIndex(e => new { e.SchoolId, e.StudentId });
        builder.Property(e => e.Reason).HasMaxLength(1000);
        builder.Property(e => e.PickedUpBy).HasMaxLength(200);
        // Date pure (saisie "JJ/MM/AAAA"), pas un instant : mappée en "date" plutôt que le "timestamp
        // with time zone" par défaut de Npgsql pour DateTime, qui rejette tout DateTime dont le Kind
        // n'est pas explicitement Utc (le JSON entrant a Kind=Unspecified) — voir LateArrivalConfiguration.
        builder.Property(e => e.Date).HasColumnType("date");
    }
}

public class ParentSummonsConfiguration : IEntityTypeConfiguration<ParentSummons>
{
    public void Configure(EntityTypeBuilder<ParentSummons> builder)
    {
        builder.HasIndex(e => new { e.SchoolId, e.StudentId });
        builder.Property(e => e.Reason).HasMaxLength(1000);
    }
}

public class FinancialCommitmentConfiguration : IEntityTypeConfiguration<FinancialCommitment>
{
    public void Configure(EntityTypeBuilder<FinancialCommitment> builder)
    {
        builder.HasIndex(e => new { e.SchoolId, e.EnrollmentId });
        builder.Property(e => e.Amount).IsRequired().HasPrecision(12, 2);
        builder.Property(e => e.Terms).HasMaxLength(2000);
    }
}

public class TeacherHourRecordConfiguration : IEntityTypeConfiguration<TeacherHourRecord>
{
    public void Configure(EntityTypeBuilder<TeacherHourRecord> builder)
    {
        builder.HasIndex(e => new { e.SchoolId, e.EmployeeContractId });
        builder.Property(e => e.Hours).IsRequired().HasPrecision(6, 2);
        builder.Property(e => e.Note).HasMaxLength(500);
    }
}
