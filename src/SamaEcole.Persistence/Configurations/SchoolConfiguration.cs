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

        // Coordonnées et mentions légales de l'en-tête du reçu. NINEA (9 chiffres + 1 lettre en
        // pratique) et RCCM (« SN DKR 2020 B 1234 ») restent des chaînes LIBRES : le format officiel a
        // déjà changé, et un établissement ne doit pas être bloqué par notre idée de la nomenclature.
        builder.Property(s => s.Email).HasMaxLength(150);
        builder.Property(s => s.Ninea).HasMaxLength(50);
        builder.Property(s => s.RegistreCommerce).HasMaxLength(50);

        // En-tête administratif du bulletin (IA / IEF / LYCEE DE) — mêmes ordres de grandeur que
        // l'adresse : des libellés courts, jamais des paragraphes.
        builder.Property(s => s.InspectionAcademie).HasMaxLength(150);
        builder.Property(s => s.InspectionEducationFormation).HasMaxLength(150);
        builder.Property(s => s.NomLycee).HasMaxLength(150);

        builder.HasQueryFilter(s => !s.IsDeleted);
    }
}
