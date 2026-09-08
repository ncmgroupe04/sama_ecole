using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Pas de Global Query Filter SchoolId ici : User.SchoolId est nullable (Super Admin) et le
/// filtre tenant applicable à cette table est traité par le ticket JGK-A03, pas par le mécanisme
/// générique ITenantEntity de ApplicationDbContext.
/// </summary>
public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);

        // `citext` (et non `varchar`) : l'e-mail est un IDENTIFIANT DE COMPTE, la comparaison — donc
        // l'unicité — doit être insensible à la casse, comme l'est déjà le login
        // (auth_find_user_by_email fait lower() = lower()). Sur un btree `varchar`,
        // « Directeur@ecole.sn » et « directeur@ecole.sn » étaient deux comptes distincts.
        // Toute écriture passe malgré tout par EmailNormalizer (minuscules) : citext protège l'unicité,
        // la normalisation garde la valeur stockée propre.
        builder.Property(u => u.Email).IsRequired().HasColumnType("citext");
        builder.Property(u => u.PasswordHash).IsRequired().HasMaxLength(500);
        builder.Property(u => u.FullName).IsRequired().HasMaxLength(200);
        builder.Property(u => u.Phone).HasMaxLength(30);
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(u => u.Status).HasConversion<string>().HasMaxLength(20);

        // Un e-mail n'identifie qu'un seul compte VIVANT sur toute la plateforme (docs/Volume_3_DDS.md
        // §5.2), casse ignorée (colonne citext). Filtré sur IsDeleted : un compte soft-deleted ne
        // réserve plus l'adresse — cohérent avec auth_find_user_by_email, qui ne voit pas les comptes
        // supprimés (sans ce filtre, l'adresse d'un Directeur soft-deleted restait bloquée à jamais).
        builder.HasIndex(u => u.Email).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(u => u.SchoolId);

        // FK vers schools, nullable pour le Super Admin (docs/Volume_3_DDS.md §5.2).
        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(u => u.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(u => !u.IsDeleted);
    }
}