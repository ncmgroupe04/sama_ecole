using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Persistence.Configurations;

public class DormitoryRoomConfiguration : IEntityTypeConfiguration<DormitoryRoom>
{
    public void Configure(EntityTypeBuilder<DormitoryRoom> builder)
    {
        builder.ToTable("dormitory_rooms");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.SchoolId).IsRequired();
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(r => r.Name).IsRequired().HasMaxLength(100);

        builder.HasAlternateKey(r => new { r.SchoolId, r.Id });

        builder.HasIndex(r => new { r.SchoolId, r.DormitoryId, r.Name }).IsUnique()
            .HasDatabaseName("UX_dormitory_rooms_name")
            .HasFilter("\"IsDeleted\" = false");

        builder.HasOne<School>().WithMany().HasForeignKey(r => r.SchoolId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Dormitory>().WithMany()
            .HasForeignKey(r => new { r.SchoolId, r.DormitoryId })
            .HasPrincipalKey(d => new { d.SchoolId, d.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
