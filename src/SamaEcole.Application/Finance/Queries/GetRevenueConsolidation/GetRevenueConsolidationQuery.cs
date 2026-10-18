using SamaEcole.Application.Classrooms;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetRevenueConsolidation;

/// <summary>
/// GET /finance/reports/revenue — consolidation des encaissements sur une période, ventilée par
/// CYCLE, par CLASSE et par MODE DE PAIEMENT (Espèces, Wave, Orange Money, Chèque…). Rapport
/// financier avancé : réservé aux formules Standard et Premium (<see cref="Feature.AdvancedFinancialReports"/>).
///
/// Le cycle n'est pas stocké sur la classe comme donnée saisie : il est DÉRIVÉ du niveau par
/// ClassroomCycle.CycleFor, seule source de vérité (voir CycleType). Le calculer ici plutôt que de
/// lire une colonne évite que ce rapport et les bulletins ne classent la même classe différemment.
///
/// Réservé à Directeur et Finance, comme GetFinanceDashboardQuery : c'est la santé financière
/// agrégée de l'établissement, pas une donnée de guichet.
/// </summary>
public record GetRevenueConsolidationQuery : IRequest<RevenueConsolidationDto>
{
    /// <summary>Bornes incluses. Absentes : l'année civile en cours, période la plus souvent demandée.</summary>
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public record RevenueLine(string Label, decimal Amount, int PaymentCount);

/// <summary>Point de la série temporelle du graphique d'évolution — <see cref="Period"/> est le 1er du
/// mois, JAMAIS formaté en français côté serveur : le mois se traduit côté client (Intl.DateTimeFormat,
/// financial-report.js) comme le reste des dates de ce DTO (voir formatDate).</summary>
public record RevenueMonthLine(DateOnly Period, decimal Amount, int PaymentCount);

public record RevenueConsolidationDto(
    DateOnly From,
    DateOnly To,
    decimal TotalCollected,
    int PaymentCount,
    IReadOnlyList<RevenueLine> ByCycle,
    IReadOnlyList<RevenueLine> ByClassroom,
    IReadOnlyList<RevenueLine> ByPaymentMethod,
    IReadOnlyList<RevenueMonthLine> ByMonth);

public class GetRevenueConsolidationQueryHandler(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<GetRevenueConsolidationQuery, RevenueConsolidationDto>
{
    public async Task<RevenueConsolidationDto> Handle(
        GetRevenueConsolidationQuery request, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var from = request.From ?? new DateOnly(today.Year, 1, 1);
        var to = request.To ?? today;

        var fromInstant = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        // Borne HAUTE exclusive sur le lendemain plutôt qu'inclusive sur `to` : un versement encaissé
        // à 14 h le dernier jour de la période serait sinon exclu, la comparaison portant sur un
        // instant et non sur une date.
        var toExclusive = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        // Registre daté (PaymentLedger) : un versement annulé APRÈS la période y reste compté (un état déjà remis ne
        // change pas), un versement annulé pendant la période en sort, et l'annulation d'un versement d'une période
        // antérieure est une ligne NÉGATIVE de la période où elle est faite — sans jamais compter deux fois.
        // Le Global Query Filter borne déjà les tables au tenant courant.
        var ledger = await PaymentLedger.EntriesAsync(dbContext, fromInstant, toExclusive, cancellationToken);

        var enrollmentIds = ledger.Select(e => e.EnrollmentId).Distinct().ToList();
        var classrooms = await (
            from enrollment in dbContext.Enrollments.AsNoTracking()
            join classroom in dbContext.Classrooms.AsNoTracking()
                on enrollment.ClassroomId equals classroom.Id
            where enrollmentIds.Contains(enrollment.Id)
            select new { EnrollmentId = enrollment.Id, ClassroomName = classroom.Name, ClassroomLevel = classroom.Level })
            .ToDictionaryAsync(x => x.EnrollmentId, cancellationToken);

        var payments = ledger
            .Where(e => classrooms.ContainsKey(e.EnrollmentId))
            .Select(e => new
            {
                e.Amount,
                e.Method,
                PaidAt = e.At,
                // +1 pour un versement, −1 pour l'annulation d'un versement antérieur : le nombre de versements
                // se compense comme les montants.
                Count = e.IsReversal ? -1 : 1,
                classrooms[e.EnrollmentId].ClassroomName,
                classrooms[e.EnrollmentId].ClassroomLevel
            })
            .ToList();

        // Agrégation EN MÉMOIRE et non en SQL : le cycle se déduit du niveau via ClassroomCycle.CycleFor,
        // du code C# qu'aucun GROUP BY ne saurait exécuter. La volumétrie s'y prête — un exercice
        // compte quelques milliers de versements, pas des millions.
        var byCycle = payments
            .GroupBy(p => ClassroomCycle.CycleFor(p.ClassroomLevel))
            .Select(g => new RevenueLine(g.Key.ToString(), g.Sum(p => p.Amount), g.Sum(p => p.Count)))
            .OrderByDescending(l => l.Amount)
            .ToList();

        var byClassroom = payments
            .GroupBy(p => p.ClassroomName)
            .Select(g => new RevenueLine(g.Key, g.Sum(p => p.Amount), g.Sum(p => p.Count)))
            .OrderByDescending(l => l.Amount)
            .ToList();

        var byPaymentMethod = payments
            .GroupBy(p => p.Method)
            .Select(g => new RevenueLine(g.Key.ToString(), g.Sum(p => p.Amount), g.Sum(p => p.Count)))
            .OrderByDescending(l => l.Amount)
            .ToList();

        // Seule série ordonnée CHRONOLOGIQUEMENT (pas par montant décroissant comme les autres) :
        // c'est un graphique d'évolution, pas un classement.
        var byMonth = payments
            .GroupBy(p => new DateOnly(p.PaidAt.Year, p.PaidAt.Month, 1))
            .Select(g => new RevenueMonthLine(g.Key, g.Sum(p => p.Amount), g.Sum(p => p.Count)))
            .OrderBy(l => l.Period)
            .ToList();

        return new RevenueConsolidationDto(
            from, to,
            payments.Sum(p => p.Amount),
            payments.Sum(p => p.Count),
            byCycle, byClassroom, byPaymentMethod, byMonth);
    }
}
