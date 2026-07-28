using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Table plateforme (comme SubscriptionConfiguration) : aucun Global Query Filter SchoolId, aucune
/// policy RLS — un code promo n'appartient à aucune école.
///
/// Verrou optimiste xmin (AGENTS.md règle #5, même recette que ClassFeeConfiguration) : protège
/// l'incrémentation concurrente de CurrentUses contre un dépassement de MaxUses en cas de course.
/// </summary>
public class PromoCodeConfiguration : IEntityTypeConfiguration<PromoCode>
{
    public void Configure(EntityTypeBuilder<PromoCode> builder)
    {
        builder.ToTable("promo_codes");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Code).IsRequired().HasMaxLength(32);
        builder.Property(p => p.DiscountType).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.DiscountValue).HasPrecision(12, 2);

        // Normalisé en majuscules par le Handler avant écriture — l'unicité porte donc bien sur le
        // code tel qu'un utilisateur le saisirait, indépendamment de la casse.
        builder.HasIndex(p => p.Code).IsUnique();

        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}
