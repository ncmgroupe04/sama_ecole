using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

public class EmployeeContractConfiguration : IEntityTypeConfiguration<EmployeeContract>
{
    public void Configure(EntityTypeBuilder<EmployeeContract> builder)
    {
        builder.ToTable("employee_contracts");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.SchoolId).IsRequired();
        
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(e => e.SchoolId);
        
        builder.HasIndex(e => e.TeacherId).IsUnique().HasFilter("\"TeacherId\" IS NOT NULL");
        builder.HasIndex(e => e.UserId).IsUnique().HasFilter("\"UserId\" IS NOT NULL");

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(e => e.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Teacher)
            .WithMany()
            .HasForeignKey(e => e.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);
            
        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
