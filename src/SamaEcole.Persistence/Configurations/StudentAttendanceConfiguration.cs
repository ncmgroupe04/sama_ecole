using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

public class StudentAttendanceConfiguration : IEntityTypeConfiguration<StudentAttendance>
{
    public void Configure(EntityTypeBuilder<StudentAttendance> builder)
    {
        builder.ToTable("student_attendances");

        builder.HasKey(sa => sa.Id);
        builder.Property(sa => sa.SchoolId).IsRequired();
        builder.Property(sa => sa.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Un élève ne peut figurer qu'une fois sur une même fiche d'appel.
        builder.HasIndex(sa => new { sa.AttendanceSheetId, sa.StudentId }).IsUnique();
        builder.HasIndex(sa => sa.SchoolId);

        // Billet d'entrée rattaché à la ligne (Évolution N°5) : nullable, Restrict (règle #6).
        builder.HasIndex(sa => sa.EntryTicketId);

        // Statut d'avant un billet (Complément N°5 bis) : texte nullable, comme LateArrival.PreviousStatus.
        builder.Property(sa => sa.PreviousStatus).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<LateArrival>()
            .WithMany()
            .HasForeignKey(sa => sa.EntryTicketId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(sa => sa.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK COMPOSITES vers attendance_sheets et students : empêchent structurellement qu'une ligne
        // pointe une fiche ou un élève d'une autre école. Cascade sur la fiche : supprimer la fiche
        // (soft delete applicatif mis à part) n'a pas de sens sans ses lignes — mais on reste en
        // Restrict, cohérent avec le reste du schéma (aucune suppression physique, règle #6).
        builder.HasOne<AttendanceSheet>()
            .WithMany()
            .HasForeignKey(sa => new { sa.SchoolId, sa.AttendanceSheetId })
            .HasPrincipalKey(a => new { a.SchoolId, a.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Student>()
            .WithMany()
            .HasForeignKey(sa => new { sa.SchoolId, sa.StudentId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
