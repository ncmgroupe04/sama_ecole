using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Table plateforme (comme PromoCodeConfiguration) : aucun Global Query Filter SchoolId, aucune policy RLS.
/// Les enums sont persistés en TEXTE (convention du projet). La migration sème la grille de départ ; l'écran
/// Super Admin ne fait ensuite que MODIFIER ces lignes.
/// </summary>
public class QuotaTierPriceConfiguration : IEntityTypeConfiguration<QuotaTierPrice>
{
    public void Configure(EntityTypeBuilder<QuotaTierPrice> builder)
    {
        builder.ToTable("quota_tier_prices", t =>
        {
            t.HasCheckConstraint("CK_quota_tier_prices_prices_positive", "\"MonthlyXof\" > 0 AND \"AnnualXof\" > 0");
            t.HasCheckConstraint("CK_quota_tier_prices_surcharges_not_negative",
                "\"BoardingSurchargeMonthlyXof\" >= 0 AND \"BoardingSurchargeAnnualXof\" >= 0");
        });

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Tier).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.MonthlyXof).HasPrecision(12, 2);
        builder.Property(p => p.AnnualXof).HasPrecision(12, 2);
        builder.Property(p => p.BoardingSurchargeMonthlyXof).HasPrecision(12, 2);
        builder.Property(p => p.BoardingSurchargeAnnualXof).HasPrecision(12, 2);

        builder.HasIndex(p => p.Tier).IsUnique();

        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}
