using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Voir <see cref="InventoryCategoryConfiguration"/> pour le filtre tenant et la policy RLS.
///
/// La contrainte CHECK sur le bénéficiaire est le cœur de ce fichier : sans elle, le modèle à trois
/// clés étrangères nullables autoriserait une fiche « type = Élève » sans élève, ou désignant à la
/// fois un élève et un enseignant — exactement la dérive qu'un identifiant polymorphe générique
/// aurait rendue impossible à détecter.
/// </summary>
public class ItemAssignmentConfiguration : IEntityTypeConfiguration<ItemAssignment>
{
    public void Configure(EntityTypeBuilder<ItemAssignment> builder)
    {
        builder.ToTable("item_assignments");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.SchoolId).IsRequired();
        builder.Property(a => a.ItemId).IsRequired();
        builder.Property(a => a.Quantity).IsRequired();

        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(a => a.BeneficiaryType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(a => a.BeneficiaryLabel).IsRequired().HasMaxLength(150);

        builder.Property(a => a.Status)
            .HasConversion<string>()
            .HasMaxLength(25)
            .IsRequired()
            .HasDefaultValue(AssignmentStatus.EnCours);

        builder.Property(a => a.ReturnCondition)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(a => a.Notes).HasMaxLength(500);

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_item_assignments_quantity_positive", "\"Quantity\" > 0"));

        // Exactement UN bénéficiaire, et il correspond au type déclaré.
        //
        // Chaîne sur UNE seule ligne (\n explicites), jamais un raw string multi-lignes : un littéral
        // qui s'étend sur plusieurs lignes physiques du fichier source contient de VRAIS caractères de
        // fin de ligne, que git réécrit au checkout selon core.autocrlf — CRLF sur Windows, LF partout
        // ailleurs. La migration qui a figé cette contrainte capture un octet précis ; la relire sur une
        // machine dont le retour à la ligne diffère fait croire à EF Core que le modèle a changé
        // (PendingModelChangesWarning, qui casse net tous les tests d'intégration au démarrage). Un \n
        // explicite est un caractère d'échappement, jamais transformé par le checkout.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_item_assignments_beneficiary",
            "((\"StudentId\" IS NOT NULL)::int + (\"TeacherId\" IS NOT NULL)::int + (\"UserId\" IS NOT NULL)::int) = 1\n"
            + "AND (\"BeneficiaryType\" <> 'Eleve' OR \"StudentId\" IS NOT NULL)\n"
            + "AND (\"BeneficiaryType\" <> 'Enseignant' OR \"TeacherId\" IS NOT NULL)\n"
            + "AND (\"BeneficiaryType\" <> 'Personnel' OR \"UserId\" IS NOT NULL)"));

        builder.HasIndex(a => new { a.SchoolId, a.ItemId });
        builder.HasIndex(a => new { a.SchoolId, a.StudentId });
        builder.HasIndex(a => new { a.SchoolId, a.TeacherId });

        // L'index de l'écran « prêts en cours / en retard » : filtre sur le statut, tri sur l'échéance.
        builder.HasIndex(a => new { a.SchoolId, a.Status, a.DueOn });

        builder.HasOne(a => a.Item)
            .WithMany()
            .HasForeignKey(a => a.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Student>()
            .WithMany()
            .HasForeignKey(a => a.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Teacher>()
            .WithMany()
            .HasForeignKey(a => a.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(a => a.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
