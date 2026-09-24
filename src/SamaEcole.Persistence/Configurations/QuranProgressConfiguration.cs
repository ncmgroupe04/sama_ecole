using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL équivalente est posée par la migration AddQuranCoreModule
/// (AGENTS.md règle #2 : les DEUX protections, jamais une seule).
/// </summary>
public class QuranProgressConfiguration : IEntityTypeConfiguration<QuranProgress>
{
    public void Configure(EntityTypeBuilder<QuranProgress> builder)
    {
        builder.ToTable("quran_progress");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.SchoolId).IsRequired();

        // Verrou optimiste xmin (AGENTS.md règle #5), comme Grade et Subject.
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(QuranMemorizationStatus.InProcess);
        builder.Property(p => p.Notes).HasMaxLength(2000);

        builder.HasIndex(p => new { p.SchoolId, p.StudentId });

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(p => p.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK COMPOSITE (SchoolId, StudentId) : même défense qu'ailleurs (Grade → Student) contre un
        // suivi qui pointerait l'élève d'une AUTRE école.
        builder.HasOne<Student>()
            .WithMany()
            .HasForeignKey(p => new { p.SchoolId, p.StudentId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
