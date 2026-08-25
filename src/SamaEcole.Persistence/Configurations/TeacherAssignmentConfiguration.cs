using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

public class TeacherAssignmentConfiguration : IEntityTypeConfiguration<TeacherAssignment>
{
    public void Configure(EntityTypeBuilder<TeacherAssignment> builder)
    {
        builder.ToTable("teacher_assignments");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.SchoolId).IsRequired();

        // Un même enseignant ne peut pas être attribué deux fois à la même classe/matière/année.
        //
        // Index PARTIEL (« NOT IsDeleted »), et c'est indispensable : le retrait d'une attribution est
        // une suppression LOGIQUE (AGENTS.md règle #6), la ligne reste donc en base. Sans ce filtre,
        // PostgreSQL continue de compter la ligne retirée — la place reste occupée par une attribution
        // que l'utilisateur croit avoir supprimée, et l'établissement ne peut PLUS JAMAIS rendre cette
        // matière à cet enseignant dans cette classe pour l'année en cours. Aucun écran ne permet de
        // sortir de cette impasse.
        //
        // Même convention que EnrollmentConfiguration et SchoolConfiguration : sur une table à soft
        // delete, un index unique est toujours partiel.
        builder.HasIndex(a => new { a.TeacherId, a.ClassroomId, a.SubjectId, a.SchoolYearId })
            .IsUnique()
            .HasFilter("NOT \"IsDeleted\"");
        builder.HasIndex(a => a.SchoolId);

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(a => a.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK COMPOSITE vers teachers/classrooms/subjects/school_years (même raisonnement que
        // TeacherSubjectConfiguration) : empêche structurellement le croisement de tenants.
        builder.HasOne<Teacher>()
            .WithMany()
            .HasForeignKey(a => new { a.SchoolId, a.TeacherId })
            .HasPrincipalKey(t => new { t.SchoolId, t.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Classroom>()
            .WithMany()
            .HasForeignKey(a => new { a.SchoolId, a.ClassroomId })
            .HasPrincipalKey(c => new { c.SchoolId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Subject>()
            .WithMany()
            .HasForeignKey(a => new { a.SchoolId, a.SubjectId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SchoolYear>()
            .WithMany()
            .HasForeignKey(a => new { a.SchoolId, a.SchoolYearId })
            .HasPrincipalKey(y => new { y.SchoolId, y.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
