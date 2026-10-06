using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par ApplicationDbContext.OnModelCreating
/// à toute entité ITenantEntity. La policy RLS équivalente vit dans la migration AddDaaraHalqaAndHizbTracking
/// (AGENTS.md règle #2 : les DEUX protections, jamais une seule).
/// </summary>
public class StudentHizbStatusConfiguration : IEntityTypeConfiguration<StudentHizbStatus>
{
    public void Configure(EntityTypeBuilder<StudentHizbStatus> builder)
    {
        builder.ToTable("student_hizb_statuses", table =>
        {
            // Les bornes sont tenues par la BASE, pas seulement par un validateur : un import ou une requête
            // SQL directe ne doit pas pouvoir écrire un Hizb 61 ou une note de 9.
            table.HasCheckConstraint("CK_student_hizb_statuses_hizb_range", "\"HizbNumber\" BETWEEN 1 AND 60");
            table.HasCheckConstraint("CK_student_hizb_statuses_quarters_range", "\"CompletedQuarters\" BETWEEN 0 AND 4");
            table.HasCheckConstraint("CK_student_hizb_statuses_rating_range", "\"Rating\" IS NULL OR \"Rating\" BETWEEN 1 AND 5");

            // L'état n'est pas libre : il est la lecture de CompletedQuarters (0 / 1-3 / 4). Sans cette
            // contrainte, rien n'empêcherait « Completed » avec 2 quarts, deux sources de vérité qui divergent.
            table.HasCheckConstraint(
                "CK_student_hizb_statuses_state_matches_quarters",
                "(\"State\" = 'NotStarted' AND \"CompletedQuarters\" = 0)"
                + " OR (\"State\" = 'InProgress' AND \"CompletedQuarters\" BETWEEN 1 AND 3)"
                + " OR (\"State\" = 'Completed' AND \"CompletedQuarters\" = 4)");
        });

        builder.HasKey(h => h.Id);
        builder.Property(h => h.SchoolId).IsRequired();

        // Verrou optimiste xmin (AGENTS.md règle #5), comme QuranProgress.
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(h => h.State)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(HizbMemorizationState.NotStarted);

        // Un seul état courant par (élève, Hizb). Index PARTIEL : un état supprimé logiquement
        // (règle #6) ne bloque pas la recréation.
        builder.HasIndex(h => new { h.SchoolId, h.StudentId, h.HizbNumber })
            .IsUnique()
            .HasDatabaseName("UX_student_hizb_statuses_key")
            .HasFilter("NOT \"IsDeleted\"");

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(h => h.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK COMPOSITE (SchoolId, StudentId) : rend structurellement impossible un suivi qui pointerait
        // l'élève d'une AUTRE école (la RLS ne contrôle que le SchoolId de la ligne insérée).
        builder.HasOne<Student>()
            .WithMany()
            .HasForeignKey(h => new { h.SchoolId, h.StudentId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
