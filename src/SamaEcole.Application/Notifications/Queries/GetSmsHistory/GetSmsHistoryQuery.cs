using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Notifications.Queries.GetSmsHistory;

/// <summary>
/// GET /sms/history — historique des envois de MON école, avec le solde restant. Le tenant vient du
/// JWT (Global Query Filter), jamais d'un paramètre : aucun établissement ne lit l'historique d'un
/// autre.
///
/// Les échecs et les refus pour solde épuisé figurent dans la liste au même titre que les succès :
/// c'est ce qui permet à une école de comprendre pourquoi une alerte attendue n'est jamais partie.
/// </summary>
public record GetSmsHistoryQuery : IRequest<PaginatedSmsHistory>
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public record SmsHistoryItem(
    Guid Id,
    DateTimeOffset SentAt,
    string Recipient,
    string Body,
    string Trigger,
    string Status,
    int SegmentCount,
    string? FailureReason);

public record PaginatedSmsHistory(
    IReadOnlyList<SmsHistoryItem> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int CreditBalance);

public class GetSmsHistoryQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetSmsHistoryQuery, PaginatedSmsHistory>
{
    public async Task<PaginatedSmsHistory> Handle(
        GetSmsHistoryQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.SmsMessages.AsNoTracking();

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(m => m.SentAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(m => new SmsHistoryItem(
                m.Id, m.SentAt, m.Recipient, m.Body,
                m.Trigger.ToString(), m.Status.ToString(), m.SegmentCount, m.FailureReason))
            .ToListAsync(cancellationToken);

        // Le solde voyage avec l'historique plutôt que dans un second endpoint : l'écran affiche
        // toujours les deux ensemble, et deux appels laisseraient une fenêtre où ils se contredisent.
        var creditBalance = await dbContext.SchoolSettings
            .AsNoTracking()
            .Select(s => s.SmsCreditBalance)
            .FirstOrDefaultAsync(cancellationToken);

        return new PaginatedSmsHistory(items, totalCount, request.Page, request.PageSize, creditBalance);
    }
}
