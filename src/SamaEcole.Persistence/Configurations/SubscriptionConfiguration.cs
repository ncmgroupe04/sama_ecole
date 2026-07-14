using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("subscriptions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Plan).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);

        // Un seul abonnement par école (docs/Volume_3_DDS.md §5.6).
        builder.HasIndex(s => s.SchoolId).IsUnique();

        // FK vers schools (docs/Volume_3_DDS.md §5.6).
        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(s => s.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(s => !s.IsDeleted);
    }
}