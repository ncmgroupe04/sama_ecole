using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating pour toute entité ITenantEntity. La policy RLS PostgreSQL
/// équivalente vit dans la migration AddAttendance (AGENTS.md règle #2).
/// </summary>
public class AttendanceSheetConfiguration : IEntityTypeConfiguration<AttendanceSheet>
{
    public void Configure(EntityTypeBuilder<AttendanceSheet> builder)
    {
        builder.ToTable("attendance_sheets");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.SchoolId).IsRequired();
        builder.Property(a => a.Period).IsRequired().HasMaxLength(50);

        // Une seule fiche par (classe, matière, date, créneau) : une seconde saisie de la même clé
        // viole cet index et remonte en 409 (AGENTS.md règle #5).
        builder.HasIndex(a => new { a.SchoolId, a.ClassroomId, a.SubjectId, a.Date, a.Period }).IsUnique();
        builder.HasIndex(a => a.SchoolId);

        // Clé alternative composite (SchoolId, Id) : cible des FK composites de StudentAttendance,
        // qui empêchent une ligne d'élève de pointer une fiche d'une autre école.
        builder.HasAlternateKey(a => new { a.SchoolId, a.Id });

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(a => a.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK COMPOSITES vers classrooms/subjects/school_years (même raisonnement que
        // TeacherAssignmentConfiguration) : le croisement de tenants devient structurellement impossible.
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

        // Appel par créneau (Évolution N°5) : lien NULLABLE vers le cours d'emploi du temps. FK simple, comme
        // celles de ScheduleSlot lui-même (qui n'expose pas de clé alternative composite) ; le contrôle
        // « ce créneau est bien celui de la classe et de la matière » est fait par SlotPeriod.Mismatch.
        builder.HasIndex(a => a.ScheduleSlotId);

        builder.HasOne<ScheduleSlot>()
            .WithMany()
            .HasForeignKey(a => a.ScheduleSlotId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
