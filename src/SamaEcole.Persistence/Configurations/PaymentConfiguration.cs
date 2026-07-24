using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Ticket JGK-F02 — encaissements (table `payments`). Le Global Query Filter (SchoolId + IsDeleted) est
/// posé automatiquement par ApplicationDbContext pour toute ITenantEntity ; la policy RLS PostgreSQL
/// équivalente est posée par la migration AddPayments, qui inscrit aussi `payments` dans les tables
/// protégées (AGENTS.md règle #2 : les DEUX protections, jamais une seule).
///
/// Le paiement ne porte PAS de verrou optimiste : le solde vit sur l'inscription, et c'est le xmin
/// d'`enrollments` (via AmountPaid) qui sérialise deux encaissements concurrents (règle #5).
///
/// FK COMPOSITE (SchoolId, EnrollmentId) : sur la seule colonne d'inscription, rien n'empêcherait un
/// paiement de viser l'inscription d'une AUTRE école. En incluant SchoolId des deux côtés, le croisement
/// de tenants devient structurellement impossible — même défense que enrollments → students (JGK-E01).
/// </summary>
public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.SchoolId).IsRequired();

        builder.Property(p => p.Amount).IsRequired().HasPrecision(12, 2);
        builder.Property(p => p.BalanceAfter).IsRequired().HasPrecision(12, 2);
        builder.Property(p => p.Method).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Category).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(p => p.ReferencePeriod).HasMaxLength(20);
        builder.Property(p => p.ReceiptNumber).HasMaxLength(50).IsRequired();
        builder.Property(p => p.ReceivedByUserId).IsRequired();
        builder.Property(p => p.PaidAt).IsRequired();

        // Le montant versé est strictement positif : garde-fou en base, en plus de la validation applicative.
        builder.ToTable(t => t.HasCheckConstraint("CK_payments_amount_positive", "\"Amount\" > 0"));

        // Numéro de reçu officiel : unique par établissement, jamais réémis (même registre que le reçu
        // d'inscription, JGK-E02). L'index n'est pas filtré : un numéro consommé reste consommé.
        builder.HasIndex(p => new { p.SchoolId, p.ReceiptNumber })
            .IsUnique()
            .HasDatabaseName("UX_payments_receipt_number");

        // Lister les versements d'une inscription (relevé de compte, futur écran caisse).
        builder.HasIndex(p => new { p.SchoolId, p.EnrollmentId })
            .HasDatabaseName("IX_payments_SchoolId_EnrollmentId");

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(p => p.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Enrollment)
            .WithMany()
            .HasForeignKey(p => new { p.SchoolId, p.EnrollmentId })
            .HasPrincipalKey(e => new { e.SchoolId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.CashierSession)
            .WithMany()
            .HasForeignKey(p => new { p.SchoolId, p.CashierSessionId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // ReceivedByUserId reste une référence AUDIT (l'utilisateur qui a encaissé), toujours renseignée
        // depuis le JWT. Volontairement sans FK dure vers `users` : la valeur est de confiance (serveur),
        // et on évite l'interaction FK × RLS lors de l'INSERT sous le rôle applicatif NOBYPASSRLS.
    }
}
