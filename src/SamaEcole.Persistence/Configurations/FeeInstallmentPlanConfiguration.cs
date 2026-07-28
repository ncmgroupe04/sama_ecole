using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL équivalente est posée par la migration AddFeeInstallmentPlans (règle #2).
/// </summary>
public class FeeInstallmentPlanConfiguration : IEntityTypeConfiguration<FeeInstallmentPlan>
{
    public void Configure(EntityTypeBuilder<FeeInstallmentPlan> builder)
    {
        builder.ToTable("fee_installment_plans");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.SchoolId).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Reason).HasMaxLength(500);
        builder.Property(p => p.CreatedFromClassroomTemplate).IsRequired();

        // UN SEUL plan Active par inscription : un nouvel accord bascule l'ancien à Cancelled plutôt
        // que de l'écraser (même parti pris que EnrollmentConfiguration pour l'inscription active par
        // année). Index PARTIEL, filtré sur le statut : les plans annulés d'une même inscription
        // coexistent librement, seul l'état "actif" est contraint à l'unicité.
        builder.HasIndex(p => new { p.SchoolId, p.EnrollmentId })
            .IsUnique()
            .HasFilter("\"Status\" = 'Active'")
            .HasDatabaseName("UX_fee_installment_plans_single_active_per_enrollment");

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(p => p.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Enrollment>()
            .WithMany()
            .HasForeignKey(p => new { p.SchoolId, p.EnrollmentId })
            .HasPrincipalKey(e => new { e.SchoolId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // Relation vers FeeInstallment entièrement configurée côté FeeInstallmentConfiguration (FK
        // composite SchoolId+FeeInstallmentPlanId) : la déclarer aussi ici dupliquerait la même
        // relation avec deux configurations différentes.
    }
}
