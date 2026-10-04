using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Uniforms.Commands.UpdateUniformVariant;

public class UpdateUniformVariantCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<UpdateUniformVariantCommand, UniformVariantDto>
{
    public async Task<UniformVariantDto> Handle(UpdateUniformVariantCommand request, CancellationToken cancellationToken)
    {
        var variant = await dbContext.UniformVariants.FirstOrDefaultAsync(v => v.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Taille {request.Id} introuvable.");

        var item = await dbContext.UniformItems.AsNoTracking()
            .FirstAsync(i => i.Id == variant.UniformItemId, cancellationToken);

        variant.SizeLabel = request.SizeLabel.Trim();
        variant.SortOrder = request.SortOrder;

        var lot = await dbContext.InventoryItems.FirstOrDefaultAsync(l => l.Id == variant.InventoryItemId, cancellationToken);
        if (lot is not null) lot.Name = UniformLotNaming.Build(item.Name, variant.SizeLabel);

        await dbContext.SaveChangesAsync(cancellationToken);

        return await UniformCatalogReader.GetVariantAsync(dbContext, variant.Id, cancellationToken);
    }
}
