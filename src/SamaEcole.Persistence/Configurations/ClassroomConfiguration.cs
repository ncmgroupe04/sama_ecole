using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL équivalente est posée par la migration AddClassrooms, qui inscrit aussi
/// `classrooms` dans TenantTables (AGENTS.md règle #2 : les DEUX protections, jamais une seule).
/// </summary>
public class ClassroomConfiguration : IEntityTypeConfiguration<Classroom>
{
    public void Configure(EntityTypeBuilder<Classroom> builder)
    {
        builder.ToTable("classrooms");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.SchoolId).IsRequired();

        // Verrou optimiste xmin (AGENTS.md règle #5) : UpdateClassroomCommand/DeleteClassroomCommand
        // en dépendent pour refuser en 409 une écriture sur une classe modifiée entre-temps, comme
        // Grade et Enrollment. Propriété fantôme, aucune migration requise (convention Npgsql).
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(c => c.Name).IsRequired().HasMaxLength(50);
        builder.Property(c => c.Level).IsRequired().HasMaxLength(50);

        // Persisté en string comme tous les enums métier (cf. EnrollmentConfiguration, PaymentConfiguration).
        // HasDefaultValue applique le convertisseur : la migration écrit defaultValue: "College", ce qui
        // renseigne automatiquement la colonne NOT NULL pour les classrooms déjà en base (aucun downtime).
        //
        // HasSentinel(College) est INDISPENSABLE ici : avec HasDefaultValue, EF n'envoie la valeur à
        // l'INSERT que si elle DIFFÈRE de la « sentinelle » (sinon il laisse le DEFAULT SGBD s'appliquer).
        // Or `CycleType.Primaire = 0 = default(CycleType)`, et la détection automatique de sentinelle
        // depuis l'initialiseur `= College` échoue parce que Classroom porte des membres `required`
        // (Name, Level) — EF retombe alors sur default(T) = Primaire. Résultat sans ce réglage : une
        // classe `Cycle = Primaire` est prise pour « non renseignée », le DEFAULT "College" s'applique,
        // et le cycle Primaire (barème /10, moyenne simple) devient IMPOSSIBLE à enregistrer. En fixant
        // la sentinelle sur College (le vrai défaut voulu), seule une valeur College est omise ; Primaire
        // et Lycée sont toujours envoyés et correctement persistés.
        builder.Property(c => c.Cycle)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(CycleType.College)
            .HasSentinel(CycleType.College);

        // Deux classes ne peuvent pas porter le même nom dans la même école — mais « CM2 A » peut
        // évidemment exister dans deux écoles différentes. Le soft delete fait partie de la clé :
        // sans lui, on ne pourrait jamais recréer une classe portant le nom d'une classe archivée.
        builder.HasIndex(c => new { c.SchoolId, c.Name, c.IsDeleted }).IsUnique();
        builder.HasIndex(c => c.SchoolId);

        // Restrict : on ne supprime jamais physiquement une école (AGENTS.md règle #6).
        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(c => c.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
