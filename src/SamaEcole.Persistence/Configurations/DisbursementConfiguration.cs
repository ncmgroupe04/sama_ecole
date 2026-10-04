using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Persistence.Configurations;

public class DisbursementConfiguration : IEntityTypeConfiguration<Disbursement>
{
    public void Configure(EntityTypeBuilder<Disbursement> builder)
    {
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Reason)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(d => d.Category)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(d => d.Amount)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(d => d.VatRate)
            .HasColumnType("decimal(5,4)");

        builder.Property(d => d.VatAmount)
            .HasColumnType("decimal(18,2)")
            .IsRequired()
            .HasDefaultValue(0m);

        // Un taux de TVA (fraction) ne peut être négatif ni dépasser 100 % — garde-fou en base, en plus
        // de la validation applicative.
        builder.ToTable(t => t.HasCheckConstraint("CK_disbursements_vat_rate_range", "\"VatRate\" IS NULL OR (\"VatRate\" >= 0 AND \"VatRate\" <= 1)"));

        builder.Property(d => d.PaymentMethod)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(d => d.Beneficiary)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(d => d.ReceiptUrl)
            .HasMaxLength(500);

        builder.HasIndex(d => new { d.SchoolId, d.Date });

        // Contre-écriture : au plus UNE par décaissement d'origine (une double annulation, même concurrente,
        // est refusée par la base), toujours en montant négatif, et l'origine ne peut jamais être effacée.
        builder.HasIndex(d => d.ReversalOfId)
            .IsUnique()
            .HasFilter("\"ReversalOfId\" IS NOT NULL");

        builder.HasOne<Disbursement>()
            .WithMany()
            .HasForeignKey(d => d.ReversalOfId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_disbursements_reversal_negative", "\"ReversalOfId\" IS NULL OR \"Amount\" < 0"));
    }
}
