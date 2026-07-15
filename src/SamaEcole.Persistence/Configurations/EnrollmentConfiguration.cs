using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL équivalente est posée par la migration AddEnrollments, qui inscrit aussi
/// `enrollments` dans les tables protégées (AGENTS.md règle #2 : les DEUX protections, jamais une seule).
///
/// Le verrou optimiste exploite la colonne système xmin (AGENTS.md règle #5) : TotalDue est une
/// donnée financière, deux corrections concurrentes ne doivent jamais s'écraser en silence.
/// </summary>
public class EnrollmentConfiguration : IEntityTypeConfiguration<Enrollment>
{
    public void Configure(EntityTypeBuilder<Enrollment> builder)
    {
        builder.ToTable("enrollments");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.SchoolId).IsRequired();

        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.TotalDue).IsRequired().HasPrecision(12, 2);
        builder.Property(e => e.ReceiptNumber).HasMaxLength(50).IsRequired();
        builder.Property(e => e.EnrolledAt).IsRequired();

        // Le numéro de reçu est officiel (JGK-E02) : unique par établissement, jamais réémis. L'index
        // (non filtré) vaut même pour une inscription annulée — un numéro consommé reste consommé.
        builder.HasIndex(e => new { e.SchoolId, e.ReceiptNumber })
            .IsUnique()
            .HasDatabaseName("UX_enrollments_receipt_number");

        // « Un élève n'a qu'une inscription active par année scolaire » (DDS §5.4), tenu par la BASE.
        // Index PARTIEL excluant les inscriptions annulées et supprimées : un élève dont l'inscription
        // a été annulée peut être réinscrit, mais jamais inscrit deux fois en même temps sur une année.
        builder.HasIndex(e => new { e.SchoolId, e.StudentId, e.SchoolYearId })
            .IsUnique()
            .HasFilter("NOT \"IsDeleted\" AND \"Status\" <> 'Cancelled'")
            .HasDatabaseName("UX_enrollments_single_active_per_year");

        builder.HasIndex(e => new { e.SchoolId, e.SchoolYearId });
        builder.HasIndex(e => new { e.SchoolId, e.ClassroomId });

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(e => e.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK COMPOSITES (SchoolId, …) : sur la seule colonne d'identité, rien n'empêcherait une
        // inscription de pointer l'élève, la classe ou l'année d'une AUTRE école. En incluant SchoolId
        // des deux côtés, PostgreSQL rend le croisement de tenants structurellement impossible — la
        // même défense que students → classrooms (JGK-C02).
        builder.HasOne<Student>()
            .WithMany()
            .HasForeignKey(e => new { e.SchoolId, e.StudentId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Classroom>()
            .WithMany()
            .HasForeignKey(e => new { e.SchoolId, e.ClassroomId })
            .HasPrincipalKey(c => new { c.SchoolId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SchoolYear>()
            .WithMany()
            .HasForeignKey(e => new { e.SchoolId, e.SchoolYearId })
            .HasPrincipalKey(y => new { y.SchoolId, y.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
