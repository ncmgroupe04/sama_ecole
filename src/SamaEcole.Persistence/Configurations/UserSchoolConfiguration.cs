using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Table plateforme (comme SubscriptionConfiguration) : aucun Global Query Filter SchoolId, aucune
/// policy RLS — voir <see cref="UserSchool"/> pour la raison, qui n'est pas un allègement mais une
/// nécessité (les lignes recherchées sont celles des autres écoles).
/// </summary>
public class UserSchoolConfiguration : IEntityTypeConfiguration<UserSchool>
{
    public void Configure(EntityTypeBuilder<UserSchool> builder)
    {
        builder.ToTable("user_schools");

        builder.HasKey(us => us.Id);

        // Un rattachement au plus par couple : le soft delete fait partie de la clé, comme partout
        // ailleurs — un rattachement retiré puis rétabli ne doit pas buter sur l'index.
        builder.HasIndex(us => new { us.UserId, us.SchoolId, us.IsDeleted }).IsUnique();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(us => us.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(us => us.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(us => !us.IsDeleted);
    }
}
