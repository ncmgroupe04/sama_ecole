using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity. La policy RLS PostgreSQL
/// équivalente vit dans la migration AddSubjectCoefficientOverrides (AGENTS.md règle #2).
/// </summary>
public class SubjectCoefficientOverrideConfiguration : IEntityTypeConfiguration<SubjectCoefficientOverride>
{
    public void Configure(EntityTypeBuilder<SubjectCoefficientOverride> builder)
    {
        builder.ToTable("subject_coefficient_overrides", table =>
        {
            // Exactement UNE portée : une surcharge de classe OU de série, jamais les deux, jamais aucune.
            // Tenue par la BASE — un appel qui oublierait le validateur ne peut pas enregistrer une
            // surcharge ambiguë (règle #2, « les deux, jamais un seul »).
            table.HasCheckConstraint(
                "CK_subject_coefficient_overrides_one_scope",
                "num_nonnulls(\"ClassroomId\", \"Series\") = 1");
        });

        builder.HasKey(o => o.Id);
        builder.Property(o => o.SchoolId).IsRequired();

        // Verrou optimiste xmin (règle #5) : la surcharge pilote les moyennes, une écriture sur une ligne
        // modifiée entre-temps remonte en 409, jamais un écrasement silencieux.
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(o => o.Series).HasMaxLength(10);

        // numeric(4,2), comme Subject.Coefficient : de 0,01 à 99,99, sans dérive de flottant.
        builder.Property(o => o.Coefficient).IsRequired().HasPrecision(4, 2);

        // Une surcharge par (année, matière, portée). Index PARTIELS (« NOT IsDeleted ») — même convention
        // que SubjectConfiguration : sur une table à suppression logique, « Rétablir » puis poser une
        // nouvelle valeur ne doit pas se heurter à la ligne archivée.
        builder.HasIndex(o => new { o.SchoolYearId, o.SubjectId, o.ClassroomId })
            .IsUnique()
            .HasDatabaseName("UX_subject_coefficient_overrides_classroom")
            .HasFilter("\"ClassroomId\" IS NOT NULL AND NOT \"IsDeleted\"");

        builder.HasIndex(o => new { o.SchoolYearId, o.SubjectId, o.Series })
            .IsUnique()
            .HasDatabaseName("UX_subject_coefficient_overrides_series")
            .HasFilter("\"Series\" IS NOT NULL AND NOT \"IsDeleted\"");

        builder.HasIndex(o => o.SchoolId);

        // FK COMPOSITES tenant-safe (même motif qu'AttendanceSheetConfiguration) : le croisement de
        // tenants devient structurellement impossible, pas seulement filtré à la lecture.
        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(o => o.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Subject>()
            .WithMany()
            .HasForeignKey(o => new { o.SchoolId, o.SubjectId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SchoolYear>()
            .WithMany()
            .HasForeignKey(o => new { o.SchoolId, o.SchoolYearId })
            .HasPrincipalKey(y => new { y.SchoolId, y.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // Nullable (portée « série ») : PostgreSQL ne contrôle pas une FK composite dont une colonne est NULL.
        builder.HasOne<Classroom>()
            .WithMany()
            .HasForeignKey(o => new { o.SchoolId, o.ClassroomId })
            .HasPrincipalKey(c => new { c.SchoolId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
