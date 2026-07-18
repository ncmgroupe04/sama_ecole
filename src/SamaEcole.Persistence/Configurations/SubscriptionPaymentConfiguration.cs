using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Ticket JGK-I05 — paiements d'abonnement (table `subscription_payments`). Table TENANT normale (le
/// Global Query Filter est posé automatiquement par ApplicationDbContext pour toute ITenantEntity ; la
/// policy RLS équivalente est posée par la migration AddSubscriptionPayments) — contrairement à
/// `subscriptions`, cette table est écrite directement par le Directeur, pas via une fonction SECURITY
/// DEFINER (AGENTS.md règle #2 : les DEUX protections, jamais une seule).
///
/// `SubscriptionId` référence `Subscriptions.Id` en simple colonne (pas de FK composite avec SchoolId,
/// à la différence de payments → enrollments) : ce n'est JAMAIS une valeur fournie par le client — le
/// Handler résout systématiquement l'abonnement du tenant courant avant d'écrire cette ligne, il n'y a
/// donc aucun risque de traverser un autre établissement à défendre ici.
/// </summary>
public class SubscriptionPaymentConfiguration : IEntityTypeConfiguration<SubscriptionPayment>
{
    public void Configure(EntityTypeBuilder<SubscriptionPayment> builder)
    {
        builder.ToTable("subscription_payments");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.SchoolId).IsRequired();
        builder.Property(p => p.SubscriptionId).IsRequired();

        builder.Property(p => p.Amount).IsRequired().HasPrecision(12, 2);
        builder.Property(p => p.Currency).IsRequired().HasMaxLength(3);
        builder.Property(p => p.Method).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.BillingPeriod).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Provider).IsRequired().HasMaxLength(50);
        builder.Property(p => p.ProviderTransactionRef).HasMaxLength(100);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.WebhookPayloadRaw).HasColumnType("jsonb");
        builder.Property(p => p.InitiatedAt).IsRequired();

        // Le montant facturé est strictement positif : garde-fou en base, en plus du calcul serveur.
        builder.ToTable(t => t.HasCheckConstraint("CK_subscription_payments_amount_positive", "\"Amount\" > 0"));

        // Déduplication du futur webhook (JGK-I06, docs/Volume_3_DDS.md §5.8) : un même
        // ProviderTransactionRef ne doit jamais correspondre à deux lignes. Index PARTIEL (WHERE NOT NULL) :
        // rien n'empêche en théorie deux échecs très précoces sans référence encore attribuée.
        builder.HasIndex(p => p.ProviderTransactionRef)
            .IsUnique()
            .HasFilter("\"ProviderTransactionRef\" IS NOT NULL")
            .HasDatabaseName("UX_subscription_payments_provider_transaction_ref");

        builder.HasIndex(p => new { p.SchoolId, p.InitiatedAt })
            .HasDatabaseName("IX_subscription_payments_SchoolId_InitiatedAt");

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(p => p.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Subscription>()
            .WithMany()
            .HasForeignKey(p => p.SubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Aucun HasQueryFilter ici : SubscriptionPayment implémente ITenantEntity, le filtre
        // (SchoolId + IsDeleted) est posé automatiquement par ApplicationDbContext.OnModelCreating
        // pour TOUTE entité de ce type (voir PaymentConfiguration, même pattern).
    }
}
