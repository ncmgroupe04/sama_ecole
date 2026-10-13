using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Demandes de changement d'abonnement. Table tenant : RLS PostgreSQL (migration AddSubscriptionChangeRequests) +
/// Global Query Filter posé par ApplicationDbContext. Les enums sont persistés en TEXTE (convention du projet).
/// </summary>
public class SubscriptionChangeRequestConfiguration : IEntityTypeConfiguration<SubscriptionChangeRequest>
{
    public void Configure(EntityTypeBuilder<SubscriptionChangeRequest> builder)
    {
        builder.ToTable("subscription_change_requests", t =>
        {
            t.HasCheckConstraint("CK_subscription_change_requests_target_max_positive", "\"TargetMaxStudentLimit\" > 0");
            t.HasCheckConstraint("CK_subscription_change_requests_target_soft_gte_max", "\"TargetSoftQuotaLimit\" >= \"TargetMaxStudentLimit\"");
            t.HasCheckConstraint("CK_subscription_change_requests_target_cycles_not_empty", "\"TargetCycles\" <> ''");
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(r => r.CurrentProfile).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(r => r.CurrentTier).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.CurrentCycles).HasMaxLength(60).IsRequired();

        builder.Property(r => r.TargetProfile).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(r => r.TargetEstablishmentProfile).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(r => r.TargetTier).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.TargetCycles).HasMaxLength(60).IsRequired();

        builder.Property(r => r.CurrentMonthlyXof).HasPrecision(12, 2);
        builder.Property(r => r.CurrentAnnualXof).HasPrecision(12, 2);
        builder.Property(r => r.TargetMonthlyXof).HasPrecision(12, 2);
        builder.Property(r => r.TargetAnnualXof).HasPrecision(12, 2);

        builder.Property(r => r.RequestedByName).HasMaxLength(200);
        builder.Property(r => r.RequestedByEmail).HasMaxLength(200);
        builder.Property(r => r.DirectorNote).HasMaxLength(500);
        builder.Property(r => r.ReviewNote).HasMaxLength(500);

        // UNE seule demande en attente par école : deux demandes concurrentes ne sauraient pas partir du même état de
        // départ, et la seconde validée écraserait la première. Index filtré (une demande décidée ne bloque rien).
        builder.HasIndex(r => r.SchoolId)
            .IsUnique()
            .HasDatabaseName("UX_subscription_change_requests_pending_school")
            .HasFilter("\"Status\" = 'Pending' AND NOT \"IsDeleted\"");

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(r => r.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        // Aucun HasQueryFilter ici : ITenantEntity le fait poser (SchoolId + IsDeleted) par ApplicationDbContext.
    }
}
