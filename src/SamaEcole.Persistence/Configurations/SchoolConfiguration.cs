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

        // En-tête administratif du bulletin (IA / IEF / LYCEE DE) — mêmes ordres de grandeur que
        // l'adresse : des libellés courts, jamais des paragraphes.
        builder.Property(s => s.InspectionAcademie).HasMaxLength(150);
        builder.Property(s => s.InspectionEducationFormation).HasMaxLength(150);
        builder.Property(s => s.NomLycee).HasMaxLength(150);

        builder.HasQueryFilter(s => !s.IsDeleted);
    }
}
