using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL équivalente est posée par la migration AddQuranCoreModule
/// (AGENTS.md règle #2 : les DEUX protections, jamais une seule).
/// </summary>
public class QuranEvaluationConfiguration : IEntityTypeConfiguration<QuranEvaluation>
{
    public void Configure(EntityTypeBuilder<QuranEvaluation> builder)
    {
        builder.ToTable("quran_evaluations");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.SchoolId).IsRequired();

        // Verrou optimiste xmin (AGENTS.md règle #5) : c'est une note, comme Grade.
        builder.Property<uint>("xmin").IsRowVersion();

        // Même précision que Grade.Value : le barème d'un examen oral n'a aucune raison de dépasser 999,99.
        builder.Property(e => e.FinalScore).IsRequired().HasPrecision(5, 2);

        builder.HasIndex(e => new { e.SchoolId, e.StudentId });

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(e => e.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Student>()
            .WithMany()
            .HasForeignKey(e => new { e.SchoolId, e.StudentId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
