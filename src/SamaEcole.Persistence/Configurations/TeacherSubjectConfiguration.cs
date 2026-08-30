using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Table de LIAISON pure : « cet enseignant est qualifié pour cette matière ». Contrairement au reste
/// du modèle, le rôle applicatif y a le droit <c>DELETE</c> (migration
/// <c>GrantDeleteOnTeacherSubjects</c>, ticket JGK-T01) : retirer une qualification est une vraie
/// suppression — aucune valeur d'audit à conserver « untel a pu enseigner les maths jusqu'en mars »,
/// et <c>UpdateTeacherCommandHandler</c> recrée la ligne à l'identique si le Directeur se ravise.
/// </summary>
public class TeacherSubjectConfiguration : IEntityTypeConfiguration<TeacherSubject>
{
    public void Configure(EntityTypeBuilder<TeacherSubject> builder)
    {
        builder.ToTable("teacher_subjects");

        builder.HasKey(ts => ts.Id);
        builder.Property(ts => ts.SchoolId).IsRequired();

        // Une même matière ne peut être qualifiée deux fois pour le même enseignant.
        builder.HasIndex(ts => new { ts.TeacherId, ts.SubjectId }).IsUnique();
        builder.HasIndex(ts => ts.SchoolId);

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(ts => ts.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK COMPOSITE vers teachers et subjects (même raisonnement que Student → Classroom, voir
        // StudentConfiguration) : sur la seule colonne, rien n'empêcherait de qualifier un enseignant
        // sur une matière d'une AUTRE école. En incluant SchoolId des deux côtés, PostgreSQL rend le
        // croisement de tenants structurellement impossible.
        builder.HasOne<Teacher>()
            .WithMany()
            .HasForeignKey(ts => new { ts.SchoolId, ts.TeacherId })
            .HasPrincipalKey(t => new { t.SchoolId, t.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Subject>()
            .WithMany()
            .HasForeignKey(ts => new { ts.SchoolId, ts.SubjectId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
