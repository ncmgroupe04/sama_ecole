using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL équivalente est posée par la migration AddDebtorReminderBatches (règle #2).
/// </summary>
public class DebtorReminderBatchItemConfiguration : IEntityTypeConfiguration<DebtorReminderBatchItem>
{
    public void Configure(EntityTypeBuilder<DebtorReminderBatchItem> builder)
    {
        builder.ToTable("debtor_reminder_batch_items");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.SchoolId).IsRequired();
        builder.Property(i => i.GuardianPhone).HasMaxLength(20);
        builder.Property(i => i.RemainingBalance).IsRequired().HasPrecision(12, 2);
        builder.Property(i => i.DaysOverdue).IsRequired();

        builder.HasIndex(i => new { i.SchoolId, i.DebtorReminderBatchId });

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(i => i.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.DebtorReminderBatch)
            .WithMany(b => b.Items)
            .HasForeignKey(i => new { i.SchoolId, i.DebtorReminderBatchId })
            .HasPrincipalKey(b => new { b.SchoolId, b.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Enrollment>()
            .WithMany()
            .HasForeignKey(i => new { i.SchoolId, i.EnrollmentId })
            .HasPrincipalKey(e => new { e.SchoolId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
