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

        // Un versement annulé ne compte dans AUCUN total (même règle que GetFinanceDashboardQuery :
        // un Payment se corrige par Status = Cancelled, jamais par suppression — AGENTS.md règle #6).
        // Le Global Query Filter borne déjà les trois tables au tenant courant.
        var payments = await (
            from payment in dbContext.Payments.AsNoTracking()
            where payment.Status != PaymentStatus.Cancelled
                  && payment.PaidAt >= fromInstant
                  && payment.PaidAt < toExclusive
            join enrollment in dbContext.Enrollments.AsNoTracking()
                on payment.EnrollmentId equals enrollment.Id
            join classroom in dbContext.Classrooms.AsNoTracking()
                on enrollment.ClassroomId equals classroom.Id
            select new
            {
                payment.Amount,
                payment.Method,
                payment.PaidAt,
                ClassroomName = classroom.Name,
                ClassroomLevel = classroom.Level
            })
            .ToListAsync(cancellationToken);

        // Agrégation EN MÉMOIRE et non en SQL : le cycle se déduit du niveau via ClassroomCycle.CycleFor,
        // du code C# qu'aucun GROUP BY ne saurait exécuter. La volumétrie s'y prête — un exercice
        // compte quelques milliers de versements, pas des millions.
        var byCycle = payments
            .GroupBy(p => ClassroomCycle.CycleFor(p.ClassroomLevel))
            .Select(g => new RevenueLine(g.Key.ToString(), g.Sum(p => p.Amount), g.Count()))
            .OrderByDescending(l => l.Amount)
            .ToList();

        var byClassroom = payments
            .GroupBy(p => p.ClassroomName)
            .Select(g => new RevenueLine(g.Key, g.Sum(p => p.Amount), g.Count()))
            .OrderByDescending(l => l.Amount)
            .ToList();

        var byPaymentMethod = payments
            .GroupBy(p => p.Method)
            .Select(g => new RevenueLine(g.Key.ToString(), g.Sum(p => p.Amount), g.Count()))
            .OrderByDescending(l => l.Amount)
            .ToList();

        // Seule série ordonnée CHRONOLOGIQUEMENT (pas par montant décroissant comme les autres) :
        // c'est un graphique d'évolution, pas un classement.
        var byMonth = payments
            .GroupBy(p => new DateOnly(p.PaidAt.Year, p.PaidAt.Month, 1))
            .Select(g => new RevenueMonthLine(g.Key, g.Sum(p => p.Amount), g.Count()))
            .OrderBy(l => l.Period)
            .ToList();

        return new RevenueConsolidationDto(
            from, to,
            payments.Sum(p => p.Amount),
            payments.Count,
            byCycle, byClassroom, byPaymentMethod, byMonth);
    }
}
