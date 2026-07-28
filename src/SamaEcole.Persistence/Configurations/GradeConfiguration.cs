using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL équivalente est posée par la migration AddGrades (AGENTS.md règle #2).
///
/// Le verrou optimiste exploite la colonne système xmin de PostgreSQL (AGENTS.md règle #5) : une
/// propriété fantôme uint marquée IsRowVersion, que la convention Npgsql mappe automatiquement sur
/// xmin sans générer d'opération de migration — comme ClassFee et Enrollment.
/// </summary>
public class GradeConfiguration : IEntityTypeConfiguration<Grade>
{
    public void Configure(EntityTypeBuilder<Grade> builder)
    {
        builder.ToTable("grades");

        builder.HasKey(g => g.Id);
        builder.Property(g => g.SchoolId).IsRequired();

        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(g => g.EvaluationType).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Le barème (10 ou 20) borne la valeur au niveau applicatif (SchoolSettings.GradingScale) ;
        // ici, seule la précision du stockage est fixée.
        builder.Property(g => g.Value).IsRequired().HasPrecision(5, 2);

        // Une seule note par (élève, matière, trimestre, type d'évaluation) : c'est CETTE contrainte
        // qui transforme une tentative de double CRÉATION concurrente en conflit détecté par
        // ApplicationDbContext.SaveChangesAsync (violation d'unicité -> 409), au lieu d'un doublon
        // silencieux (AGENTS.md règle #5).
        builder.HasIndex(g => new { g.SchoolId, g.StudentId, g.SubjectId, g.TermId, g.EvaluationType, g.IsDeleted })
            .IsUnique()
            .HasDatabaseName("UX_grades_single_entry");

        builder.HasIndex(g => new { g.SchoolId, g.StudentId, g.TermId });

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(g => g.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK COMPOSITES (SchoolId, …) : sur la seule colonne d'identité, rien n'empêcherait une note
        // de pointer l'élève, la matière ou le trimestre d'une AUTRE école (même défense que
        // students → classrooms, JGK-C02).
        builder.HasOne<Student>()
            .WithMany()
            .HasForeignKey(g => new { g.SchoolId, g.StudentId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Subject>()
            .WithMany()
            .HasForeignKey(g => new { g.SchoolId, g.SubjectId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Term>()
            .WithMany()
            .HasForeignKey(g => new { g.SchoolId, g.TermId })
            .HasPrincipalKey(t => new { t.SchoolId, t.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
