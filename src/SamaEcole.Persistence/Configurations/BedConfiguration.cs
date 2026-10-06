using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Persistence.Configurations;

public class BedConfiguration : IEntityTypeConfiguration<Bed>
{
    public void Configure(EntityTypeBuilder<Bed> builder)
    {
        builder.ToTable("beds", t =>
        {
            // Occupied est projeté à la lecture, jamais stocké (spec N2).
            t.HasCheckConstraint("CK_beds_stored_status", "\"Status\" IN ('Available','Maintenance')");
            t.HasCheckConstraint("CK_beds_number_positive", "\"BedNumber\" >= 1");
        });

        builder.HasKey(b => b.Id);
        builder.Property(b => b.SchoolId).IsRequired();
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(b => b.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(BedStatus.Available);

        builder.HasAlternateKey(b => new { b.SchoolId, b.Id });

        builder.HasIndex(b => new { b.SchoolId, b.DormitoryRoomId, b.BedNumber }).IsUnique()
            .HasDatabaseName("UX_beds_number")
            .HasFilter("\"IsDeleted\" = false");

        builder.HasOne<School>().WithMany().HasForeignKey(b => b.SchoolId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<DormitoryRoom>().WithMany()
            .HasForeignKey(b => new { b.SchoolId, b.DormitoryRoomId })
            .HasPrincipalKey(r => new { r.SchoolId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
