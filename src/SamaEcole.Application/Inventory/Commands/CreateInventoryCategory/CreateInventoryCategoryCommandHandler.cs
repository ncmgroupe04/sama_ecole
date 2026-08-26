using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Inventory.Commands.CreateInventoryCategory;

public class CreateInventoryCategoryCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider)
    : IRequestHandler<CreateInventoryCategoryCommand, InventoryCategoryResult>
{
    public async Task<InventoryCategoryResult> Handle(
        CreateInventoryCategoryCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var category = new InventoryCategory
        {
            SchoolId = schoolId,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim()
        };

        dbContext.InventoryCategories.Add(category);

        // Deux catégories de même nom violent l'index unique : SaveChangesAsync traduit la violation
        // en DuplicateRecordException -> 409, jamais un doublon silencieux (AGENTS.md règle #5).
        await dbContext.SaveChangesAsync(cancellationToken);

        var rowVersion = await dbContext.InventoryCategories.AsNoTracking()
            .Where(c => c.Id == category.Id)
            .Select(c => EF.Property<uint>(c, "xmin"))
            .FirstAsync(cancellationToken);

        return new InventoryCategoryResult(category.Id, category.Name, category.Description, rowVersion);
    }
}
