using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL équivalente est posée par la migration AddSubjects
/// (AGENTS.md règle #2 : les DEUX protections, jamais une seule).
/// </summary>
public class SubjectConfiguration : IEntityTypeConfiguration<Subject>
{
    public void Configure(EntityTypeBuilder<Subject> builder)
    {
        builder.ToTable("subjects");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.SchoolId).IsRequired();
        builder.Property(s => s.Name).IsRequired().HasMaxLength(80);
        builder.Property(s => s.Level).IsRequired().HasMaxLength(50);

        // decimal(4,2) : de 0,01 à 99,99. Un coefficient n'a aucune raison de dépasser cette borne,
        // et le type fixe évite qu'un float ne fasse dériver « total des points ÷ total des
        // coefficients » d'un centième au moment du calcul de la moyenne.
        builder.Property(s => s.Coefficient).IsRequired().HasPrecision(4, 2);

        // Une matière est unique par (niveau, nom) au sein de l'école — pas par nom seul : « Maths »
        // existe légitimement au primaire ET en terminale, avec des coefficients distincts. Le soft
        // delete fait partie de la clé : sans lui, une matière archivée interdirait à jamais d'en
        // recréer une de même nom au même niveau.
        builder.HasIndex(s => new { s.SchoolId, s.Level, s.Name, s.IsDeleted }).IsUnique();
        builder.HasIndex(s => s.SchoolId);

        // Restrict : on ne supprime jamais physiquement une école (AGENTS.md règle #6).
        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(s => s.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
