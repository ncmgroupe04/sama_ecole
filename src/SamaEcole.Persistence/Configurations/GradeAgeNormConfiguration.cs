using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity. La policy RLS PostgreSQL
/// équivalente vit dans la migration AddIefMapping (AGENTS.md règle #2).
/// </summary>
public class GradeAgeNormConfiguration : IEntityTypeConfiguration<GradeAgeNorm>
{
    public void Configure(EntityTypeBuilder<GradeAgeNorm> builder)
    {
        builder.ToTable("grade_age_norms", table =>
            table.HasCheckConstraint("CK_grade_age_norms_range", "\"MinAge\" >= 0 AND \"MaxAge\" >= \"MinAge\" AND \"MaxAge\" <= 30"));

        builder.HasKey(n => n.Id);
        builder.Property(n => n.SchoolId).IsRequired();
        builder.Property<uint>("xmin").IsRowVersion();
        builder.Property(n => n.GradeLevel).IsRequired().HasMaxLength(20);

        // Une tranche par niveau et par école ; index partiel : « revenir au modèle » archive la ligne.
        builder.HasIndex(n => new { n.SchoolId, n.GradeLevel })
            .IsUnique()
            .HasDatabaseName("UX_grade_age_norms_level")
            .HasFilter("NOT \"IsDeleted\"");

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(n => n.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
