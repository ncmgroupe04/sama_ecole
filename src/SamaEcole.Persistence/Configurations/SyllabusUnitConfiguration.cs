using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Global Query Filter (SchoolId + IsDeleted) appliqué par ApplicationDbContext à toute ITenantEntity ; policy RLS
/// équivalente dans la migration AddSyllabusTracking (AGENTS.md règle #2).
/// </summary>
public class SyllabusUnitConfiguration : IEntityTypeConfiguration<SyllabusUnit>
{
    public void Configure(EntityTypeBuilder<SyllabusUnit> builder)
    {
        builder.ToTable("syllabus_units");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.SchoolId).IsRequired();
        builder.Property<uint>("xmin").IsRowVersion();
        builder.Property(u => u.GradeLevel).IsRequired().HasMaxLength(20);
        builder.Property(u => u.Section).HasMaxLength(120);
        builder.Property(u => u.Title).IsRequired().HasMaxLength(200);
        builder.Property(u => u.PlannedHours).HasPrecision(5, 2);

        // Un intitulé une seule fois par programme (matière, niveau) — index partiel, archiver libère la clé.
        builder.HasIndex(u => new { u.SchoolId, u.SubjectId, u.GradeLevel, u.Title })
            .IsUnique()
            .HasDatabaseName("UX_syllabus_units_title")
            .HasFilter("NOT \"IsDeleted\"");

        builder.HasOne<School>().WithMany().HasForeignKey(u => u.SchoolId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Subject>()
            .WithMany()
            .HasForeignKey(u => new { u.SchoolId, u.SubjectId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
