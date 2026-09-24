using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>Paramètres d'établissement (ticket JGK-B02). Table tenant : RLS + Global Query Filter.</summary>
public class SchoolSettingsConfiguration : IEntityTypeConfiguration<SchoolSettings>
{
    public void Configure(EntityTypeBuilder<SchoolSettings> builder)
    {
        builder.ToTable("school_settings");

        builder.HasKey(s => s.Id);

        // UNE seule ligne de réglages par école : deux lignes concurrentes signifieraient que la
        // moitié des inscriptions utiliserait un format et l'autre moitié un autre.
        builder.HasIndex(s => s.SchoolId).IsUnique();

        builder.Property(s => s.GradingScale).IsRequired();
        builder.Property(s => s.StudentMatriculeFormat).IsRequired().HasMaxLength(50);
        builder.Property(s => s.TeacherMatriculeFormat).IsRequired().HasMaxLength(50);
        builder.Property(s => s.AutoLogoutMinutes).IsRequired();
        builder.Property(s => s.DateFormat).IsRequired().HasMaxLength(30);
        builder.Property(s => s.TuitionMonthsPerYear).IsRequired();
        builder.Property(s => s.AllowSecretaryToManageGrading).IsRequired();
        builder.Property(s => s.AllowFinanceToModifyFees).IsRequired();
        builder.Property(s => s.AllowFinanceToDeleteFees).IsRequired();

        // Défaut en base = 7 (SchoolSettingsDefaults.DebtorReminderThresholdDays) : les écoles déjà
        // existantes reçoivent la même valeur par défaut qu'une école neuve, jamais 0 (qui relancerait
        // dès le premier jour de retard).
        builder.Property(s => s.DebtorReminderThresholdDays)
            .IsRequired()
            .HasDefaultValue(SamaEcole.Domain.Entities.SchoolSettingsDefaults.DebtorReminderThresholdDays);

        // Défaut en base = 7 : les écoles déjà existantes reçoivent la même fenêtre qu'une école neuve,
        // jamais 0 (qui verrouillerait toute correction Enseignant dès la migration).
        builder.Property(s => s.GradeEditWindowDays)
            .IsRequired()
            .HasDefaultValue(SamaEcole.Domain.Entities.SchoolSettingsDefaults.GradeEditWindowDays);

        // Défaut en base = true pour Pédagogie/Finance (socle métier existant, activé par défaut) et
        // false pour Internat/Coran (aucun module derrière ces deux réglages pour l'instant) — sans
        // ce HasDefaultValue explicite, EF Core scaffolderait le défaut CLR (false) pour les quatre,
        // ce qui fermerait Pédagogie/Finance pour toute école déjà en base au moment de la migration.
        builder.Property(s => s.IsPedagogyEnabled)
            .IsRequired()
            .HasDefaultValue(SchoolSettingsDefaults.IsPedagogyEnabled);
        builder.Property(s => s.IsFinanceEnabled)
            .IsRequired()
            .HasDefaultValue(SchoolSettingsDefaults.IsFinanceEnabled);
        builder.Property(s => s.IsInternatEnabled)
            .IsRequired()
            .HasDefaultValue(SchoolSettingsDefaults.IsInternatEnabled);
        builder.Property(s => s.IsCoranModuleEnabled)
            .IsRequired()
            .HasDefaultValue(SchoolSettingsDefaults.IsCoranModuleEnabled);

        // Contrairement à TypeEtablissement (stocké en int, choix antérieur à la convention actuelle),
        // SchoolType suit la convention devenue systématique du projet : string, jamais un entier qui
        // se briserait silencieusement si l'ordre des membres de l'enum changeait un jour.
        builder.Property(s => s.SchoolType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(SchoolSettingsDefaults.SchoolType);

        builder.Property(s => s.DirectorSignatureUrl).HasMaxLength(500);
        builder.Property(s => s.SecretarySignatureUrl).HasMaxLength(500);
        builder.Property(s => s.CashierSignatureUrl).HasMaxLength(500);
        builder.Property(s => s.OfficialStampUrl).HasMaxLength(500);
        builder.Property(s => s.SurveillantSignatureUrl).HasMaxLength(500);

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(s => s.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
