using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Global Query Filter (SchoolId + IsDeleted) appliqué par ApplicationDbContext à toute ITenantEntity ; policy RLS
/// équivalente dans la migration AddSyllabusTracking (AGENTS.md règle #2).
/// </summary>
public class ClassJournalEntryUnitConfiguration : IEntityTypeConfiguration<ClassJournalEntryUnit>
{
    public void Configure(EntityTypeBuilder<ClassJournalEntryUnit> builder)
    {
        builder.ToTable("class_journal_entry_units");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.SchoolId).IsRequired();
        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasIndex(l => new { l.ClassJournalEntryId, l.SyllabusUnitId })
            .IsUnique()
            .HasDatabaseName("UX_class_journal_entry_units_link")
            .HasFilter("NOT \"IsDeleted\"");

        builder.HasIndex(l => l.SchoolId);

        builder.HasOne<School>().WithMany().HasForeignKey(l => l.SchoolId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ClassJournalEntry>()
            .WithMany()
            .HasForeignKey(l => new { l.SchoolId, l.ClassJournalEntryId })
            .HasPrincipalKey(e => new { e.SchoolId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SyllabusUnit>()
            .WithMany()
            .HasForeignKey(l => new { l.SchoolId, l.SyllabusUnitId })
            .HasPrincipalKey(u => new { u.SchoolId, u.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
