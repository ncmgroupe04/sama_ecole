using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Journal append-only des changements de contrat (Volume 1 §14.1).
/// Table tenant : policy RLS + Global Query Filter, comme toute donnée d'établissement (règle #2).
/// </summary>
public class EmployeeContractHistoryConfiguration : IEntityTypeConfiguration<EmployeeContractHistory>
{
    public void Configure(EntityTypeBuilder<EmployeeContractHistory> builder)
    {
        builder.ToTable("employee_contract_histories");

        builder.HasKey(h => h.Id);
        builder.Property(h => h.SchoolId).IsRequired();
        builder.Property(h => h.EmployeeContractId).IsRequired();
        builder.Property(h => h.ChangeType).HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.Reason).IsRequired().HasMaxLength(500);
        builder.Property(h => h.ChangedByUserId).IsRequired();
        builder.Property(h => h.ChangedAt).IsRequired();

        // Chemin d'accès principal : « l'historique de CE contrat, du plus récent au plus ancien ».
        builder.HasIndex(h => new { h.EmployeeContractId, h.ChangedAt });

        builder.HasOne(h => h.EmployeeContract)
            .WithMany()
            .HasForeignKey(h => h.EmployeeContractId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
