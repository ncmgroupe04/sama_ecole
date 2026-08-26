using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Module Examens officiels — trois tables tenant (exam_sessions, exam_dossiers, exam_results),
/// toutes protégées par la double barrière AGENTS.md règle #2 (Global Query Filter EF Core ici +
/// policy RLS posée par la migration AddExamsModule). Voir docs/Volume_3_DDS.md §5.10.
/// </summary>
public class ExamDossierConfiguration : IEntityTypeConfiguration<ExamDossier>
{
    public void Configure(EntityTypeBuilder<ExamDossier> builder)
    {
        builder.ToTable("exam_dossiers");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.SchoolId).IsRequired();

        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(d => d.CandidateNumber).HasMaxLength(20);
        builder.Property(d => d.ExamCenterName).HasMaxLength(150);
        builder.Property(d => d.BirthCertificateNumber).HasMaxLength(50);
        builder.Property(d => d.BirthCertificatePresent).IsRequired().HasDefaultValue(false);
        builder.Property(d => d.CivilStatusNotes).HasMaxLength(500);

        builder.Property(d => d.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(ExamDossierStatus.Incomplet);

        // Un élève n'a qu'un dossier par session (Volume 1 §22.1).
        builder.HasIndex(d => new { d.SchoolId, d.ExamSessionId, d.StudentId }).IsUnique();

        // Index unique PARTIEL, même dispositif que InventoryItem.Code : le numéro de table est nul
        // tant qu'il n'est pas attribué, et plusieurs dossiers sans numéro doivent coexister.
        builder.HasIndex(d => new { d.SchoolId, d.ExamSessionId, d.CandidateNumber })
            .IsUnique()
            .HasFilter("\"CandidateNumber\" IS NOT NULL");

        builder.HasIndex(d => new { d.SchoolId, d.ClassroomId });
        builder.HasIndex(d => new { d.SchoolId, d.Status });

        builder.HasOne(d => d.ExamSession)
            .WithMany()
            .HasForeignKey(d => d.ExamSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.Student)
            .WithMany()
            .HasForeignKey(d => d.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Figée à l'ouverture (Volume 1 §22.1) : aucun Handler de mise à jour n'expose ClassroomId.
        builder.HasOne(d => d.Classroom)
            .WithMany()
            .HasForeignKey(d => d.ClassroomId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(d => d.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
