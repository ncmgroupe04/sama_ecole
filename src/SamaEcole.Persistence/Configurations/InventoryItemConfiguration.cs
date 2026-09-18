using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Voir <see cref="InventoryCategoryConfiguration"/> pour le filtre tenant et la policy RLS.
///
/// Les contraintes CHECK sur les quantités sont le DERNIER rempart, pas le premier : les Handlers
/// refusent déjà en 422 une sortie supérieure au disponible. Elles existent pour qu'aucun chemin
/// — script de reprise, requête SQL manuelle, bug futur — ne puisse laisser un lot avec un
/// disponible négatif ou supérieur à son total.
/// </summary>
public class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.ToTable("inventory_items");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.SchoolId).IsRequired();

        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(i => i.Name).IsRequired().HasMaxLength(150);
        builder.Property(i => i.Code).HasMaxLength(50);
        builder.Property(i => i.LocationLabel).HasMaxLength(100);
        builder.Property(i => i.Notes).HasMaxLength(500);

        // Persisté en string, comme tous les enums métier (cf. RoomConfiguration.Type).
        //
        // PAS de HasDefaultValue ici : InventoryItem.Condition porte déjà son défaut en C#
        // (= ItemCondition.Bon), le seul chemin de création de ce lot passant par EF. Un défaut
        // généré côté base ET côté C# sur le même enum est ambigu — Neuf (valeur CLR 0 de
        // ItemCondition) ne peut alors plus être distingué d'une valeur non renseignée sans
        // sentinelle dédiée (PendingModelChangesWarning). Autant ne déclarer le défaut qu'une fois,
        // là où il est lu : l'entité.
        builder.Property(i => i.Condition)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // Précision (12,2) : convention de TOUT montant du projet (ClassFee.Amount, Payment.Amount…).
        builder.Property(i => i.UnitPrice).HasPrecision(12, 2);

        builder.Property(i => i.IsConsumable).IsRequired().HasDefaultValue(false);

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_inventory_items_quantities",
            "\"QuantityTotal\" >= 0 AND \"QuantityAvailable\" >= 0 AND \"QuantityAvailable\" <= \"QuantityTotal\""));

        // Index unique PARTIEL : le code est facultatif (saisie libre), et plusieurs lots sans code
        // doivent pouvoir coexister — un index unique classique ne le permettrait pas sous PostgreSQL
        // au-delà de deux NULL... et surtout il interdirait de réutiliser le code d'un lot archivé.
        builder.HasIndex(i => new { i.SchoolId, i.Code })
            .IsUnique()
            .HasFilter("\"Code\" IS NOT NULL AND \"IsDeleted\" = false");

        builder.HasIndex(i => new { i.SchoolId, i.CategoryId });
        builder.HasIndex(i => new { i.SchoolId, i.RoomId });
        builder.HasIndex(i => new { i.SchoolId, i.Name });

        builder.HasOne(i => i.Category)
            .WithMany(c => c.Items)
            .HasForeignKey(i => i.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Emplacement facultatif : un lot peut vivre en « Réserve A », qui n'est pas une Room.
        builder.HasOne(i => i.Room)
            .WithMany()
            .HasForeignKey(i => i.RoomId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(i => i.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
