using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using MediatR;

namespace SamaEcole.Application.Classrooms.Commands.CreateClassroom;

public class CreateClassroomCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider)
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
            Capacity = request.Capacity
        };

        dbContext.Classrooms.Add(classroom);

        // Deux classes de même nom dans la même école violent l'index unique : SaveChangesAsync
        // traduit la violation en ConcurrencyConflictException → 409, jamais un écrasement
        // silencieux ni un 500 (AGENTS.md règle #5).
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreateClassroomResult(classroom.Id, classroom.Name, classroom.Level, classroom.Capacity, classroom.Cycle);
    }
}
