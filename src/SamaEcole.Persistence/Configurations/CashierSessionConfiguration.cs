using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

public class CashierSessionConfiguration : IEntityTypeConfiguration<CashierSession>
{
    public void Configure(EntityTypeBuilder<CashierSession> builder)
    {
        builder.ToTable("cashier_sessions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.SchoolId).IsRequired();

        builder.Property(s => s.CashierId).IsRequired();
        builder.Property(s => s.OpenedAt).IsRequired();
        builder.Property(s => s.OpeningBalance).IsRequired().HasPrecision(12, 2);
        builder.Property(s => s.ClosingBalance).HasPrecision(12, 2);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // One cashier can only have one open session at a time
        builder.HasIndex(s => new { s.SchoolId, s.CashierId })
            .IsUnique()
            .HasFilter("\"Status\" = 'Open'")
            .HasDatabaseName("UX_cashier_sessions_SchoolId_CashierId_Open");

        builder.HasOne(s => s.Cashier)
            .WithMany()
            .HasForeignKey(s => s.CashierId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
