using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.SoftDelete;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.SchoolYears.Commands.RestoreSchoolYear;

/// <summary>
/// POST /api/v1/school-years/{id}/restore — restaure une année scolaire archivée (mode réel), avec les
/// trimestres et les affectations d'enseignants qui l'ont suivie dans l'archivage.
/// </summary>
public record RestoreSchoolYearCommand(Guid Id) : IRequest<RestoreSchoolYearResult>;

/// <param name="RestoredTerms">Trimestres revenus avec l'année.</param>
/// <param name="RestoredAssignments">Affectations d'enseignants revenues avec l'année.</param>
/// <param name="SkippedAssignments">
/// Affectations laissées supprimées parce que leur enseignant, leur classe ou leur matière a été supprimé depuis :
/// les réactiver créerait une affectation vers un élément inexistant. Elles se recréent explicitement.
/// </param>
public record RestoreSchoolYearResult(Guid Id, string Label, int RestoredTerms, int RestoredAssignments, int SkippedAssignments);

public class RestoreSchoolYearCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    IKpiCacheService kpiCache)
    : IRequestHandler<RestoreSchoolYearCommand, RestoreSchoolYearResult>
{
    public async Task<RestoreSchoolYearResult> Handle(
        RestoreSchoolYearCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Même garde que la suppression : un exercice ne se ressuscite pas sur un seul attribut d'endpoint.
        if (currentUser.Role != Role.Directeur)
        {
            throw new UnauthorizedAccessException("Seul le Directeur peut restaurer une année scolaire.");
        }

        int terms = 0, assignments = 0, skipped = 0;
        string label = string.Empty;

        // Tout se joue dans UN SaveChanges (celui de RestoreAsync) : l'année et ses enfants reviennent ensemble,
        // ou rien ne change.
        var year = await SoftDeleteLifecycle.RestoreAsync(
            dbContext, dbContext.SchoolYears, schoolId, request.Id, "Une année scolaire",
            y => other => other.Label == y.Label,
            async (year, ct) =>
            {
                label = year.Label;

                // Un tombstone encore marqué actif (ligne antérieure à la désactivation à l'archivage) ne revient
                // actif que si l'école n'a pas d'autre année active : l'index « une seule année active » l'impose.
                if (year.IsActive && await dbContext.SchoolYears.AnyAsync(y => y.IsActive, ct))
                {
                    throw new BusinessRuleException(
                        "Impossible de restaurer : une autre année scolaire est déjà active.",
                        SoftDeleteLifecycle.ActiveEntityConflict);
                }

                // Deux années qui se chevauchent rendent « en quelle année sommes-nous ? » sans réponse
                // (même règle que CreateSchoolYear).
                var overlap = await dbContext.SchoolYears
                    .Where(y => y.StartDate <= year.EndDate && year.StartDate <= y.EndDate)
                    .Select(y => y.Label)
                    .FirstOrDefaultAsync(ct);
                if (overlap is not null)
                {
                    throw new BusinessRuleException(
                        $"Impossible de restaurer : cette période chevauche l'année scolaire « {overlap} » déjà enregistrée.",
                        SoftDeleteLifecycle.ActiveEntityConflict);
                }

                // Les enfants partis AVEC l'année (suppression postérieure ou égale à la sienne) reviennent ; ceux
                // supprimés auparavant pour une autre raison restent supprimés.
                var deletedAt = year.DeletedAt;

                var termRows = await dbContext.Terms.IgnoreQueryFilters()
                    .Where(t => t.SchoolId == year.SchoolId && t.SchoolYearId == year.Id
                                && t.IsDeleted && t.DeletedAt >= deletedAt)
                    .ToListAsync(ct);

                // Un rang de période déjà repris par une ligne vivante bloque tout (aucune fusion).
                var liveOrders = await dbContext.Terms
                    .Where(t => t.SchoolYearId == year.Id).Select(t => t.Order).ToListAsync(ct);
                if (termRows.Any(t => liveOrders.Contains(t.Order)))
                {
                    throw new BusinessRuleException(
                        "Impossible de restaurer : un trimestre actif occupe déjà le même rang dans cette année.",
                        SoftDeleteLifecycle.ActiveEntityConflict);
                }

                foreach (var term in termRows) term.Restore();
                terms = termRows.Count;

                var assignmentRows = await dbContext.TeacherAssignments.IgnoreQueryFilters()
                    .Where(a => a.SchoolId == year.SchoolId && a.SchoolYearId == year.Id
                                && a.IsDeleted && a.DeletedAt >= deletedAt)
                    .ToListAsync(ct);

                var liveTeachers = (await dbContext.Teachers.Select(t => t.Id).ToListAsync(ct)).ToHashSet();
                var liveClassrooms = (await dbContext.Classrooms.Select(c => c.Id).ToListAsync(ct)).ToHashSet();
                var liveSubjects = (await dbContext.Subjects.Select(s => s.Id).ToListAsync(ct)).ToHashSet();

                foreach (var assignment in assignmentRows)
                {
                    var parentsAlive = liveTeachers.Contains(assignment.TeacherId)
                        && liveClassrooms.Contains(assignment.ClassroomId)
                        && liveSubjects.Contains(assignment.SubjectId);
                    if (!parentsAlive)
                    {
                        skipped++;
                        continue;
                    }

                    assignment.Restore();
                    assignments++;
                }
            },
            cancellationToken);

        // Le tableau de bord Directeur agrège l'année active et ses classes (voir DeleteSchoolYearCommandHandler).
        kpiCache.Invalidate(KpiCacheKeys.DirectorDashboard);
        kpiCache.Invalidate(KpiCacheKeys.FinanceDashboard);

        return new RestoreSchoolYearResult(year.Id, label, terms, assignments, skipped);
    }
}
