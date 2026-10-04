using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Uniforms.Commands.UpdateUniformItem;

public class UpdateUniformItemCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<UpdateUniformItemCommand, UniformItemDto>
{
    public async Task<UniformItemDto> Handle(UpdateUniformItemCommand request, CancellationToken cancellationToken)
    {
        var item = await dbContext.UniformItems.FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Modèle de tenue {request.Id} introuvable.");

        if (request.FeeCategoryId is { } feeCategoryId
            && !await dbContext.FeeCategories.AnyAsync(c => c.Id == feeCategoryId, cancellationToken))
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.FeeCategoryId), "Le type de frais indiqué n'existe pas dans votre établissement.")]);
        }

        dbContext.SetOriginalConcurrencyToken(item, request.RowVersion);

        item.Name = request.Name.Trim();
        item.Kind = request.Kind;
        item.Gender = request.Gender;
        item.Cycle = request.Cycle;
        item.FeeCategoryId = request.FeeCategoryId;
        item.QuantityIncluded = request.QuantityIncluded;
        item.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();

        // Le lot de chaque taille porte « Modèle — Taille » : il suit le renommage, pour rester lisible dans
        // l'écran Inventaire. Renommer ne touche à aucune quantité ni au journal de stock.
        var variants = await dbContext.UniformVariants
            .Where(v => v.UniformItemId == item.Id)
            .ToListAsync(cancellationToken);

        if (variants.Count > 0)
        {
            var lotIds = variants.Select(v => v.InventoryItemId).ToList();
            var lots = await dbContext.InventoryItems.Where(l => lotIds.Contains(l.Id)).ToListAsync(cancellationToken);

            foreach (var variant in variants)
            {
                var lot = lots.SingleOrDefault(l => l.Id == variant.InventoryItemId);
                if (lot is not null) lot.Name = UniformLotNaming.Build(item.Name, variant.SizeLabel);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return await UniformCatalogReader.GetItemAsync(dbContext, item.Id, cancellationToken);
    }
}
