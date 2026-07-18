using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating pour toute entité ITenantEntity — ne pas en
/// ajouter un ici, EF Core n'autorise qu'un seul HasQueryFilter par entité.
/// La policy RLS PostgreSQL équivalente doit être ajoutée dans la migration qui crée
/// cette table (AGENTS.md règle #2, docs/Volume_3_DDS.md).
/// </summary>
public class StudentConfiguration : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> builder)
    {
        builder.ToTable("students");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.SchoolId).IsRequired();
        builder.Property(s => s.Matricule).IsRequired().HasMaxLength(30);
        builder.Property(s => s.FullName).IsRequired().HasMaxLength(200);
        builder.Property(s => s.BirthPlace).HasMaxLength(200);
        builder.Property(s => s.Gender).IsRequired().HasMaxLength(1);
        builder.Property(s => s.PhotoUrl).HasMaxLength(500);

        // Un matricule est unique par école, pas globalement.
        builder.HasIndex(s => new { s.SchoolId, s.Matricule }).IsUnique();
        builder.HasIndex(s => s.SchoolId);

        // FK vers schools (docs/Volume_3_DDS.md §3) — pas de navigation dans l'entité, la relation
        // reste au niveau du schéma. Restrict : on ne supprime jamais physiquement une école (règle #6).
        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(s => s.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK COMPOSITE vers classrooms, et non une simple FK sur ClassroomId : sur la seule colonne,
        // rien n'empêcherait un élève de pointer une classe d'une AUTRE école. La RLS masque une
        // telle ligne à la lecture, mais ne l'empêche pas d'exister — sa clause WITH CHECK ne
        // contrôle que le SchoolId de la ligne insérée, pas la cohérence de ce qu'elle référence.
        // En incluant SchoolId des deux côtés, PostgreSQL rend le croisement de tenants
        // structurellement impossible (ticket JGK-C02).
        builder.HasOne<Classroom>()
            .WithMany()
            .HasForeignKey(s => new { s.SchoolId, s.ClassroomId })
            .HasPrincipalKey(c => new { c.SchoolId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
