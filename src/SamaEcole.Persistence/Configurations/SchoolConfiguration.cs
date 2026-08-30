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

        // Identification réglementaire SIMEN (module Intégration étatique, ticket JGK-M05).
        builder.Property(s => s.NationalSchoolCode).HasMaxLength(30);
        builder.Property(s => s.MinistryAuthorizationNumber).HasMaxLength(80);
        builder.Property(s => s.SchoolDistrictCode).HasMaxLength(30);

        // Coordonnées GPS en decimal(9,6) : six décimales ≈ 11 cm au sol, largement au-delà de ce que
        // demande une carte scolaire, et une précision fixe évite les dérives d'arrondi d'un double.
        // La propriété calculée School.GpsCoordinates n'est PAS mappée — elle se dérive de ces deux
        // colonnes à chaque lecture, et la stocker permettrait qu'elle les contredise un jour.
        builder.Property(s => s.GpsLatitude).HasPrecision(9, 6);
        builder.Property(s => s.GpsLongitude).HasPrecision(9, 6);
        builder.Ignore(s => s.GpsCoordinates);

        // Le code établissement national est unique sur TOUTE la plateforme : deux écoles ne peuvent
        // pas déclarer le même code au ministère. Partiel, car il reste nul tant que le Directeur ne
        // l'a pas saisi — ce qui est le cas de toutes les écoles existantes.
        builder.HasIndex(s => s.NationalSchoolCode)
            .IsUnique()
            .HasFilter("\"NationalSchoolCode\" IS NOT NULL");

        // Annuaire public. Ville/région sont des libellés courts (critères de recherche) ; la
        // présentation est bornée à 2000 caractères — assez pour un paragraphe de vitrine, trop peu
        // pour qu'un champ public devienne un vecteur de stockage arbitraire.
        builder.Property(s => s.City).HasMaxLength(120);
        builder.Property(s => s.Region).HasMaxLength(120);
        builder.Property(s => s.PublicDescription).HasMaxLength(2000);

        // Index PARTIEL : l'annuaire ne requête que les écoles ayant consenti, une infime minorité au
        // début. Indexer la table entière ferait payer l'écriture de toutes les autres pour rien.
        builder.HasIndex(s => new { s.City, s.Region })
            .HasDatabaseName("IX_schools_public_directory")
            .HasFilter("\"IsPubliclyListed\" = TRUE AND \"IsDeleted\" = FALSE");

        builder.HasQueryFilter(s => !s.IsDeleted);
    }
}
