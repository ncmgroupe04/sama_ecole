using MediatR;

namespace SamaEcole.Application.Finance.Queries.GetFeeHistory;

/// <summary>
/// GET /api/v1/finance/fees/{id}/history — ticket JGK-F01, Volume 1 §7.4.
/// L'historique d'une ligne de barème, du plus récent au plus ancien. Tenant du JWT (RLS + filtre).
/// </summary>
public record GetFeeHistoryQuery(Guid ClassFeeId) : IRequest<IReadOnlyList<FeeHistoryDto>>;

/// <summary>
/// <paramref name="OldAmount"/> est null pour la toute première définition du montant (création).
/// <paramref name="ChangedByName"/> est le nom de l'auteur, joint depuis la table des comptes.
/// </summary>
public record FeeHistoryDto(
    decimal? OldAmount,
    decimal NewAmount,
    Guid ChangedByUserId,
    string ChangedByName,
    DateTimeOffset ChangedAt);
