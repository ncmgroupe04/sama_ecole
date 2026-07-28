using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici. La
/// policy RLS PostgreSQL équivalente est posée par la migration AddSmsNotifications (règle #2).
///
/// Aucun verrou optimiste xmin : cette table est un JOURNAL (append-only en pratique), jamais
/// modifiée après écriture — il n'y a donc pas d'écrasement concurrent à prévenir. Le compteur qui,
/// lui, se met à jour de façon concurrente est le solde (SchoolSettings.SmsCreditBalance), protégé
/// par un UPDATE atomique côté SmsDispatcher.
/// </summary>
public class SmsMessageConfiguration : IEntityTypeConfiguration<SmsMessage>
{
    public void Configure(EntityTypeBuilder<SmsMessage> builder)
    {
        builder.ToTable("sms_messages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.SchoolId).IsRequired();

        builder.Property(m => m.Recipient).IsRequired().HasMaxLength(20);

        // 5 segments d'UCS-2 : au-delà, aucune école n'envoie de SMS — mais la colonne ne doit pas
        // tronquer silencieusement un corps de message qui sert de preuve de ce qui a été envoyé.
        builder.Property(m => m.Body).IsRequired().HasMaxLength(1000);

        builder.Property(m => m.Trigger).HasConversion<string>().HasMaxLength(30);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.ProviderMessageId).HasMaxLength(120);
        builder.Property(m => m.FailureReason).HasMaxLength(500);

        // L'historique se consulte du plus récent au plus ancien, filtré par école : l'index suit
        // exactement cette lecture (voir GetSmsHistoryQuery).
        builder.HasIndex(m => new { m.SchoolId, m.SentAt });

        // Index de FILE, volontairement PARTIEL (filtre sur le statut) : le worker cherche des
        // messages en attente toutes les quelques secondes, alors que la table est un journal qui ne
        // fait que croître. Sans le filtre, l'index grossirait avec l'historique entier pour ne
        // servir qu'une poignée de lignes vivantes. Non préfixé par SchoolId : le worker balaie
        // TOUTES les écoles (voir claim_pending_sms).
        builder.HasIndex(m => m.NextAttemptAt)
            .HasDatabaseName("IX_sms_messages_queue")
            .HasFilter("\"Status\" = 'Pending'");

        // L'accusé de réception ne connaît que la référence du fournisseur : c'est par elle que le
        // webhook DLR retrouve la ligne, d'où un index dédié.
        builder.HasIndex(m => m.ProviderMessageId)
            .HasFilter("\"ProviderMessageId\" IS NOT NULL");

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(m => m.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK COMPOSITE (SchoolId, StudentId) et non sur le seul identifiant : sur une colonne unique,
        // rien n'empêcherait un SMS d'être rattaché à l'élève d'une AUTRE école. En incluant SchoolId
        // des deux côtés, PostgreSQL rend le croisement de tenants structurellement impossible — même
        // défense que students → classrooms.
        builder.HasOne<Student>()
            .WithMany()
            .HasForeignKey(m => new { m.SchoolId, m.StudentId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
