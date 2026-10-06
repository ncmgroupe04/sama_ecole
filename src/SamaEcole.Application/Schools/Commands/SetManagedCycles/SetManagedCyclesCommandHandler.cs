using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Schools.Commands.SetManagedCycles;

/// <summary>
/// Enregistre les cycles gérés. Le contrôle « ce cycle contient-il encore des classes ? » est refait ICI, côté
/// serveur : l'écran le prévient aussi, mais une modale ne protège que l'interface.
///
/// Compte les classes VIVANTES : <c>Classroom</c> est une ITenantEntity, son Global Query Filter écarte déjà les
/// classes d'une autre école et les classes supprimées logiquement. Le cycle d'une classe est
/// <c>Classroom.Cycle</c> (structuré, dérivé de son niveau), jamais son libellé libre.
/// </summary>
public class SetManagedCyclesCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ILogger<SetManagedCyclesCommandHandler> logger)
    : IRequestHandler<SetManagedCyclesCommand, SchoolSettingsDto>
{
    public async Task<SchoolSettingsDto> Handle(SetManagedCyclesCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Validé en amont : la liste est non vide, sans doublon, faite de noms connus.
        var requested = ManagedCycleSet.TryParse(request.Cycles)!;

        var settings = await dbContext.SchoolSettings.FirstOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            settings = new SchoolSettings { SchoolId = schoolId };
            dbContext.SchoolSettings.Add(settings);
        }

        var removed = ManagedCycleSet.Removed(ManagedCycleSet.FromStored(settings.ManagedCycles), requested);

        if (removed.Count > 0)
        {
            var inUse = await dbContext.Classrooms.AsNoTracking()
                .Where(c => removed.Contains(c.Cycle))
                .GroupBy(c => c.Cycle)
                .Select(g => new { Cycle = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            if (inUse.Count > 0)
            {
                var blocking = removed
                    .Select(cycle => (Cycle: cycle, Count: inUse.FirstOrDefault(x => x.Cycle == cycle)?.Count ?? 0))
                    .Where(x => x.Count > 0)
                    .ToList();

                throw new BusinessRuleException(Describe(blocking), "CYCLE_HAS_CLASSROOMS")
                {
                    Details = blocking
                        .Select(x => new { cycle = x.Cycle.ToString(), classroomCount = x.Count })
                        .ToList()
                };
            }
        }

        settings.ManagedCycles = ManagedCycleSet.Serialize(requested);

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Cycles gérés de l'établissement {SchoolId} : {ManagedCycles}.", schoolId, settings.ManagedCycles);

        return SchoolSettingsDtoMapper.From(settings);
    }

    private static string Describe(IReadOnlyList<(CycleType Cycle, int Count)> blocking)
    {
        var parts = blocking.Select(x =>
            $"Le cycle {ManagedCycleSet.FrenchLabel(x.Cycle)} compte {x.Count} classe{(x.Count > 1 ? "s" : "")}.");

        return $"{string.Join(" ", parts)} Supprimez-les ou déplacez-les avant de le désactiver.";
    }
}
