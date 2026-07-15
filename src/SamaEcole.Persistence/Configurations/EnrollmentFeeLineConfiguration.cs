using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL équivalente est posée par la migration AddEnrollments (AGENTS.md règle #2).
///
/// Instantané figé des frais : écrit une seule fois dans la transaction d'inscription, jamais réédité.
/// Pas de verrou optimiste, donc — il n'y a jamais deux écritures concurrentes sur la même ligne.
/// </summary>
public class EnrollmentFeeLineConfiguration : IEntityTypeConfiguration<EnrollmentFeeLine>
{
    public void Configure(EntityTypeBuilder<EnrollmentFeeLine> builder)
    {
        builder.ToTable("enrollment_fee_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.SchoolId).IsRequired();
        builder.Property(l => l.Designation).IsRequired().HasMaxLength(60);
        builder.Property(l => l.IsRecurring).IsRequired();
        builder.Property(l => l.UnitAmount).IsRequired().HasPrecision(12, 2);
        builder.Property(l => l.Months).IsRequired();
        builder.Property(l => l.LineTotal).IsRequired().HasPrecision(12, 2);

        builder.HasIndex(l => new { l.SchoolId, l.EnrollmentId });

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(l => l.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK COMPOSITES (SchoolId, …), même défense anti-croisement de tenants que partout ailleurs.
        // La ligne appartient à une inscription ET à une catégorie de la MÊME école.
        builder.HasOne<Enrollment>()
            .WithMany()
            .HasForeignKey(l => new { l.SchoolId, l.EnrollmentId })
            .HasPrincipalKey(e => new { e.SchoolId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<FeeCategory>()
            .WithMany()
            .HasForeignKey(l => new { l.SchoolId, l.FeeCategoryId })
            .HasPrincipalKey(c => new { c.SchoolId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
