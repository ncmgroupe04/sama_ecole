using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

public class FichePaieConfiguration : IEntityTypeConfiguration<FichePaie>
{
    public void Configure(EntityTypeBuilder<FichePaie> builder)
    {
        builder.ToTable("fiche_paies");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.SchoolId).IsRequired();
        
        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasIndex(f => f.SchoolId);
        // Ensure one payslip per employee per month
        builder.HasIndex(f => new { f.SchoolId, f.EmployeeContractId, f.Month, f.Year }).IsUnique();

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(f => f.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(f => f.EmployeeContract)
            .WithMany()
            .HasForeignKey(f => f.EmployeeContractId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
