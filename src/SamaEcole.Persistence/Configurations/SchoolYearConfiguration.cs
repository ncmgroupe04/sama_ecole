using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL équivalente est posée par la migration AddSchoolYears
/// (AGENTS.md règle #2 : les DEUX protections, jamais une seule).
/// </summary>
public class SchoolYearConfiguration : IEntityTypeConfiguration<SchoolYear>
{
    /// <summary>
    /// « Une seule année active à la fois » (ticket JGK-C01) tenu par PostgreSQL, et non par un
    /// contrôle applicatif : deux Directeurs qui activent chacun une année au même instant liraient
    /// tous deux « aucun conflit » avant d'écrire, et l'école se retrouverait avec deux années
    /// actives — donc des inscriptions et des frais rattachés à deux exercices différents.
    ///
    /// Index PARTIEL : il ne contraint que les lignes actives et non supprimées. Une école peut donc
    /// avoir autant d'années inactives ou archivées qu'elle veut, mais jamais deux actives.
    /// </summary>
    private const string SingleActiveYearFilter = "\"IsActive\" AND NOT \"IsDeleted\"";

    public void Configure(EntityTypeBuilder<SchoolYear> builder)
    {
        builder.ToTable("school_years");

        builder.HasKey(y => y.Id);
        builder.Property(y => y.SchoolId).IsRequired();
        builder.Property(y => y.Label).IsRequired().HasMaxLength(20);
        builder.Property(y => y.StartDate).IsRequired();
        builder.Property(y => y.EndDate).IsRequired();
        builder.Property(y => y.IsActive).IsRequired();

        // Deux années ne peuvent pas porter le même libellé dans la même école — mais « 2026-2027 »
        // existe évidemment dans toutes les écoles. Le soft delete fait partie de la clé : sans lui,
        // on ne pourrait jamais recréer une année portant le libellé d'une année archivée.
        builder.HasIndex(y => new { y.SchoolId, y.Label, y.IsDeleted }).IsUnique();

        builder.HasIndex(y => y.SchoolId)
            .IsUnique()
            .HasFilter(SingleActiveYearFilter)
            .HasDatabaseName("UX_school_years_single_active");

        // Restrict : on ne supprime jamais physiquement une école (AGENTS.md règle #6).
        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(y => y.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
