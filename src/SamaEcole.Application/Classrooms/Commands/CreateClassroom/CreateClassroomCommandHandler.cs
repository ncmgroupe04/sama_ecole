using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using MediatR;

namespace SamaEcole.Application.Classrooms.Commands.CreateClassroom;

public class CreateClassroomCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    IKpiCacheService kpiCache)
    : IRequestHandler<CreateClassroomCommand, CreateClassroomResult>
{
    public async Task<CreateClassroomResult> Handle(CreateClassroomCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var level = request.Level.Trim();

        var classroom = new Classroom
        {
            SchoolId = schoolId,
            Name = request.Name.Trim(),
            Level = level,
            // Cycle DÉRIVÉ du niveau, jamais saisi séparément (voir ClassroomCycle) : c'est son absence
            // ici qui laissait toute classe de Primaire sur le défaut College — bulletin intitulé
            // « COLLÈGE DE », notes sur /20 et moyenne pondérée, pour un CM2.
            Cycle = ClassroomCycle.CycleFor(level),
            Capacity = request.Capacity,

            // Classe passerelle / accélérée (option). Le second niveau n'est retenu que si la case est
            // cochée — voir ClassroomPromotion.NormalizeTargetLevel : jamais de niveau cible orphelin.
            IsAccelerated = request.IsAccelerated,
            TargetLevel = ClassroomPromotion.NormalizeTargetLevel(request.IsAccelerated, request.TargetLevel)
        };

        dbContext.Classrooms.Add(classroom);

        // Deux classes de même nom dans la même école violent l'index unique : SaveChangesAsync
        // traduit la violation en ConcurrencyConflictException → 409, jamais un écrasement
        // silencieux ni un 500 (AGENTS.md règle #5).
        await dbContext.SaveChangesAsync(cancellationToken);

        // Voir DeleteClassroomCommandHandler : le taux d'occupation du dashboard Directeur reste sinon
        // figé jusqu'à 7 min après l'ouverture d'une nouvelle classe.
        kpiCache.Invalidate(KpiCacheKeys.DirectorDashboard);

        return new CreateClassroomResult(
            classroom.Id, classroom.Name, classroom.Level, classroom.Capacity, classroom.Cycle,
            classroom.IsAccelerated, classroom.TargetLevel);
    }
}
