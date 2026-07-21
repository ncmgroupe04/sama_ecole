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
    /// RECALE les trimestres existants sur la nouvelle période — sans jamais en créer ni en supprimer.
    /// Les notes pointent un TermId : supprimer puis recréer les trimestres les orphelinerait (et la FK
    /// Restrict le refuserait). Un trimestre garde donc son identité et ses notes, seules ses bornes
    /// bougent — c'est ce qui permet de prolonger une année sans toucher aux notes déjà saisies.
    ///
    /// L'appariement se fait par <c>Order</c> (1, 2, 3), le rang porté par le bulletin : c'est la clé
    /// métier stable du trimestre, là où une position dans une liste dépendrait de l'ordre de lecture.
    /// </summary>
    private async Task RescheduleTermsAsync(UpdateSchoolYearCommand request, CancellationToken cancellationToken)
    {
        var terms = await dbContext.Terms
            .Where(t => t.SchoolYearId == request.Id)
            .ToListAsync(cancellationToken);

        var schedule = TermSchedule.Split(request.StartDate, request.EndDate);

        for (var index = 0; index < schedule.Count; index++)
        {
            // Découpage inattendu (année héritée d'un autre nombre de trimestres) : on recale ce qui
            // existe et on ne fabrique jamais un trimestre au passage — ce n'est pas le rôle de cette
            // commande, et un trimestre surgi ici n'aurait ni notes ni bulletin cohérents.
            var term = terms.Find(t => t.Order == index + 1);
            if (term is null) continue;

            term.StartDate = schedule[index].Start;
            term.EndDate = schedule[index].End;
        }
    }
}
