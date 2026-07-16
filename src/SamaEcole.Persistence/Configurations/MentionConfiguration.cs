using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL équivalente est posée par la migration AddMentions (AGENTS.md règle #2).
/// </summary>
public class MentionConfiguration : IEntityTypeConfiguration<Mention>
{
    public void Configure(EntityTypeBuilder<Mention> builder)
    {
        builder.ToTable("mentions");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.SchoolId).IsRequired();
        builder.Property(m => m.Label).IsRequired().HasMaxLength(40);
        builder.Property(m => m.MinAverage).IsRequired().HasPrecision(5, 2);

        builder.HasIndex(m => new { m.SchoolId, m.Label, m.IsDeleted }).IsUnique();

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(m => m.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
