using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Uniforms.Commands.CreateUniformVariant;

public class CreateUniformVariantCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<CreateUniformVariantCommand, UniformVariantDto>
{
    public async Task<UniformVariantDto> Handle(CreateUniformVariantCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var item = await dbContext.UniformItems.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == request.UniformItemId, cancellationToken)
            ?? throw new KeyNotFoundException($"Modèle de tenue {request.UniformItemId} introuvable.");

        // La catégorie « Tenues » de CETTE école (le filtre tenant écarte celle des autres), insensible à la casse :
        // une école qui l'a déjà créée à la main sous « tenues » ne doit pas en voir naître une seconde.
        var category = await dbContext.InventoryCategories
            .FirstOrDefaultAsync(c => c.Name.ToLower() == UniformCatalogRules.InventoryCategoryName.ToLower(), cancellationToken);

        if (category is null)
        {
            category = new InventoryCategory { SchoolId = schoolId, Name = UniformCatalogRules.InventoryCategoryName };
            dbContext.InventoryCategories.Add(category);
        }

        var sizeLabel = request.SizeLabel.Trim();

        // Le lot naît à zéro : le stock entre par un mouvement depuis l'écran Inventaire, pour que le journal raconte
        // l'inventaire depuis sa première unité. Consommable : une tenue se donne, elle ne se prête pas.
        var lot = new InventoryItem
        {
            SchoolId = schoolId,
            Name = UniformLotNaming.Build(item.Name, sizeLabel),
            CategoryId = category.Id,
            QuantityTotal = 0,
            QuantityAvailable = 0,
            Condition = ItemCondition.Neuf,
            IsConsumable = true
        };

        var variant = new UniformVariant
        {
            SchoolId = schoolId,
            UniformItemId = item.Id,
            SizeLabel = sizeLabel,
            SortOrder = request.SortOrder,
            InventoryItemId = lot.Id
        };

        dbContext.InventoryItems.Add(lot);
        dbContext.UniformVariants.Add(variant);

        // Une seule sauvegarde = une seule transaction : une taille en doublon (index unique partiel -> 409) ne laisse
        // aucun lot orphelin ni catégorie « Tenues » créée pour rien.
        await dbContext.SaveChangesAsync(cancellationToken);

        return await UniformCatalogReader.GetVariantAsync(dbContext, variant.Id, cancellationToken);
    }
}
