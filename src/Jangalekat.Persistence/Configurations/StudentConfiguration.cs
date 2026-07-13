using Jangalekat.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jangalekat.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating pour toute entité ITenantEntity — ne pas en
/// ajouter un ici, EF Core n'autorise qu'un seul HasQueryFilter par entité.
/// La policy RLS PostgreSQL équivalente doit être ajoutée dans la migration qui crée
/// cette table (AGENTS.md règle #2, docs/Volume_3_DDS.md).
/// </summary>
public class StudentConfiguration : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> builder)
    {
        builder.ToTable("students");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.SchoolId).IsRequired();
        builder.Property(s => s.Matricule).IsRequired().HasMaxLength(30);
        builder.Property(s => s.FullName).IsRequired().HasMaxLength(200);
        builder.Property(s => s.Gender).IsRequired().HasMaxLength(1);

        // Un matricule est unique par école, pas globalement.
        builder.HasIndex(s => new { s.SchoolId, s.Matricule }).IsUnique();
        builder.HasIndex(s => s.SchoolId);
    }
}
