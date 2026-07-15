using MediatR;

namespace SamaEcole.Application.Finance.Queries.GetClassFees;

/// <summary>
/// GET /api/v1/finance/fees — ticket JGK-F01. Le barème de l'école courante : une ligne par
/// (catégorie × classe) déjà définie. Les couples encore vierges n'apparaissent pas — c'est
/// l'interface qui, connaissant catégories et classes, affiche « non défini » là où il manque une
/// ligne. Le tenant vient du JWT (RLS + Global Query Filter).
/// </summary>
public record GetClassFeesQuery : IRequest<IReadOnlyList<ClassFeeDto>>;

/// <summary>
/// <paramref name="RowVersion"/> est le jeton de concurrence (xmin PostgreSQL) : l'interface le
/// renvoie tel quel lors d'une modification, pour que deux éditions concurrentes ne s'écrasent pas
/// (AGENTS.md règle #5). Les noms de catégorie et de classe sont dénormalisés pour que la liste soit
/// lisible sans jointure côté client.
/// </summary>
public record ClassFeeDto(
    Guid Id,
    Guid FeeCategoryId,
    string FeeCategoryName,
    Guid ClassroomId,
    string ClassroomName,
    string Level,
    decimal Amount,
    uint RowVersion);
