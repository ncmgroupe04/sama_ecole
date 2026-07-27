using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("subscriptions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Plan).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);

        // Un seul abonnement par école (docs/Volume_3_DDS.md §5.6).
        builder.HasIndex(s => s.SchoolId).IsUnique();

        // FK vers schools (docs/Volume_3_DDS.md §5.6).
        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(s => s.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        // Dernier code promo bénéficié (module Tarification & Promotions) — simple traçabilité,
        // Restrict comme les autres FK de ce fichier : un code promo utilisé ne doit jamais pouvoir
        // disparaître silencieusement sous un abonnement qui le référence encore.
        builder.HasOne<PromoCode>()
            .WithMany()
            .HasForeignKey(s => s.PromoCodeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Aucun HasQueryFilter ici : Subscription implémente ITenantEntity, le filtre
        // (SchoolId + IsDeleted) est posé automatiquement par ApplicationDbContext.OnModelCreating —
        // EF Core n'accepte qu'un seul filtre par entité, un appel local l'écraserait ou serait écrasé.
        // Même pattern que SubscriptionPaymentConfiguration.
    }
}