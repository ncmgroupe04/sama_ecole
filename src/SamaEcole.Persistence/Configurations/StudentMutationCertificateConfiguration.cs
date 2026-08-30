using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Certificats de mutation (module Intégration étatique, ticket JGK-M06) — table tenant, protégée par
/// la double barrière AGENTS.md règle #2 : Global Query Filter EF Core (posé automatiquement par
/// ApplicationDbContext pour toute ITenantEntity) + policy RLS posée par la migration
/// AddStateIntegrationModule. Voir docs/Volume_3_DDS.md §5.11.
/// </summary>
public class StudentMutationCertificateConfiguration : IEntityTypeConfiguration<StudentMutationCertificate>
{
    public void Configure(EntityTypeBuilder<StudentMutationCertificate> builder)
    {
        builder.ToTable("student_mutation_certificates");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.SchoolId).IsRequired();

        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(c => c.CertificateNumber).IsRequired().HasMaxLength(30);
        builder.Property(c => c.VerificationCode).IsRequired().HasMaxLength(32);
        builder.Property(c => c.DestinationSchoolName).HasMaxLength(150);
        builder.Property(c => c.DestinationCity).HasMaxLength(100);
        builder.Property(c => c.ReasonDetails).HasMaxLength(300);
        builder.Property(c => c.ClassroomNameSnapshot).IsRequired().HasMaxLength(100);
        builder.Property(c => c.RevocationReason).HasMaxLength(300);
        builder.Property(c => c.WasFinanciallyClear).IsRequired();

        builder.Property(c => c.Reason)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired()
            .HasDefaultValue(StudentMutationReason.Autre);

        // Le numéro est unique PAR ÉCOLE, comme le matricule et le numéro de reçu : deux
        // établissements peuvent légitimement émettre chacun leur « MUT-2026-0001 ».
        builder.HasIndex(c => new { c.SchoolId, c.CertificateNumber }).IsUnique();

        // Le code de vérification, lui, est unique GLOBALEMENT et volontairement SANS SchoolId : le
        // point de vérification publique reçoit un code nu, sans tenant — c'est un tiers extérieur à
        // la plateforme qui scanne le QR. Un index borné à l'école serait inutilisable par ce chemin,
        // et deux écoles pourraient tirer le même code.
        builder.HasIndex(c => c.VerificationCode).IsUnique();

        builder.HasIndex(c => new { c.SchoolId, c.StudentId });

        // Restrict des deux côtés : aucune suppression physique de donnée métier (règle #6), et un
        // certificat délivré survit de toute façon à l'archivage de l'élève — c'est une pièce remise
        // à un tiers, elle doit rester vérifiable.
        builder.HasOne(c => c.Student)
            .WithMany()
            .HasForeignKey(c => c.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.SchoolYear)
            .WithMany()
            .HasForeignKey(c => c.SchoolYearId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(c => c.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
