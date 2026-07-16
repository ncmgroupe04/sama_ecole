using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL équivalente est posée par la migration AddGrades (AGENTS.md règle #2).
/// </summary>
public class TermConfiguration : IEntityTypeConfiguration<Term>
{
    public void Configure(EntityTypeBuilder<Term> builder)
    {
        builder.ToTable("terms");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.SchoolId).IsRequired();
        builder.Property(t => t.Label).IsRequired().HasMaxLength(30);
        builder.Property(t => t.Order).IsRequired();
        builder.Property(t => t.StartDate).IsRequired();
        builder.Property(t => t.EndDate).IsRequired();

        // Un seul trimestre par rang au sein d'une même année scolaire.
        builder.HasIndex(t => new { t.SchoolId, t.SchoolYearId, t.Order, t.IsDeleted }).IsUnique();

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(t => t.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK COMPOSITE (SchoolId, SchoolYearId) : sur la seule colonne d'identité, rien n'empêcherait
        // un trimestre de pointer l'année scolaire d'une AUTRE école (même défense que students →
        // classrooms, JGK-C02).
        builder.HasOne<SchoolYear>()
            .WithMany()
            .HasForeignKey(t => new { t.SchoolId, t.SchoolYearId })
            .HasPrincipalKey(y => new { y.SchoolId, y.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
