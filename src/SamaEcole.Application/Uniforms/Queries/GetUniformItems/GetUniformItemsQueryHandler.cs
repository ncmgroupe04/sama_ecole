using MediatR;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Uniforms.Queries.GetUniformItems;

public class GetUniformItemsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetUniformItemsQuery, IReadOnlyList<UniformItemDto>>
{
    public Task<IReadOnlyList<UniformItemDto>> Handle(GetUniformItemsQuery request, CancellationToken cancellationToken) =>
        UniformCatalogReader.GetItemsAsync(dbContext, onlyItemId: null, cancellationToken);
}
