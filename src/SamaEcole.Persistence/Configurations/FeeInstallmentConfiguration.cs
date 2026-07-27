using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL équivalente est posée par la migration AddFeeInstallmentPlans (règle #2).
/// </summary>
public class FeeInstallmentConfiguration : IEntityTypeConfiguration<FeeInstallment>
{
    public void Configure(EntityTypeBuilder<FeeInstallment> builder)
    {
        builder.ToTable("fee_installments");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.SchoolId).IsRequired();
        builder.Property(i => i.SequenceNo).IsRequired();
        builder.Property(i => i.Label).IsRequired().HasMaxLength(120);
        builder.Property(i => i.Amount).IsRequired().HasPrecision(12, 2);

        builder.HasIndex(i => new { i.SchoolId, i.FeeInstallmentPlanId, i.SequenceNo }).IsUnique();

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(i => i.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK COMPOSITE (SchoolId, FeeInstallmentPlanId) : même défense anti-croisement de tenants que
        // EnrollmentFeeLine → Enrollment — une échéance ne peut jamais se rattacher au plan d'une
        // autre école, même par erreur applicative.
        builder.HasOne(i => i.FeeInstallmentPlan)
            .WithMany(p => p.Installments)
            .HasForeignKey(i => new { i.SchoolId, i.FeeInstallmentPlanId })
            .HasPrincipalKey(p => new { p.SchoolId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
