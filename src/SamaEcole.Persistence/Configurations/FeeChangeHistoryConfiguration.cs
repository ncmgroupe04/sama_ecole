using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Journal append-only des modifications de barème (ticket JGK-F01).
/// Table tenant : policy RLS + Global Query Filter, comme toute donnée d'établissement (règle #2).
/// L'interdiction d'UPDATE/DELETE est imposée par la migration AddFees (GRANT SELECT, INSERT seul).
/// </summary>
public class FeeChangeHistoryConfiguration : IEntityTypeConfiguration<FeeChangeHistory>
{
    public void Configure(EntityTypeBuilder<FeeChangeHistory> builder)
    {
        builder.ToTable("fee_change_history");

        builder.HasKey(h => h.Id);
        builder.Property(h => h.SchoolId).IsRequired();
        builder.Property(h => h.ClassFeeId).IsRequired();
        builder.Property(h => h.OldAmount).HasPrecision(12, 2);
        builder.Property(h => h.NewAmount).IsRequired().HasPrecision(12, 2);
        builder.Property(h => h.ChangedByUserId).IsRequired();
        builder.Property(h => h.ChangedAt).IsRequired();

        // Chemin d'accès principal : « l'historique de CETTE ligne de barème, du plus récent au plus
        // ancien ».
        builder.HasIndex(h => new { h.ClassFeeId, h.ChangedAt });

        builder.HasOne<ClassFee>()
            .WithMany()
            .HasForeignKey(h => h.ClassFeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
