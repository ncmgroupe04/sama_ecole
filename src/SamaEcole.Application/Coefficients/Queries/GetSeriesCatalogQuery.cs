using MediatR;

namespace SamaEcole.Application.Coefficients.Queries;

/// <summary>
/// GET /api/v1/coefficients/catalog — le catalogue fermé des séries (LyceeSeries), servi par l'API plutôt
/// que recopié en JavaScript : une seule source de vérité, comme ClassroomGradeLevels pour les niveaux.
/// </summary>
public record GetSeriesCatalogQuery : IRequest<IReadOnlyList<SeriesDto>>;

public record SeriesDto(string Code, string Label);

public class GetSeriesCatalogQueryHandler : IRequestHandler<GetSeriesCatalogQuery, IReadOnlyList<SeriesDto>>
{
    public Task<IReadOnlyList<SeriesDto>> Handle(GetSeriesCatalogQuery request, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SeriesDto>>(
            LyceeSeries.All.Select(s => new SeriesDto(s.Code, s.Label)).ToList());
}
