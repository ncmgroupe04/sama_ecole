using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>Paramètres d'établissement (ticket JGK-B02). Table tenant : RLS + Global Query Filter.</summary>
public class SchoolSettingsConfiguration : IEntityTypeConfiguration<SchoolSettings>
{
    public void Configure(EntityTypeBuilder<SchoolSettings> builder)
    {
        builder.ToTable("school_settings");

        builder.HasKey(s => s.Id);

        // UNE seule ligne de réglages par école : deux lignes concurrentes signifieraient que la
        // moitié des inscriptions utiliserait un format et l'autre moitié un autre.
        builder.HasIndex(s => s.SchoolId).IsUnique();

        builder.Property(s => s.GradingScale).IsRequired();
        builder.Property(s => s.StudentMatriculeFormat).IsRequired().HasMaxLength(50);
        builder.Property(s => s.TeacherMatriculeFormat).IsRequired().HasMaxLength(50);
        builder.Property(s => s.AutoLogoutMinutes).IsRequired();
        builder.Property(s => s.DateFormat).IsRequired().HasMaxLength(30);

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(s => s.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
