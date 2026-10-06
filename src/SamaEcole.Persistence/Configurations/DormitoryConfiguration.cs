using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par ApplicationDbContext.OnModelCreating
/// à toute entité ITenantEntity. La policy RLS équivalente vit dans la migration AddBoardingDormitoryModel
/// (AGENTS.md règle #2). Capacité NON stockée : dérivée du nombre de lits (spec N1).
/// </summary>
public class DormitoryConfiguration : IEntityTypeConfiguration<Dormitory>
{
    public void Configure(EntityTypeBuilder<Dormitory> builder)
    {
        builder.ToTable("dormitories");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.SchoolId).IsRequired();
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(d => d.Name).IsRequired().HasMaxLength(100);
        builder.Property(d => d.Gender).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(d => d.SupervisorName).HasMaxLength(150);
        builder.Property(d => d.SupervisorPhone).HasMaxLength(30);
        builder.Property(d => d.Notes).HasMaxLength(1000);

        // Cible des FK composites des chambres (même défense anti cross-tenant que Room).
        builder.HasAlternateKey(d => new { d.SchoolId, d.Id });

        builder.HasIndex(d => new { d.SchoolId, d.Name }).IsUnique()
            .HasDatabaseName("UX_dormitories_name")
            .HasFilter("\"IsDeleted\" = false");

        builder.HasOne<School>().WithMany().HasForeignKey(d => d.SchoolId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(d => d.SupervisorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
