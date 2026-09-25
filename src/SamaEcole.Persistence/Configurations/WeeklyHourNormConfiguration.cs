using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Global Query Filter (SchoolId + IsDeleted) appliqué par ApplicationDbContext à toute ITenantEntity ; policy RLS
/// équivalente dans la migration AddWeeklyHourNorms (AGENTS.md règle #2).
/// </summary>
public class WeeklyHourNormConfiguration : IEntityTypeConfiguration<WeeklyHourNorm>
{
    public void Configure(EntityTypeBuilder<WeeklyHourNorm> builder)
    {
        builder.ToTable("weekly_hour_norms", t =>
            t.HasCheckConstraint("CK_weekly_hour_norms_hours", "\"WeeklyHours\" >= 0 AND \"WeeklyHours\" <= 40"));

        builder.HasKey(n => n.Id);
        builder.Property(n => n.SchoolId).IsRequired();
        builder.Property<uint>("xmin").IsRowVersion();
        builder.Property(n => n.GradeLevel).IsRequired().HasMaxLength(20);
        builder.Property(n => n.Series).HasMaxLength(10);
        builder.Property(n => n.WeeklyHours).HasPrecision(4, 2);

        // Un réglage par (niveau, série, matière). NULLS NOT DISTINCT : la ligne « toutes séries » (Series NULL)
        // est unique elle aussi — sans quoi deux réglages concurrents du même niveau passeraient.
        builder.HasIndex(n => new { n.SchoolId, n.GradeLevel, n.Series, n.SubjectId })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName("UX_weekly_hour_norms_scope")
            .HasFilter("NOT \"IsDeleted\"");

        builder.HasOne<School>().WithMany().HasForeignKey(n => n.SchoolId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Subject>()
            .WithMany()
            .HasForeignKey(n => new { n.SchoolId, n.SubjectId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
