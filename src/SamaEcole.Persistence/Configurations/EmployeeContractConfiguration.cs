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

        // Filtre étendu à "EndDate IS NULL" (Volume 1 §14.1) : un contrat CLÔTURÉ ne doit plus bloquer
        // la création d'un nouveau contrat pour la même personne (reprise après un départ) — seul un
        // contrat encore ACTIF doit être unique par enseignant/utilisateur.
        builder.HasIndex(e => e.TeacherId).IsUnique().HasFilter("\"TeacherId\" IS NOT NULL AND \"EndDate\" IS NULL");
        builder.HasIndex(e => e.UserId).IsUnique().HasFilter("\"UserId\" IS NOT NULL AND \"EndDate\" IS NULL");

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
