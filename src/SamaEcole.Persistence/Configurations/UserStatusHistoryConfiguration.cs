using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Journal append-only des changements de statut (ticket JGK-A05).
/// Table tenant : policy RLS + Global Query Filter, comme toute donnée d'établissement (règle #2).
/// </summary>
public class UserStatusHistoryConfiguration : IEntityTypeConfiguration<UserStatusHistory>
{
    public void Configure(EntityTypeBuilder<UserStatusHistory> builder)
    {
        builder.ToTable("user_status_history");

        builder.HasKey(h => h.Id);
        builder.Property(h => h.SchoolId).IsRequired();
        builder.Property(h => h.UserId).IsRequired();
        builder.Property(h => h.PreviousStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.NewStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.Reason).IsRequired().HasMaxLength(500);
        builder.Property(h => h.ChangedByUserId).IsRequired();
        builder.Property(h => h.ChangedAt).IsRequired();

        // Chemin d'accès principal : « l'historique de CE compte, du plus récent au plus ancien ».
        builder.HasIndex(h => new { h.UserId, h.ChangedAt });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(h => h.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
