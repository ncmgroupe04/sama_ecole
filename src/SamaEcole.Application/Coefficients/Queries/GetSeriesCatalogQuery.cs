using MediatR;

namespace SamaEcole.Application.Coefficients.Queries;

/// <summary>
/// GET /api/v1/coefficients/catalog — le catalogue fermé des séries (LyceeSeries), servi par l'API plutôt
/// que recopié en JavaScript : une seule source de vérité, comme ClassroomGradeLevels pour les niveaux.
/// </summary>
public record GetSeriesCatalogQuery : IRequest<IReadOnlyList<SeriesDto>>;

/// <param name="Category">Famille : Littéraire, Scientifique, Technique ou Franco-Arabe — regroupe la liste déroulante.</param>
/// <param name="IsLegacy">Ancien code (L1, TECH) : accepté, affiché pour une classe qui le porte, jamais proposé pour une nouvelle classe.</param>
/// <param name="HasTemplate">Vrai si un modèle national existe : ses matières sont alors injectées dans la classe à sa création.</param>
public record SeriesDto(string Code, string Label, string Category = "", bool IsLegacy = false, bool HasTemplate = false);

public class GetSeriesCatalogQueryHandler : IRequestHandler<GetSeriesCatalogQuery, IReadOnlyList<SeriesDto>>
{
    public Task<IReadOnlyList<SeriesDto>> Handle(GetSeriesCatalogQuery request, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SeriesDto>>(
            LyceeSeries.All
                .Select(s => new SeriesDto(
                    s.Code, s.Label, s.Category, s.IsLegacy, SeriesCoefficientTemplates.For(s.Code).Count > 0))
                .ToList());
}
