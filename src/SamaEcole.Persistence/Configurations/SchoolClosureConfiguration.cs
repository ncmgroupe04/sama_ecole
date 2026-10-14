using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL est posée à la main par la migration AddSchoolClosures (AGENTS.md règle #2).
/// </summary>
public class SchoolClosureConfiguration : IEntityTypeConfiguration<SchoolClosure>
{
    public void Configure(EntityTypeBuilder<SchoolClosure> builder)
    {
        builder.ToTable("school_closures", t =>
            t.HasCheckConstraint("CK_school_closures_period", "\"EndDate\" >= \"StartDate\""));

        builder.HasKey(c => c.Id);
        builder.Property(c => c.SchoolId).IsRequired();
        builder.Property(c => c.StartDate).IsRequired();
        builder.Property(c => c.EndDate).IsRequired();
        builder.Property(c => c.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.Label).IsRequired().HasMaxLength(120);
        builder.Property(c => c.LabelAr).HasMaxLength(120);

        // Lecture dominante : « les fermetures de cette année, dans l'ordre du calendrier ».
        builder.HasIndex(c => new { c.SchoolId, c.SchoolYearId, c.StartDate });

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(c => c.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK COMPOSITE (SchoolId, SchoolYearId) : sur la seule colonne d'identité, rien n'empêcherait
        // une fermeture de pointer l'année scolaire d'une AUTRE école (même défense que terms).
        builder.HasOne<SchoolYear>()
            .WithMany()
            .HasForeignKey(c => new { c.SchoolId, c.SchoolYearId })
            .HasPrincipalKey(y => new { y.SchoolId, y.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
