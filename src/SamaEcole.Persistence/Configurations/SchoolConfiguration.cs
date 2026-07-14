using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

public class SchoolConfiguration : IEntityTypeConfiguration<School>
{
    public void Configure(EntityTypeBuilder<School> builder)
    {
        builder.ToTable("schools");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).IsRequired().HasMaxLength(200);
        builder.Property(s => s.Address).HasMaxLength(300);
        builder.Property(s => s.Phone).HasMaxLength(30);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasQueryFilter(s => !s.IsDeleted);
    }
}
