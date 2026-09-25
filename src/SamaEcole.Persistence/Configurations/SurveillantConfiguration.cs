using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

public class DisciplineRecordConfiguration : IEntityTypeConfiguration<DisciplineRecord>
{
    public void Configure(EntityTypeBuilder<DisciplineRecord> builder)
    {
        builder.HasIndex(e => new { e.SchoolId, e.StudentId });
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(50);
        builder.Property(e => e.Reason).HasMaxLength(1000);
        // Date pure (saisie "JJ/MM/AAAA" côté vue), pas un instant : mappée en "date" plutôt que le
        // "timestamp with time zone" par défaut de Npgsql pour DateTime, qui rejette tout DateTime dont
        // le Kind n'est pas explicitement Utc (le JSON entrant a Kind=Unspecified) — voir LateArrival.
        builder.Property(e => e.Date).HasColumnType("date");
    }
}

public class AbsenceJustificationConfiguration : IEntityTypeConfiguration<AbsenceJustification>
{
    public void Configure(EntityTypeBuilder<AbsenceJustification> builder)
    {
        builder.HasIndex(e => new { e.SchoolId, e.StudentId });
        builder.Property(e => e.Reason).HasMaxLength(1000);
    }
}

public class LateArrivalConfiguration : IEntityTypeConfiguration<LateArrival>
{
    public void Configure(EntityTypeBuilder<LateArrival> builder)
    {
        builder.HasIndex(e => new { e.SchoolId, e.StudentId });
        builder.Property(e => e.Reason).HasMaxLength(1000);
        builder.Property(e => e.Observations).HasMaxLength(1000);
        // Voir DisciplineRecordConfiguration.Date : une date pure, mappée en "date" pour éviter le rejet
        // Npgsql "Cannot write DateTime with Kind=Unspecified to timestamp with time zone" (le JSON
        // entrant "2026-07-26" n'a pas de Kind Utc explicite).
        builder.Property(e => e.Date).HasColumnType("date");

        // ---- Circuit du billet d'entrée (Évolution N°5) ----
        // Index simple sur l'élève CONSERVÉ : l'index unique ci-dessous est partiel (il ne sert qu'aux billets
        // actifs), EF le jugerait sinon redondant et retirerait l'index de la clé étrangère.
        builder.HasIndex(e => e.StudentId);

        // Billet par heure d'arrivée (Complément N°5 bis) : colonnes nullables uniquement, sans table ni FK —
        // ce sont des instantanés du calcul d'émission. TimeOnly → time, Guid[] → uuid[] (Npgsql).
        builder.Property(e => e.ArrivalTime).HasColumnType("time without time zone");

        // Statut en TEXTE, nullable et SANS valeur par défaut : null = billet sans cours visé, c'est l'état de
        // tout billet existant (aucune migration de données).
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.PreviousStatus).HasConversion<string>().HasMaxLength(20);

        // AU PLUS UN billet actif (émis ou accepté) par élève, cours et jour : un second est refusé (409).
        // Un billet annulé libère la clé ; les billets sans cours visé (TargetScheduleSlotId NULL) ne se
        // heurtent jamais entre eux — NULL est toujours distinct d'un autre NULL dans un index unique.
        builder.HasIndex(e => new { e.StudentId, e.TargetScheduleSlotId, e.Date })
            .IsUnique()
            .HasDatabaseName("UX_LateArrivals_ActiveTicket")
            .HasFilter("\"Status\" IN ('Issued', 'Accepted') AND NOT \"IsDeleted\"");

        builder.HasOne<ScheduleSlot>()
            .WithMany()
            .HasForeignKey(e => e.TargetScheduleSlotId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class TeacherAttendanceConfiguration : IEntityTypeConfiguration<TeacherAttendance>
{
    public void Configure(EntityTypeBuilder<TeacherAttendance> builder)
    {
        builder.HasIndex(e => new { e.SchoolId, e.TeacherId, e.Date }).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(50);
        builder.Property(e => e.Reason).HasMaxLength(1000);
    }
}
