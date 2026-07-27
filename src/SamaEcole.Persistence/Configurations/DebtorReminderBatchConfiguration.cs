using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL équivalente est posée par la migration AddDebtorReminderBatches (règle #2).
/// </summary>
public class DebtorReminderBatchConfiguration : IEntityTypeConfiguration<DebtorReminderBatch>
{
    public void Configure(EntityTypeBuilder<DebtorReminderBatch> builder)
    {
        builder.ToTable("debtor_reminder_batches");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.SchoolId).IsRequired();
        builder.Property(b => b.ThresholdDays).IsRequired();
        builder.Property(b => b.GeneratedAt).IsRequired();
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // L'écran des brouillons liste du plus récent au plus ancien, filtré par école et par statut
        // (Draft en priorité) : l'index suit exactement cette lecture.
        builder.HasIndex(b => new { b.SchoolId, b.Status, b.GeneratedAt });

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(b => b.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Classroom>()
            .WithMany()
            .HasForeignKey(b => new { b.SchoolId, b.ClassroomId })
            .HasPrincipalKey(c => new { c.SchoolId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
