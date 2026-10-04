using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Voir <see cref="UniformItemConfiguration"/> pour le filtre tenant et la policy RLS.
///
/// Les contraintes CHECK sont le DERNIER rempart, pas le premier : <c>UniformDeliveryPolicy</c> refuse déjà ces cas
/// en 409/422. Elles existent pour qu'aucun chemin — script de reprise, SQL manuel, bug futur — ne puisse laisser
/// une dotation « remise » sans mouvement de stock, ou une remise non facturée sans motif.
/// </summary>
public class StudentUniformConfiguration : IEntityTypeConfiguration<StudentUniform>
{
    public void Configure(EntityTypeBuilder<StudentUniform> builder)
    {
        builder.ToTable("student_uniforms");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.SchoolId).IsRequired();

        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(u => u.Quantity).IsRequired();

        builder.Property(u => u.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(u => u.BillingStatus).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(u => u.NotBilledReason).HasMaxLength(200);
        builder.Property(u => u.ReturnReason).HasMaxLength(200);
        builder.Property(u => u.ItemLabel).HasMaxLength(100);
        builder.Property(u => u.SizeLabel).HasMaxLength(30);
        builder.Property(u => u.StudentLabel).HasMaxLength(200);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_student_uniforms_quantity", "\"Quantity\" > 0");

            // Une tenue remise (ou retournée après l'avoir été) a TOUJOURS sa date, son auteur, sa taille et son
            // mouvement de stock : sans le mouvement, le journal ne recollerait plus avec les compteurs.
            t.HasCheckConstraint(
                "CK_student_uniforms_delivered_has_movement",
                "\"Status\" NOT IN ('Delivered', 'Returned') OR (\"DeliveredOn\" IS NOT NULL " +
                "AND \"DeliveredByUserId\" IS NOT NULL AND \"DeliveryStockMovementId\" IS NOT NULL " +
                "AND \"UniformVariantId\" IS NOT NULL)");

            t.HasCheckConstraint(
                "CK_student_uniforms_returned_has_movement",
                "\"Status\" <> 'Returned' OR (\"ReturnStockMovementId\" IS NOT NULL AND \"ReturnReason\" IS NOT NULL)");

            // Remise non facturée : motif obligatoire ; remise facturée : preuve de facturation obligatoire.
            t.HasCheckConstraint(
                "CK_student_uniforms_not_billed_has_reason",
                "NOT (\"Status\" IN ('Delivered', 'Returned') AND \"BillingStatus\" = 'NotBilled') " +
                "OR \"NotBilledReason\" IS NOT NULL");

            t.HasCheckConstraint(
                "CK_student_uniforms_billed_has_fee_line",
                "\"BillingStatus\" <> 'Billed' OR \"FeeLineId\" IS NOT NULL");
        });

        // Une ligne ACTIVE (demandée ou remise) par élève et par modèle ; la quantité porte le nombre d'exemplaires.
        // Partiel : une ligne annulée ou retournée libère la place.
        builder.HasIndex(u => new { u.SchoolId, u.EnrollmentId, u.UniformItemId })
            .IsUnique()
            .HasDatabaseName("UX_student_uniforms_active")
            .HasFilter("\"Status\" IN ('Requested', 'Delivered') AND \"IsDeleted\" = false");

        // « Quoi remettre cette année ? » (liste à remettre, besoins).
        builder.HasIndex(u => new { u.SchoolId, u.SchoolYearId, u.Status });
        builder.HasIndex(u => new { u.SchoolId, u.UniformVariantId, u.Status });
        builder.HasIndex(u => new { u.SchoolId, u.StudentId });

        builder.HasOne<School>().WithMany().HasForeignKey(u => u.SchoolId).OnDelete(DeleteBehavior.Restrict);

        // FK composites tenant-safe : une FK ne vérifie pas l'école (elle s'exécute hors RLS) ; le couple
        // (SchoolId, X) interdit à une dotation de désigner la ligne d'une autre école.
        builder.HasOne<Student>().WithMany()
            .HasForeignKey(u => new { u.SchoolId, u.StudentId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Enrollment>().WithMany()
            .HasForeignKey(u => new { u.SchoolId, u.EnrollmentId })
            .HasPrincipalKey(e => new { e.SchoolId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SchoolYear>().WithMany()
            .HasForeignKey(u => new { u.SchoolId, u.SchoolYearId })
            .HasPrincipalKey(y => new { y.SchoolId, y.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UniformItem>().WithMany()
            .HasForeignKey(u => new { u.SchoolId, u.UniformItemId })
            .HasPrincipalKey(i => new { i.SchoolId, i.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UniformVariant>().WithMany()
            .HasForeignKey(u => new { u.SchoolId, u.UniformVariantId })
            .HasPrincipalKey(v => new { v.SchoolId, v.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<EnrollmentFeeLine>().WithMany()
            .HasForeignKey(u => new { u.SchoolId, u.FeeLineId })
            .HasPrincipalKey(l => new { l.SchoolId, l.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<StockMovement>().WithMany()
            .HasForeignKey(u => new { u.SchoolId, u.DeliveryStockMovementId })
            .HasPrincipalKey(m => new { m.SchoolId, m.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<StockMovement>().WithMany()
            .HasForeignKey(u => new { u.SchoolId, u.ReturnStockMovementId })
            .HasPrincipalKey(m => new { m.SchoolId, m.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
