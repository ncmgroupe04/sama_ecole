using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Souscription commerciale d'un établissement (profil + tranche d'effectif + modules). Table tenant :
/// RLS PostgreSQL (migration AddTenantSubscriptions) + Global Query Filter posé par ApplicationDbContext.
/// </summary>
public class TenantSubscriptionConfiguration : IEntityTypeConfiguration<TenantSubscription>
{
    public void Configure(EntityTypeBuilder<TenantSubscription> builder)
    {
        builder.ToTable("tenant_subscriptions", t =>
        {
            // Filets en base : un plafond nul ou une tolérance inférieure au plafond rendrait le contrôle
            // de quota incohérent, quel que soit le chemin d'écriture (EF, fonction SQL du Super Admin).
            t.HasCheckConstraint("CK_tenant_subscriptions_max_positive", "\"MaxStudentLimit\" > 0");
            t.HasCheckConstraint("CK_tenant_subscriptions_soft_gte_max", "\"SoftQuotaLimit\" >= \"MaxStudentLimit\"");
        });

        builder.HasKey(s => s.Id);

        // Enums en TEXTE : convention du projet, jamais un entier qui se briserait si l'ordre changeait.
        builder.Property(s => s.ProfileType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(s => s.StudentQuotaTier).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(s => s.MaxStudentLimit).IsRequired();
        builder.Property(s => s.SoftQuotaLimit).IsRequired();

        // Une seule souscription VIVANTE par école. Index filtré sur IsDeleted : une ligne supprimée
        // logiquement ne doit pas bloquer la recréation (même patron que les autres index uniques).
        builder.HasIndex(s => s.SchoolId)
            .IsUnique()
            .HasDatabaseName("UX_tenant_subscriptions_SchoolId")
            .HasFilter("NOT \"IsDeleted\"");

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(s => s.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        // Aucun HasQueryFilter ici : ITenantEntity le fait poser (SchoolId + IsDeleted) par
        // ApplicationDbContext.OnModelCreating — EF n'accepte qu'un filtre par entité.
    }
}
