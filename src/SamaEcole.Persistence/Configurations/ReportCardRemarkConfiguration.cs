using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL équivalente est posée par la migration AddReportCardRemarks (AGENTS.md
/// règle #2).
/// </summary>
public class ReportCardRemarkConfiguration : IEntityTypeConfiguration<ReportCardRemark>
{
    public void Configure(EntityTypeBuilder<ReportCardRemark> builder)
    {
        builder.ToTable("report_card_remarks");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.SchoolId).IsRequired();

        builder.Property(r => r.DisciplinaryMention).HasConversion<string>().HasMaxLength(30);
        builder.Property(r => r.CouncilDecision).HasConversion<string>().HasMaxLength(30);
        // 300 caractères : le cadre "Observations du conseil" mesure ~48pt de haut sur le bulletin
        // A5 (ReportCardDocument.ComposeFooter) — au-delà, le texte pousse le document sur une
        // seconde page. Vérifié par ReportCardDocumentTests avec un texte à la borne exacte.
        builder.Property(r => r.Observations).HasMaxLength(300);

        // Une seule remarque par (élève, trimestre) : une seconde écriture MET À JOUR la même ligne
        // (voir UpsertReportCardRemarkCommandHandler), elle n'en crée jamais une seconde.
        builder.HasIndex(r => new { r.SchoolId, r.StudentId, r.TermId, r.IsDeleted })
            .IsUnique()
            .HasDatabaseName("UX_report_card_remarks_single_entry");

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(r => r.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK COMPOSITES (SchoolId, …) : même défense qu'ailleurs (Grade, StudentAttendance) contre une
        // remarque pointant l'élève ou le trimestre d'une AUTRE école.
        builder.HasOne<Student>()
            .WithMany()
            .HasForeignKey(r => new { r.SchoolId, r.StudentId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Term>()
            .WithMany()
            .HasForeignKey(r => new { r.SchoolId, r.TermId })
            .HasPrincipalKey(t => new { t.SchoolId, t.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
