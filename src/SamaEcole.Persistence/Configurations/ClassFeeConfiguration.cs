using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL équivalente est posée par la migration AddFees (AGENTS.md règle #2).
///
/// Le verrou optimiste exploite la colonne système xmin de PostgreSQL (AGENTS.md règle #5,
/// docs/Volume_3_DDS.md §1.3). On déclare une propriété FANTÔME uint « xmin » marquée IsRowVersion
/// (jeton de concurrence + valeur régénérée à chaque écriture) : la convention Npgsql reconnaît ce
/// motif et la mappe sur la colonne système xmin — sans jamais générer d'opération de migration pour
/// elle, puisque la colonne existe déjà dans toute table PostgreSQL.
/// </summary>
public class ClassFeeConfiguration : IEntityTypeConfiguration<ClassFee>
{
    public void Configure(EntityTypeBuilder<ClassFee> builder)
    {
        builder.ToTable("class_fees");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.SchoolId).IsRequired();

        builder.Property<uint>("xmin").IsRowVersion();

        // numeric(12,2) : jusqu'à 9 999 999 999,99 F — largement de quoi couvrir une scolarité
        // annuelle. Le type fixe évite les dérives d'arrondi d'un float sur des montants d'argent.
        builder.Property(f => f.Amount).IsRequired().HasPrecision(12, 2);

        // Une seule ligne de barème par (catégorie, classe) dans une école. Le soft delete fait
        // partie de la clé, comme partout ailleurs.
        builder.HasIndex(f => new { f.SchoolId, f.FeeCategoryId, f.ClassroomId, f.IsDeleted }).IsUnique();
        builder.HasIndex(f => new { f.SchoolId, f.FeeCategoryId });

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(f => f.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK COMPOSITES (SchoolId, …), et non de simples FK sur l'identifiant : sur la seule colonne,
        // rien n'empêcherait une ligne de barème de pointer la classe ou la catégorie d'une AUTRE
        // école. En incluant SchoolId des deux côtés, PostgreSQL rend le croisement de tenants
        // structurellement impossible — la même défense que pour students → classrooms (JGK-C02).
        builder.HasOne<Classroom>()
            .WithMany()
            .HasForeignKey(f => new { f.SchoolId, f.ClassroomId })
            .HasPrincipalKey(c => new { c.SchoolId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<FeeCategory>()
            .WithMany()
            .HasForeignKey(f => new { f.SchoolId, f.FeeCategoryId })
            .HasPrincipalKey(c => new { c.SchoolId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
