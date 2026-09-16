using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.SchoolYears.Commands.DeleteSchoolYear;

/// <summary>
/// Retire une année scolaire de l'établissement courant. Voir <see cref="DeleteSchoolYearCommand"/>
/// pour les deux régimes (mode test : effacement ; mode réel : archivage d'une année vide).
/// </summary>
public class DeleteSchoolYearCommandHandler(
    IApplicationDbContext dbContext,
    ISchoolYearPurgeService purgeService,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    IKpiCacheService kpiCache,
    ILogger<DeleteSchoolYearCommandHandler> logger)
    : IRequestHandler<DeleteSchoolYearCommand, DeleteSchoolYearResult>
{
    public async Task<DeleteSchoolYearResult> Handle(
        DeleteSchoolYearCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Double garde du rôle : l'endpoint est déjà [Authorize(Roles = Directeur)], mais une
        // suppression d'exercice ne doit pas dépendre d'un seul attribut qu'un refactor déplacerait
        // (même précaution que ResetSchoolDataCommandHandler).
        if (currentUser.Role != Role.Directeur)
        {
            throw new UnauthorizedAccessException("Seul le Directeur peut supprimer une année scolaire.");
        }

        // Global Query Filter + policy RLS bornent la recherche à l'école courante : viser l'année
        // d'une autre école renvoie 404, jamais une suppression silencieuse chez le voisin.
        var year = await dbContext.SchoolYears
            .AsNoTracking()
            .FirstOrDefaultAsync(y => y.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Année scolaire {request.Id} introuvable.");

        EnsureConfirmed(request.Confirmation, year.Label);

        var wentLiveAt = await dbContext.Schools
            .AsNoTracking()
            .Where(s => s.Id == schoolId)
            .Select(s => s.WentLiveAt)
            .FirstOrDefaultAsync(cancellationToken);

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        logger.LogWarning(
            "Suppression de l'année scolaire {YearId} ({YearLabel}) demandée par {ActorId} sur l'établissement {SchoolId} — mode {Mode}.",
            year.Id, year.Label, actorId, schoolId, wentLiveAt is null ? "test" : "réel");

        // UNE transaction pour les deux temps de l'opération : le retrait de l'année, puis la
        // rebascule de l'exercice actif. Entre les deux, l'établissement n'a aucune année active —
        // état qu'aucun autre client ne doit pouvoir lire.
        var result = await dbContext.ExecuteInTransactionAsync(
            async ct =>
            {
                var summary = wentLiveAt is null
                    ? await purgeService.PurgeAsync(schoolId, year.Id, ct)
                    : await ArchiveEmptyYearAsync(year, actorId, ct);

                var fallback = year.IsActive ? await ActivateFallbackYearAsync(year, today, ct) : null;

                return new DeleteSchoolYearResult(
                    year.Label,
                    WasActive: year.IsActive,
                    WasPurged: wentLiveAt is null,
                    NewActiveYear: fallback,
                    RequiresActiveYearSelection: year.IsActive && fallback is null,
                    Summary: summary);
            },
            cancellationToken);

        // Le tableau de bord Directeur agrège élèves et classes de l'année active : sans invalidation,
        // il afficherait jusqu'à 7 minutes (KpiCacheSettings.TtlMinutes) les chiffres d'un exercice qui
        // n'existe plus.
        kpiCache.Invalidate(KpiCacheKeys.DirectorDashboard);
        kpiCache.Invalidate(KpiCacheKeys.FinanceDashboard);

        return result;
    }

    /// <summary>
    /// MODE RÉEL. L'année n'est retirée que si elle ne porte rien : sinon ses inscriptions et ses
    /// paiements — contrepartie de reçus déjà remis — disparaîtraient avec elle (AGENTS.md règle #6).
    /// Retour <c>null</c> : il n'y a jamais de lignes effacées à rapporter sur ce chemin.
    /// </summary>
    private async Task<SchoolDataResetSummary?> ArchiveEmptyYearAsync(
        SchoolYear year, Guid actorId, CancellationToken cancellationToken)
    {
        await EnsureYearCarriesNoDataAsync(year, cancellationToken);

        var tracked = await dbContext.SchoolYears.FirstAsync(y => y.Id == year.Id, cancellationToken);

        // Désactivée AVANT d'être archivée : l'index unique partiel « une seule année active » ne
        // compte que les lignes non supprimées, mais laisser IsActive à true sur une ligne archivée
        // ferait mentir toute lecture qui ignorerait le filtre.
        tracked.IsActive = false;
        tracked.SoftDelete(actorId.ToString());

        // Les trimestres et les affectations d'enseignants sont la CONFIGURATION de cette année, pas
        // des données saisies : ils la suivent dans l'archivage, sinon ils resteraient visibles en
        // rattachement d'un exercice qui n'existe plus.
        var terms = await dbContext.Terms
            .Where(t => t.SchoolYearId == year.Id)
            .ToListAsync(cancellationToken);

        var assignments = await dbContext.TeacherAssignments
            .Where(a => a.SchoolYearId == year.Id)
            .ToListAsync(cancellationToken);

        foreach (var term in terms)
        {
            term.SoftDelete(actorId.ToString());
        }

        foreach (var assignment in assignments)
        {
            assignment.SoftDelete(actorId.ToString());
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return null;
    }

    /// <summary>
    /// « Vide » se juge sur les données MÉTIER de l'exercice, y compris celles déjà en suppression
    /// logique : une inscription annulée reste la contrepartie d'un reçu émis. Les trimestres et les
    /// affectations d'enseignants n'entrent PAS dans ce compte — ils sont créés avec l'année (les
    /// trimestres le sont automatiquement) et n'ont jamais rien enregistré à eux seuls.
    /// </summary>
    private async Task EnsureYearCarriesNoDataAsync(SchoolYear year, CancellationToken cancellationToken)
    {
        var termIds = await dbContext.Terms
            .IgnoreQueryFilters()
            .Where(t => t.SchoolId == year.SchoolId && t.SchoolYearId == year.Id)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        // Le compte inclut les lignes en suppression logique : IgnoreQueryFilters retire AUSSI le
        // filtre de tenant, d'où le SchoolId réaffirmé à chaque fois (la policy RLS reste, elle, en
        // vigueur — c'est la seconde barrière de la règle #2).
        Task<int> CountAsync<T>(IQueryable<T> source, System.Linq.Expressions.Expression<Func<T, bool>> predicate)
            where T : class, ITenantEntity =>
            source.IgnoreQueryFilters()
                .Where(e => e.SchoolId == year.SchoolId)
                .Where(predicate)
                .CountAsync(cancellationToken);

        var counts = new (string Noun, string Plural, int Count)[]
        {
            ("inscription", "inscriptions", await CountAsync(dbContext.Enrollments, e => e.SchoolYearId == year.Id)),
            ("note", "notes", await CountAsync(dbContext.Grades, g => termIds.Contains(g.TermId))),
            ("appréciation de bulletin", "appréciations de bulletin",
                await CountAsync(dbContext.ReportCardRemarks, r => termIds.Contains(r.TermId))),
            ("fiche d'appel", "fiches d'appel", await CountAsync(dbContext.AttendanceSheets, s => s.SchoolYearId == year.Id)),
            ("session d'examen", "sessions d'examens", await CountAsync(dbContext.ExamSessions, s => s.SchoolYearId == year.Id)),
            ("certificat de mutation", "certificats de mutation",
                await CountAsync(dbContext.StudentMutationCertificates, c => c.SchoolYearId == year.Id))
        };

        var carried = counts.Where(c => c.Count > 0).ToList();

        if (carried.Count == 0)
        {
            return;
        }

        var detail = string.Join(", ", carried.Select(c => $"{c.Count} {(c.Count > 1 ? c.Plural : c.Noun)}"));

        throw new BusinessRuleException(
            $"L'année scolaire « {year.Label} » ne peut pas être supprimée : elle porte {detail}. "
            + "Ces données font partie de la comptabilité de l'établissement et ne peuvent plus être effacées. "
            + "Exportez l'année (bouton d'export) pour l'archiver hors de la plateforme ; elle reste consultable en lecture seule.",
            "SCHOOL_YEAR_HAS_DATA");
    }

    /// <summary>
    /// L'établissement travaillait sur l'année supprimée : il doit en retrouver une, sinon plus aucune
    /// inscription ni aucune note ne peut être saisie.
    ///
    /// On choisit l'année PRÉCÉDENTE — celle qui la jouxte par la gauche —, et à défaut la suivante.
    /// Dans les deux cas parmi les seules années NON TERMINÉES : réactiver un exercice clos imputerait
    /// les écritures du jour sur des frais et des bulletins déjà arrêtés, ce que
    /// <c>ActivateSchoolYearCommandHandler</c> refuse par ailleurs. Quand il ne reste que des années
    /// terminées, on n'en active aucune : l'écran demande alors au Directeur d'en activer ou d'en créer
    /// une (RequiresActiveYearSelection).
    /// </summary>
    private async Task<SchoolYearDto?> ActivateFallbackYearAsync(
        SchoolYear deleted, DateOnly today, CancellationToken cancellationToken)
    {
        var candidates = await dbContext.SchoolYears
            .Where(y => y.Id != deleted.Id && y.EndDate >= today)
            .ToListAsync(cancellationToken);

        var fallback = candidates
            .Where(y => y.StartDate < deleted.StartDate)
            .OrderByDescending(y => y.StartDate)
            .FirstOrDefault()
            ?? candidates
                .Where(y => y.StartDate >= deleted.StartDate)
                .OrderBy(y => y.StartDate)
                .FirstOrDefault();

        if (fallback is null)
        {
            logger.LogWarning(
                "L'établissement {SchoolId} n'a plus d'année scolaire active après la suppression de {YearLabel} : aucune année ouverte ne restait.",
                deleted.SchoolId, deleted.Label);

            return null;
        }

        fallback.IsActive = true;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Année active de l'école {SchoolId} : {Deleted} (supprimée) -> {New}.",
            deleted.SchoolId, deleted.Label, fallback.Label);

        return new SchoolYearDto(
            fallback.Id, fallback.Label, fallback.StartDate, fallback.EndDate,
            fallback.IsActive, fallback.IsClosedOn(today));
    }

    private static void EnsureConfirmed(string confirmation, string yearLabel)
    {
        if (SchoolYearDeletionConfirmation.Matches(confirmation, yearLabel))
        {
            return;
        }

        throw new ValidationException(
        [
            new ValidationFailure(
                "confirmation",
                $"Saisissez exactement « {yearLabel} » pour confirmer la suppression de cette année scolaire.")
        ]);
    }
}
