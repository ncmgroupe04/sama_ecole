using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Persistence.Configurations;

public class StudentHizbEvaluationConfiguration : IEntityTypeConfiguration<StudentHizbEvaluation>
{
    public void Configure(EntityTypeBuilder<StudentHizbEvaluation> builder)
    {
        builder.ToTable("student_hizb_evaluations", table =>
        {
            // Mêmes bornes que student_hizb_statuses, tenues par la BASE : un import ou du SQL direct ne doit pas pouvoir
            // écrire un Hizb 61, un total de 300 quarts ou une note de 9 dans un journal censé faire foi.
            table.HasCheckConstraint("CK_student_hizb_evaluations_hizb_range", "\"HizbNumber\" BETWEEN 1 AND 60");
            table.HasCheckConstraint("CK_student_hizb_evaluations_quarters_range",
                "\"QuartersBefore\" BETWEEN 0 AND 4 AND \"QuartersAfter\" BETWEEN 0 AND 4");
            table.HasCheckConstraint("CK_student_hizb_evaluations_total_range", "\"TotalQuartersAfter\" BETWEEN 0 AND 240");
            table.HasCheckConstraint("CK_student_hizb_evaluations_rating_range", "\"Rating\" IS NULL OR \"Rating\" BETWEEN 1 AND 5");
            // Un Hizb ramené à 0 quart n'a pas de note : même règle que l'état courant.
            table.HasCheckConstraint("CK_student_hizb_evaluations_rating_needs_quarters", "\"QuartersAfter\" > 0 OR \"Rating\" IS NULL");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.SchoolId).IsRequired();

        // Lecture dominante : toutes les évaluations d'un élève dans l'ordre du temps (courbe, dernières évaluations).
        builder.HasIndex(e => new { e.SchoolId, e.StudentId, e.EvaluatedAt })
            .HasDatabaseName("IX_student_hizb_evaluations_student_time");

        // Courbe de la Direction : toutes les évaluations d'une Halqa sur une période.
        builder.HasIndex(e => new { e.SchoolId, e.InstructorId, e.EvaluatedAt })
            .HasDatabaseName("IX_student_hizb_evaluations_instructor_time");

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(e => e.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK COMPOSITE (SchoolId, StudentId) : un journal ne peut pas pointer vers un élève d'une autre école.
        builder.HasOne<Student>()
            .WithMany()
            .HasForeignKey(e => new { e.SchoolId, e.StudentId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Instructor>()
            .WithMany()
            .HasForeignKey(e => new { e.SchoolId, e.InstructorId })
            .HasPrincipalKey(i => new { i.SchoolId, i.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
