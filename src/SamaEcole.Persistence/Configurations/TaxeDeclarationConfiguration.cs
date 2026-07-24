using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

public class TaxeDeclarationConfiguration : IEntityTypeConfiguration<TaxeDeclaration>
{
    public void Configure(EntityTypeBuilder<TaxeDeclaration> builder)
    {
        builder.ToTable("taxe_declarations");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.SchoolId).IsRequired();
        
        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasIndex(t => t.SchoolId);
        // Ensure one declaration per school per month
        builder.HasIndex(t => new { t.SchoolId, t.Month, t.Year }).IsUnique();

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(t => t.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
