using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

public class PaymentBreakdownConfiguration : IEntityTypeConfiguration<PaymentBreakdown>
{
    public void Configure(EntityTypeBuilder<PaymentBreakdown> builder)
    {
        builder.ToTable("payment_breakdowns");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.AmountAllocated).IsRequired().HasPrecision(12, 2);

        // 60 caractères : la colonne « Période / Note » du reçu A5 fait 32 % d'une demi-largeur utile,
        // au-delà le libellé se replierait et doublerait la hauteur de ligne. Facultatif par conception.
        builder.Property(b => b.Label).HasMaxLength(60);

        builder.HasOne(b => b.Payment)
            .WithMany(p => p.Breakdowns)
            .HasForeignKey(b => b.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(b => b.FeeCategory)
            .WithMany()
            .HasForeignKey(b => b.FeeCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
