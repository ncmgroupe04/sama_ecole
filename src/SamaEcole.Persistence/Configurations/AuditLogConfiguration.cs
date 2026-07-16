using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Journal d'audit append-only (ticket JGK-H01).
/// Table tenant : policy RLS + Global Query Filter, comme toute donnée d'établissement (règle #2).
/// </summary>
public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.SchoolId).IsRequired();
        builder.Property(a => a.UserId).IsRequired();
        builder.Property(a => a.Module).IsRequired().HasMaxLength(50);
        builder.Property(a => a.Action).IsRequired().HasMaxLength(100);
        builder.Property(a => a.Success).IsRequired();
        builder.Property(a => a.FailureReason).HasMaxLength(1000);
        builder.Property(a => a.IpAddress).HasMaxLength(45); // IPv6 max
        builder.Property(a => a.OccurredAt).IsRequired();

        // Chemin d'accès principal : « le journal de CETTE école, du plus récent au plus ancien ».
        builder.HasIndex(a => new { a.SchoolId, a.OccurredAt });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
