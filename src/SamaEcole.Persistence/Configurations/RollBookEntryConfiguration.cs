using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL est posée à la main par la migration AddRollBookEntries (AGENTS.md règle #2).
/// </summary>
public class RollBookEntryConfiguration : IEntityTypeConfiguration<RollBookEntry>
{
    public void Configure(EntityTypeBuilder<RollBookEntry> builder)
    {
        builder.ToTable("roll_book_entries", t =>
        {
            t.HasCheckConstraint("CK_roll_book_entries_late_minutes", "\"LateMinutes\" BETWEEN 0 AND 600");
            t.HasCheckConstraint("CK_roll_book_entries_minutes_need_late", "\"Status\" = 'Late' OR \"LateMinutes\" = 0");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.SchoolId).IsRequired();
        builder.Property(e => e.Date).IsRequired();
        builder.Property(e => e.HalfDay).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.Reason).HasMaxLength(300);

        // Une seule saisie VIVANTE par (élève, jour, demi-journée) : une saisie supprimée (retour au
        // calcul) ne bloque pas la suivante. Index partiel, comme les autres entités à suppression logique.
        builder.HasIndex(e => new { e.SchoolId, e.StudentId, e.Date, e.HalfDay })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        // Lecture dominante : « la classe sur cette période ».
        builder.HasIndex(e => new { e.SchoolId, e.ClassroomId, e.Date });

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(e => e.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK COMPOSITES (SchoolId, X) : une saisie ne peut pas pointer l'année, la classe ou l'élève d'une
        // AUTRE école, même si la RLS était un jour contournée.
        builder.HasOne<SchoolYear>()
            .WithMany()
            .HasForeignKey(e => new { e.SchoolId, e.SchoolYearId })
            .HasPrincipalKey(y => new { y.SchoolId, y.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Classroom>()
            .WithMany()
            .HasForeignKey(e => new { e.SchoolId, e.ClassroomId })
            .HasPrincipalKey(c => new { c.SchoolId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Student>()
            .WithMany()
            .HasForeignKey(e => new { e.SchoolId, e.StudentId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
