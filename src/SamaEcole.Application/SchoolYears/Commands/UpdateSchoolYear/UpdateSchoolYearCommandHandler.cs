using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.SchoolYears.Commands.UpdateSchoolYear;

public class UpdateSchoolYearCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    TimeProvider timeProvider,
    ILogger<UpdateSchoolYearCommandHandler> logger)
    : IRequestHandler<UpdateSchoolYearCommand, SchoolYearDto>
{
    public async Task<SchoolYearDto> Handle(UpdateSchoolYearCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        // Le Global Query Filter + la policy RLS bornent la recherche à l'école courante : viser l'année
        // d'une autre école renvoie 404, jamais une modification silencieuse chez le voisin.
        var year = await dbContext.SchoolYears
            .FirstOrDefaultAsync(y => y.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Année scolaire {request.Id} introuvable.");

        // Années passées en LECTURE SEULE (ticket JGK-C01) — la même garde que l'activation, et pour la
        // même raison : les frais et les bulletins d'un exercice clos sont déjà arrêtés. Déplacer la fin
        // d'une année terminée la rouvrirait sans que rien ne recalcule ce qui en dépend.
        if (year.IsClosedOn(today))
        {
            throw new ValidationException([
                new ValidationFailure(
                    nameof(request.Id),
                    $"L'année scolaire « {year.Label} » est terminée : les années passées sont en lecture seule.")
            ]);
        }

        // Chevauchement, en EXCLUANT l'année modifiée : sans ce `y.Id != request.Id`, toute modification
        // se heurterait à sa propre période et serait refusée d'office.
        var overlapping = await dbContext.SchoolYears
            .AsNoTracking()
            .Where(y => y.Id != request.Id && y.StartDate <= request.EndDate && request.StartDate <= y.EndDate)
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

        var periodChanged = year.StartDate != request.StartDate || year.EndDate != request.EndDate;

        year.Label = request.Label.Trim();
        year.StartDate = request.StartDate;
        year.EndDate = request.EndDate;

        if (periodChanged)
        {
            await RescheduleTermsAsync(request, cancellationToken);
        }

        // Un libellé en doublon viole l'index unique (SchoolId, Label, IsDeleted) : SaveChangesAsync le
        // traduit en ConcurrencyConflictException → 409, jamais un écrasement silencieux.
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Année scolaire {YearId} de l'école {SchoolId} modifiée : « {Label} », du {Start} au {End}.",
            year.Id, schoolId, year.Label, year.StartDate, year.EndDate);

        return new SchoolYearDto(
            year.Id, year.Label, year.StartDate, year.EndDate, year.IsActive, year.IsClosedOn(today));
    }

    /// <summary>
    /// RECALE les périodes existantes sur la nouvelle période — sans jamais en créer ni en supprimer.
    /// Les notes pointent un TermId : supprimer puis recréer les périodes les orphelinerait (et la FK
    /// Restrict le refuserait). Une période garde donc son identité et ses notes, seules ses bornes
    /// bougent — c'est ce qui permet de prolonger une année sans toucher aux notes déjà saisies.
    ///
    /// Les périodes sont prises dans l'ordre de <c>Order</c>, le rang porté par le bulletin. Leur
    /// NOMBRE est celui de l'année (ses <c>Term</c> existants), jamais celui du réglage actuel de
    /// l'école : une année créée en Semestriel reste à deux périodes même si l'école est repassée en
    /// Trimestriel. Les libellés ne bougent pas — seules les bornes se recalent.
    /// </summary>
    private async Task RescheduleTermsAsync(UpdateSchoolYearCommand request, CancellationToken cancellationToken)
    {
        var terms = await dbContext.Terms
            .Where(t => t.SchoolYearId == request.Id)
            .ToListAsync(cancellationToken);

        var ordered = terms.OrderBy(t => t.Order).ToList();
        if (ordered.Count == 0) return;

        var schedule = PeriodSchedule.SplitDates(request.StartDate, request.EndDate, ordered.Count);
        for (var index = 0; index < ordered.Count; index++)
        {
            ordered[index].StartDate = schedule[index].Start;
            ordered[index].EndDate = schedule[index].End;
        }
    }
}
