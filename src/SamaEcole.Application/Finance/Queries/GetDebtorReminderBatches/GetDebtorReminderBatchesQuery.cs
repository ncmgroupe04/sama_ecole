using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetDebtorReminderBatches;

/// <summary>
/// GET /finance/dues-reminder-batches — liste des lots de relance de débiteurs (Étape 5), calculés
/// chaque nuit par classe. Répond au Volume 1 §7.5 : « Liste automatique des élèves débiteurs :
/// montant dû, nombre de jours de retard » — jamais implémentée avant cette étape en dehors du calcul
/// interne de SendDuesReminderSmsCommand.
///
/// Lecture ouverte à Directeur/Finance uniquement (comme le tableau de bord financier) : la liste des
/// débiteurs est une donnée financière sensible, pas un simple solde d'élève.
/// </summary>
public record GetDebtorReminderBatchesQuery(DebtorReminderBatchStatus? Status) : IRequest<IReadOnlyList<DebtorReminderBatchDto>>;

public record DebtorReminderBatchDto(
    Guid Id,
    Guid ClassroomId,
    string ClassroomName,
    int ThresholdDays,
    DateTimeOffset GeneratedAt,
    string Status,
    DateTimeOffset? SentAt,
    IReadOnlyList<DebtorReminderBatchItemDto> Items);

public record DebtorReminderBatchItemDto(
    Guid EnrollmentId,
    Guid StudentId,
    string StudentFullName,
    string Matricule,
    string? GuardianPhone,
    decimal RemainingBalance,
    int DaysOverdue);

public class GetDebtorReminderBatchesQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetDebtorReminderBatchesQuery, IReadOnlyList<DebtorReminderBatchDto>>
{
    public async Task<IReadOnlyList<DebtorReminderBatchDto>> Handle(
        GetDebtorReminderBatchesQuery request, CancellationToken cancellationToken)
    {
        var batchesQuery = dbContext.DebtorReminderBatches.AsNoTracking().AsQueryable();
        if (request.Status is not null)
        {
            batchesQuery = batchesQuery.Where(b => b.Status == request.Status);
        }

        var batches = await (
            from b in batchesQuery
            join c in dbContext.Classrooms.AsNoTracking() on b.ClassroomId equals c.Id
            orderby b.GeneratedAt descending
            select new { Batch = b, ClassroomName = c.Name })
            .ToListAsync(cancellationToken);

        if (batches.Count == 0)
        {
            return [];
        }

        var batchIds = batches.Select(b => b.Batch.Id).ToList();

        var items = await (
            from i in dbContext.DebtorReminderBatchItems.AsNoTracking()
            where batchIds.Contains(i.DebtorReminderBatchId)
            join s in dbContext.Students.AsNoTracking() on i.StudentId equals s.Id
            select new { i.DebtorReminderBatchId, Item = i, s.FullName, s.Matricule })
            .ToListAsync(cancellationToken);

        var itemsByBatch = items
            .GroupBy(x => x.DebtorReminderBatchId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<DebtorReminderBatchItemDto>)g
                    .OrderByDescending(x => x.Item.DaysOverdue)
                    .Select(x => new DebtorReminderBatchItemDto(
                        x.Item.EnrollmentId,
                        x.Item.StudentId,
                        x.FullName,
                        x.Matricule,
                        x.Item.GuardianPhone,
                        x.Item.RemainingBalance,
                        x.Item.DaysOverdue))
                    .ToList());

        return batches
            .Select(b => new DebtorReminderBatchDto(
                b.Batch.Id,
                b.Batch.ClassroomId,
                b.ClassroomName,
                b.Batch.ThresholdDays,
                b.Batch.GeneratedAt,
                b.Batch.Status.ToString(),
                b.Batch.SentAt,
                itemsByBatch.GetValueOrDefault(b.Batch.Id, [])))
            .ToList();
    }
}
