using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using MediatR;

namespace SamaEcole.Application.Finance.Commands.CreateFeeCategory;

public class CreateFeeCategoryCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider)
    : IRequestHandler<CreateFeeCategoryCommand, CreateFeeCategoryResult>
{
    public async Task<CreateFeeCategoryResult> Handle(CreateFeeCategoryCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var category = new FeeCategory
        {
            SchoolId = schoolId,
            Name = request.Name.Trim(),
            IsRecurring = request.IsRecurring,
            IsBoardingFee = request.IsBoardingFee
        };

        dbContext.FeeCategories.Add(category);

        // Deux catégories de même nom violent l'index unique : SaveChangesAsync traduit la violation
        // en ConcurrencyConflictException → 409, jamais un écrasement silencieux (AGENTS.md règle #5).
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreateFeeCategoryResult(category.Id, category.Name, category.IsRecurring, category.IsBoardingFee);
    }
}
