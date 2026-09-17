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

        // Verrou optimiste xmin (AGENTS.md règle #5) : UpdateSubjectCommand/DeleteSubjectCommand en
        // dépendent pour refuser en 409 une écriture sur une matière modifiée entre-temps, comme
        // Grade et Enrollment. Propriété fantôme, aucune migration requise (convention Npgsql).
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(s => s.Name).IsRequired().HasMaxLength(80);
        builder.Property(s => s.NameAr).HasMaxLength(80);
        builder.Property(s => s.Level).IsRequired().HasMaxLength(50);

        // decimal(4,2) : de 0,01 à 99,99. Un coefficient n'a aucune raison de dépasser cette borne,
        // et le type fixe évite qu'un float ne fasse dériver « total des points ÷ total des
        // coefficients » d'un centième au moment du calcul de la moyenne.
        builder.Property(s => s.Coefficient).IsRequired().HasPrecision(4, 2);

        // (5,2) et non (4,2) comme Coefficient : un barème de ligne monte à 100 (les grilles observées
        // vont jusqu'à /60), que numeric(4,2) — plafonné à 99,99 — rejetterait en erreur de base plutôt
        // qu'en message de saisie.
        builder.Property(s => s.MaxScore).HasPrecision(5, 2);

        builder.Property(s => s.Column1Header).HasMaxLength(40);
        builder.Property(s => s.Column2Header).HasMaxLength(40);

        // Une matière est unique par (niveau, DOMAINE PARENT, nom) au sein de l'école — pas par nom seul :
        // « Maths » existe légitimement au primaire ET en terminale, avec des coefficients distincts, et
        // « Ressources » existe sous « Français » ET sous « Maths » dans la même grille APC. Sans
        // ParentSubjectId dans la clé, cette seconde ligne serait rejetée comme un doublon.
        //
        // Index PARTIEL (« NOT IsDeleted »), même convention que TeacherAssignmentConfiguration,
        // EnrollmentConfiguration et SchoolConfiguration : sur une table à soft delete, un index unique
        // est toujours partiel. IsDeleted était auparavant DANS la clé (pas en filtre) pour permettre de
        // recréer une matière de même nom après archivage — mais ça plafonnait aussi à UNE seule matière
        // archivée par clé naturelle : reset_school_data détache tous les enfants d'un coup
        // (ParentSubjectId = NULL, y compris sur des lignes déjà supprimées) avant de les effacer, et
        // deux matières archivées finissant avec la même clé après ce détachement violaient l'unicité —
        // alors qu'aucune des deux n'est plus vivante. Un index partiel exclut les lignes supprimées de
        // la contrainte : plus de plafond sur les doublons archivés, la même garantie qu'avant sur les
        // lignes vivantes.
        //
        // AreNullsDistinct(false) reste INDISPENSABLE : PostgreSQL considère par défaut deux NULL comme
        // distincts, et ParentSubjectId est NULL pour toute matière de premier niveau — l'index aurait
        // donc cessé d'interdire deux « Maths » au même niveau, la garantie même qu'il porte.
        // `NULLS NOT DISTINCT` (PostgreSQL 15+, l'image du projet est postgres:16) la rétablit.
        builder.HasIndex(s => new { s.SchoolId, s.Level, s.ParentSubjectId, s.Name })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasFilter("NOT \"IsDeleted\"");
        builder.HasIndex(s => s.SchoolId);

        // Hiérarchie APC domaine → activité, dans la MÊME table (Subject.ParentSubjectId). Restrict :
        // supprimer un domaine qui porte encore des activités laisserait des lignes orphelines,
        // invisibles sur le bulletin ; DeleteSubjectCommandHandler refuse explicitement ce cas en 409.
        builder.HasOne<Subject>()
            .WithMany()
            .HasForeignKey(s => s.ParentSubjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => s.ParentSubjectId);

        // Restrict : on ne supprime jamais physiquement une école (AGENTS.md règle #6).
        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(s => s.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
