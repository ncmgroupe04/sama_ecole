using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.SchoolYears.Queries.GetTerms;
using SamaEcole.Domain.Entities;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.SchoolYears.Commands.ApplyEvaluationPeriods;

public class ApplyEvaluationPeriodsCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    TimeProvider timeProvider)
    : IRequestHandler<ApplyEvaluationPeriodsCommand, IReadOnlyList<TermDto>>
{
    public async Task<IReadOnlyList<TermDto>> Handle(
        ApplyEvaluationPeriodsCommand request, CancellationToken cancellationToken)
    {
        _ = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        // Le Global Query Filter + la policy RLS bornent la recherche à l'école courante : viser l'année
        // d'une autre école renvoie 404, jamais une modification chez le voisin.
        var year = await dbContext.SchoolYears
            .FirstOrDefaultAsync(y => y.Id == request.SchoolYearId, cancellationToken)
            ?? throw new KeyNotFoundException($"Année scolaire {request.SchoolYearId} introuvable.");

        // Année passée en LECTURE SEULE — même garde que la modification de ses dates.
        if (year.IsClosedOn(today))
        {
            throw new ValidationException([
                new ValidationFailure(
                    nameof(request.SchoolYearId),
                    $"L'année scolaire « {year.Label} » est terminée : les années passées sont en lecture seule.")
            ]);
        }

        var current = await dbContext.Terms
            .Where(t => t.SchoolYearId == year.Id)
            .OrderBy(t => t.Order)
            .ToListAsync(cancellationToken);
        var currentIds = current.Select(t => t.Id).ToList();

        // Blocage STRICT : une seule note ou appréciation suffit. Le Global Query Filter écarte déjà
        // les lignes en suppression logique — ce qui est archivé ne retient plus une période.
        var hasGrades = await dbContext.Grades.AnyAsync(g => currentIds.Contains(g.TermId), cancellationToken);
        var hasRemarks = await dbContext.ReportCardRemarks.AnyAsync(r => currentIds.Contains(r.TermId), cancellationToken);
        if (hasGrades || hasRemarks)
        {
            throw new ValidationException([
                new ValidationFailure(
                    nameof(request.SchoolYearId),
                    $"Des notes ou des appréciations de bulletin sont déjà saisies sur l'année « {year.Label} » : "
                    + "son découpage ne peut plus changer. Le nouveau découpage s'appliquera à la prochaine année créée.")
            ]);
        }

        var settings = await dbContext.SchoolSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var wanted = PeriodSchedule.Split(
            year.StartDate,
            year.EndDate,
            settings?.EvaluationPeriodType ?? SchoolSettingsDefaults.EvaluationPeriodType,
            settings?.CustomPeriodCount ?? SchoolSettingsDefaults.CustomPeriodCount);

        // Idempotence : un découpage déjà conforme ne touche à rien (pas de période inutilement archivée).
        var unchanged = current.Count == wanted.Count
            && current.Zip(wanted, (t, w) => t.Label == w.Label && t.StartDate == w.Start && t.EndDate == w.End).All(same => same);

        if (!unchanged)
        {
            // Suppression LOGIQUE (règle #6). L'index unique (SchoolId, SchoolYearId, Order, IsDeleted)
            // laisse les nouvelles lignes reprendre les mêmes rangs : les anciennes ont IsDeleted = true.
            foreach (var term in current)
            {
                term.SoftDelete(actorId.ToString());
            }

            for (var index = 0; index < wanted.Count; index++)
            {
                dbContext.Terms.Add(new Term
                {
                    SchoolId = year.SchoolId,
                    SchoolYearId = year.Id,
                    Label = wanted[index].Label,
                    Order = index + 1,
                    StartDate = wanted[index].Start,
                    EndDate = wanted[index].End
                });
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await dbContext.Terms
            .AsNoTracking()
            .Where(t => t.SchoolYearId == year.Id)
            .OrderBy(t => t.Order)
            .Select(t => new TermDto(t.Id, t.Label, t.Order, t.StartDate, t.EndDate))
            .ToListAsync(cancellationToken);
    }
}
