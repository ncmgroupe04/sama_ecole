using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating pour toute entité ITenantEntity. La policy RLS PostgreSQL
/// équivalente vit dans la migration AddClassJournal (AGENTS.md règle #2).
/// </summary>
public class ClassJournalEntryConfiguration : IEntityTypeConfiguration<ClassJournalEntry>
{
    public void Configure(EntityTypeBuilder<ClassJournalEntry> builder)
    {
        builder.ToTable("class_journal_entries");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.SchoolId).IsRequired();

        // Verrou optimiste : une entrée reste modifiable (par son auteur puis, après 15 jours, par
        // Directeur/Secrétariat), contrairement à stock_movements (append-only, sans xmin).
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(e => e.Topic).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Content).IsRequired().HasMaxLength(4000);
        builder.Property(e => e.Homework).HasMaxLength(2000);

        // Pas d'index unique sur (SchoolId, ClassroomId, SubjectId, SessionDate) : contrairement à
        // l'appel (un seul par créneau), rien n'interdit une double période de la même matière le
        // même jour — le ticket ne demande pas cette contrainte, ne pas l'inventer.
        builder.HasIndex(e => new { e.SchoolId, e.ClassroomId, e.SessionDate });
        builder.HasIndex(e => new { e.SchoolId, e.TeacherId, e.SessionDate });

        // FK COMPOSITES tenant-safe (même motif qu'AttendanceSheetConfiguration) : le croisement de
        // tenants devient structurellement impossible, pas seulement filtré à la lecture.
        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(e => e.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Classroom>()
            .WithMany()
            .HasForeignKey(e => new { e.SchoolId, e.ClassroomId })
            .HasPrincipalKey(c => new { c.SchoolId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Subject>()
            .WithMany()
            .HasForeignKey(e => new { e.SchoolId, e.SubjectId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Teacher>()
            .WithMany()
            .HasForeignKey(e => new { e.SchoolId, e.TeacherId })
            .HasPrincipalKey(t => new { t.SchoolId, t.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
