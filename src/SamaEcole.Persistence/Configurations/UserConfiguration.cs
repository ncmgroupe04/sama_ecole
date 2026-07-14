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
        builder.Property(u => u.Email).IsRequired().HasMaxLength(255);
        builder.Property(u => u.PasswordHash).IsRequired().HasMaxLength(500);
        builder.Property(u => u.FullName).IsRequired().HasMaxLength(200);
        builder.Property(u => u.Phone).HasMaxLength(30);
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(u => u.Status).HasConversion<string>().HasMaxLength(20);

        // Un email n'appartient qu'à un seul compte sur toute la plateforme (docs/Volume_3_DDS.md §5.2).
        builder.HasIndex(u => u.Email).IsUnique();
        builder.HasIndex(u => u.SchoolId);

        // FK vers schools, nullable pour le Super Admin (docs/Volume_3_DDS.md §5.2).
        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(u => u.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(u => !u.IsDeleted);
    }
}