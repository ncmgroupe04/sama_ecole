using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Pas de policy RLS ni de Global Query Filter SchoolId : cette table ne porte aucune donnée
/// d'établissement et doit rester lisible AVANT toute résolution de tenant (celui qui a oublié son mot
/// de passe n'est pas authentifié). Même parti pris, et mêmes raisons, que
/// <see cref="RefreshTokenConfiguration"/> — voir l'entité PasswordResetToken.
/// </summary>
public class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("password_reset_tokens");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.UserId).IsRequired();
        builder.Property(t => t.TokenHash).IsRequired().HasMaxLength(64);
        builder.Property(t => t.ExpiresAt).IsRequired();

        // La vérification se fait PAR le condensat : c'est le chemin d'accès principal, et l'unicité
        // empêche deux comptes de partager un même jeton.
        builder.HasIndex(t => t.TokenHash).IsUnique();

        // Périmer les demandes en cours d'un compte (nouvelle demande, ou réinitialisation réussie)
        // parcourt les jetons par utilisateur.
        builder.HasIndex(t => t.UserId);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
