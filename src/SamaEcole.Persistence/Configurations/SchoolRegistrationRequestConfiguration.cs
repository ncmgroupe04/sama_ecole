using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Demandes d'inscription self-service (ticket JGK-I01, docs/Volume_3_DDS.md §5.7).
///
/// Table PLATEFORME, hors RLS (comme schools/subscriptions) : aucune policy ni Global Query Filter par
/// SchoolId — la demande PRÉCÈDE l'existence de l'école. N'implémente pas ITenantEntity, ne reçoit donc
/// pas le filtre automatique posé par ApplicationDbContext.OnModelCreating. Le filtre de soft delete est
/// donc posé explicitement ici (comme SchoolConfiguration/SubscriptionConfiguration).
/// </summary>
public class SchoolRegistrationRequestConfiguration : IEntityTypeConfiguration<SchoolRegistrationRequest>
{
    public void Configure(EntityTypeBuilder<SchoolRegistrationRequest> builder)
    {
        builder.ToTable("school_registration_requests");

        builder.HasKey(r => r.Id);

        // Unicité tenue par la BASE : c'est elle, pas le pré-contrôle applicatif, qui garantit que
        // deux demandes ne partagent jamais la même référence de suivi (critère du ticket JGK-I01).
        builder.Property(r => r.TrackingReference).IsRequired().HasMaxLength(20);
        builder.HasIndex(r => r.TrackingReference).IsUnique();

        builder.Property(r => r.DirectorFullName).IsRequired().HasMaxLength(200);
        builder.Property(r => r.DirectorEmail).IsRequired().HasMaxLength(200);
        builder.Property(r => r.DirectorPhone).IsRequired().HasMaxLength(30);
        builder.Property(r => r.DirectorPasswordHash).IsRequired().HasMaxLength(500);

        builder.Property(r => r.SchoolName).IsRequired().HasMaxLength(200);
        builder.Property(r => r.SchoolAddress).HasMaxLength(300);
        builder.Property(r => r.City).HasMaxLength(100);
        builder.Property(r => r.Region).HasMaxLength(100);

        builder.Property(r => r.RequestedPlan).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.RejectionReason).HasColumnType("text");

        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}
