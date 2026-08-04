using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Persistence.Configurations;

public class ScheduleSlotConfiguration : IEntityTypeConfiguration<ScheduleSlot>
{
    public void Configure(EntityTypeBuilder<ScheduleSlot> builder)
    {
        builder.HasKey(s => s.Id);

        // Required string
        builder.Property(s => s.DayOfWeek)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(s => s.RoomNumber)
            .HasMaxLength(50);

        // Relationships
        builder.HasOne(s => s.Teacher)
            .WithMany()
            .HasForeignKey(s => s.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Classroom)
            .WithMany()
            .HasForeignKey(s => s.ClassroomId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Subject)
            .WithMany()
            .HasForeignKey(s => s.SubjectId)
            .OnDelete(DeleteBehavior.Restrict);
            
        // Index
        builder.HasIndex(s => new { s.SchoolId, s.TeacherId });
        builder.HasIndex(s => new { s.SchoolId, s.ClassroomId });

        // Dashboard Directeur : "prochains cours du jour" filtre sur DayOfWeek == aujourd'hui — sans
        // index, la requête scanne tous les créneaux de l'école.
        builder.HasIndex(s => new { s.SchoolId, s.DayOfWeek })
            .HasDatabaseName("IX_schedule_slots_SchoolId_DayOfWeek");
    }
}
