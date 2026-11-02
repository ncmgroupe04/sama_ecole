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
        // Une fiche ACTIVE par employé et par mois. Une fiche annulée (Volume 1 §14.5) reste en base pour
        // l'historique mais libère la période, ce qui permet de régénérer une fiche corrigée.
        builder.HasIndex(f => new { f.SchoolId, f.EmployeeContractId, f.Month, f.Year })
            .IsUnique()
            .HasFilter("\"CancelledAt\" IS NULL");

        builder.Property(f => f.CancellationReason).HasMaxLength(500);
        builder.Property(f => f.PaidWith).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(f => f.IsCancelled);
        builder.Ignore(f => f.IsPaid);

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
