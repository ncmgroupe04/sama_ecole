using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating (entité ITenantEntity) — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL correspondante reste à poser avec le ticket JGK-A03, qui installe
/// le mécanisme `app.current_school_id` pour TOUTES les tables tenant (AGENTS.md règle #2).
/// </summary>
public class MatriculeSequenceConfiguration : IEntityTypeConfiguration<MatriculeSequence>
{
    public void Configure(EntityTypeBuilder<MatriculeSequence> builder)
    {
        builder.ToTable("matricule_sequences");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.SchoolId).IsRequired();
        builder.Property(s => s.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.Year).IsRequired();
        builder.Property(s => s.LastValue).IsRequired();

        // Cible du ON CONFLICT de MatriculeGenerator : c'est cet index unique qui sérialise
        // les générations concurrentes pour un même (école, type, année). Ne pas le retirer.
        builder.HasIndex(s => new { s.SchoolId, s.Kind, s.Year }).IsUnique();

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(s => s.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
