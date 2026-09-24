using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.SchoolYears.Commands.CreateSchoolYear;

public class CreateSchoolYearCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    TimeProvider timeProvider)
    : IRequestHandler<CreateSchoolYearCommand, SchoolYearDto>
{
    public async Task<SchoolYearDto> Handle(CreateSchoolYearCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Deux années qui se chevauchent rendent la question « en quelle année sommes-nous ? » sans
        // réponse : une inscription du 15 octobre pourrait relever de l'une comme de l'autre. Le
        // Global Query Filter restreint déjà la recherche à l'école courante — un chevauchement avec
        // l'année d'une autre école n'existe pas.
        //
        // Deux périodes se chevauchent si chacune commence avant que l'autre ne finisse (bornes
        // incluses : partager ne serait-ce qu'un jour suffit à créer l'ambiguïté).
        var overlapping = await dbContext.SchoolYears
            .AsNoTracking()
            .Where(y => y.StartDate <= request.EndDate && request.StartDate <= y.EndDate)
            .Select(y => y.Label)
            .FirstOrDefaultAsync(cancellationToken);

        if (overlapping is not null)
        {
            throw new ValidationException([
                new ValidationFailure(
                    nameof(request.StartDate),
                    $"Cette période chevauche l'année scolaire « {overlapping} » déjà enregistrée.")
            ]);
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var hasActiveYear = await dbContext.SchoolYears.AnyAsync(y => y.IsActive, cancellationToken);

        var schoolYear = new SchoolYear
        {
            SchoolId = schoolId,
            Label = request.Label.Trim(),
            StartDate = request.StartDate,
            EndDate = request.EndDate,

            // Première année de l'école : elle devient active, sinon l'établissement n'aurait aucun
            // exercice de travail et rien ne serait inscriptible. Toute année créée ENSUITE reste
            // inactive : le basculement est une décision explicite (voir CreateSchoolYearCommand).
            //
            // Une année déjà terminée — un historique que l'école rattrape après coup — n'est jamais
            // activée : ce serait ouvrir les inscriptions sur un exercice clos.
            IsActive = !hasActiveYear && !IsClosed(request, today)
        };

        dbContext.SchoolYears.Add(schoolYear);

        // Périodes d'évaluation générées selon SchoolSettings.EvaluationPeriodType (trimestres par
        // défaut, ticket JGK-G01 ; semestres ou périodes personnalisées au choix du Directeur). Une
        // école sans ligne de réglages (antérieure à JGK-B02) garde les défauts. Les dates découpent
        // la période de l'année en tranches consécutives, sans trou ni chevauchement — la dernière
        // absorbe le reste de la division entière.
        var settings = await dbContext.SchoolSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var periodType = settings?.EvaluationPeriodType ?? SchoolSettingsDefaults.EvaluationPeriodType;
        var customCount = settings?.CustomPeriodCount ?? SchoolSettingsDefaults.CustomPeriodCount;

        foreach (var term in BuildTerms(schoolYear.Id, schoolId, request.StartDate, request.EndDate, periodType, customCount))
        {
            dbContext.Terms.Add(term);
        }

        // Un libellé en doublon viole l'index unique, et deux créations simultanées d'une première
        // année violeraient l'index unique partiel « une seule active » : SaveChangesAsync traduit
        // l'un comme l'autre en ConcurrencyConflictException → 409, jamais un écrasement silencieux
        // (AGENTS.md règle #5).
        await dbContext.SaveChangesAsync(cancellationToken);

        return new SchoolYearDto(
            schoolYear.Id,
            schoolYear.Label,
            schoolYear.StartDate,
            schoolYear.EndDate,
            schoolYear.IsActive,
            IsClosed(request, today));
    }

    private static bool IsClosed(CreateSchoolYearCommand request, DateOnly today) => request.EndDate < today;

    /// <summary>
    /// Le découpage lui-même vit dans PeriodSchedule, partagé avec la modification des dates : une année
    /// modifiée doit retomber exactement sur le même découpage qu'une année créée avec ces dates-là.
    /// </summary>
    private static IEnumerable<Term> BuildTerms(
        Guid schoolYearId, Guid schoolId, DateOnly start, DateOnly end,
        EvaluationPeriodType periodType, int customCount) =>
        PeriodSchedule.Split(start, end, periodType, customCount).Select((t, index) => new Term
        {
            SchoolId = schoolId,
            SchoolYearId = schoolYearId,
            Label = t.Label,
            Order = index + 1,
            StartDate = t.Start,
            EndDate = t.End
        });
}
