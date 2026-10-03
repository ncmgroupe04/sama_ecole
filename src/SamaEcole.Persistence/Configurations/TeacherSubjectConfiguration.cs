using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Association révocable : « cet enseignant est qualifié pour cette matière ». Son retrait est une
/// suppression logique, car les consultations historiques doivent pouvoir résoudre la qualification
/// qui existait lors d'une période passée. L'index unique ne porte que sur les associations actives :
/// une nouvelle qualification après révocation crée donc une nouvelle ligne, sans réactiver l'ancienne.
/// </summary>
public class TeacherSubjectConfiguration : IEntityTypeConfiguration<TeacherSubject>
{
    public void Configure(EntityTypeBuilder<TeacherSubject> builder)
    {
        builder.ToTable("teacher_subjects");

        builder.HasKey(ts => ts.Id);
        builder.Property(ts => ts.SchoolId).IsRequired();

        // Une même matière ne peut être active deux fois pour le même enseignant; les tombstones
        // historiques restent réinsérables et résolubles par les lectures dédiées tenant-scoped.
        builder.HasIndex(ts => new { ts.TeacherId, ts.SubjectId })
            .IsUnique()
            .HasFilter("NOT \"IsDeleted\"");
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
