using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating pour toute entité ITenantEntity — ne pas en
/// ajouter un ici. La policy RLS PostgreSQL équivalente vit dans la migration AddTeachers
/// (AGENTS.md règle #2).
/// </summary>
public class TeacherConfiguration : IEntityTypeConfiguration<Teacher>
{
    public void Configure(EntityTypeBuilder<Teacher> builder)
    {
        builder.ToTable("teachers");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.SchoolId).IsRequired();

        // Verrou optimiste xmin (AGENTS.md règle #5) : UpdateTeacherCommand/DeleteTeacherCommand en
        // dépendent pour refuser en 409 une écriture sur une fiche enseignant modifiée entre-temps,
        // comme Grade et Enrollment. Propriété fantôme, aucune migration requise (convention Npgsql).
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(t => t.Matricule).IsRequired().HasMaxLength(30);
        builder.Property(t => t.FullName).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Email).IsRequired().HasMaxLength(255);
        builder.Property(t => t.BirthPlace).HasMaxLength(200);
        builder.Property(t => t.Address).HasMaxLength(300);
        builder.Property(t => t.PhotoUrl).HasMaxLength(500);
        builder.Property(t => t.PhotoData);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);

        // Un matricule est unique par école, pas globalement (même règle que Student).
        builder.HasIndex(t => new { t.SchoolId, t.Matricule }).IsUnique();
        builder.HasIndex(t => t.SchoolId);

        // Un compte de connexion ne peut être rattaché qu'à UNE fiche enseignant (ticket JGK-D06).
        // Index unique partiel : plusieurs fiches peuvent rester sans compte (UserId NULL).
        builder.HasIndex(t => t.UserId)
            .IsUnique()
            .HasFilter("\"UserId\" IS NOT NULL");

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(t => t.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK vers users (simple, non composite) : users n'est pas ITenantEntity et sa clé est le seul
        // Id. Restrict — on ne supprime jamais physiquement un compte (soft delete, règle #6).
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
