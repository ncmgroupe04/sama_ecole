using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Journal APPEND-ONLY des mouvements de stock (module Inventaire).
/// Table tenant : policy RLS + Global Query Filter, comme toute donnée d'établissement (règle #2).
/// L'interdiction d'UPDATE/DELETE est imposée par la migration AddInventoryModule (GRANT SELECT,
/// INSERT seul) — même dispositif que <see cref="FeeChangeHistoryConfiguration"/>. Aucun verrou
/// optimiste : une ligne jamais modifiée n'a rien à verrouiller.
/// </summary>
public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("stock_movements");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.SchoolId).IsRequired();
        builder.Property(m => m.ItemId).IsRequired();

        builder.Property(m => m.Type)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(m => m.Quantity).IsRequired();
        builder.Property(m => m.MovementDate).IsRequired();
        builder.Property(m => m.Reason).IsRequired().HasMaxLength(200);
        builder.Property(m => m.CounterpartyLabel).HasMaxLength(150);

        // Le sens vient du Type, jamais du signe : une quantité nulle ou négative n'a pas de sens ici.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_stock_movements_quantity_positive", "\"Quantity\" > 0"));

        // Chemin d'accès principal : « l'historique de CE lot, du plus récent au plus ancien ».
        builder.HasIndex(m => new { m.SchoolId, m.ItemId, m.MovementDate });

        // Second chemin : le journal de stock de l'école sur une période (fiche d'inventaire, contrôle).
        builder.HasIndex(m => new { m.SchoolId, m.MovementDate });

        builder.HasOne(m => m.Item)
            .WithMany()
            .HasForeignKey(m => m.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        // Pas de contrainte FK vers item_assignments : la fiche de prêt et son mouvement d'attribution
        // sont insérés dans la MÊME transaction, mais une FK imposerait un ordre d'insertion strict
        // pour un lien purement informatif (retrouver la décharge depuis le journal).
        builder.HasIndex(m => m.AssignmentId);

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(m => m.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
