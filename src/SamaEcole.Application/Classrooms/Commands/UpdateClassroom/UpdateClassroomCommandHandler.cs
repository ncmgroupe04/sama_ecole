using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Classrooms.Commands.UpdateClassroom;

public class UpdateClassroomCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<UpdateClassroomCommand, ClassroomResult>
{
    public async Task<ClassroomResult> Handle(UpdateClassroomCommand request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la policy RLS bornent déjà la recherche à l'école courante : viser
        // une classe d'une autre école renvoie 404, jamais une modification silencieuse.
        var classroom = await dbContext.Classrooms
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Classe {request.Id} introuvable.");

        // Cœur du verrou optimiste (AGENTS.md règle #5) : le jeton lu par le client devient la valeur
        // d'origine imposée à EF. Une classe modifiée en base depuis sa lecture fait échouer
        // SaveChangesAsync en 409, jamais un écrasement silencieux. Une violation de l'index unique
        // (SchoolId, Name, IsDeleted) — un autre libellé déjà pris — est traduite au même endroit.
        dbContext.SetOriginalConcurrencyToken(classroom, request.RowVersion);

        classroom.Name = request.Name.Trim();
        classroom.Level = request.Level.Trim();
        classroom.Capacity = request.Capacity;

        // Le cycle SUIT le niveau (ClassroomCycle) : corriger une classe passée par erreur en « Collège »
        // vers « Primaire » doit rebasculer son bulletin, son barème et sa moyenne — sans quoi la
        // correction resterait cosmétique et le cycle figé sur sa valeur d'origine.
        classroom.Cycle = ClassroomCycle.CycleFor(classroom.Level);

        // Classe passerelle / accélérée : décocher la case efface le second niveau
        // (ClassroomPromotion.NormalizeTargetLevel), jamais un niveau cible orphelin laissé en base.
        classroom.IsAccelerated = request.IsAccelerated;
        classroom.TargetLevel = ClassroomPromotion.NormalizeTargetLevel(request.IsAccelerated, request.TargetLevel);

        await dbContext.SaveChangesAsync(cancellationToken);

        var newRowVersion = await dbContext.Classrooms.AsNoTracking()
            .Where(c => c.Id == classroom.Id)
            .Select(c => EF.Property<uint>(c, "xmin"))
            .FirstAsync(cancellationToken);

        return new ClassroomResult(
            classroom.Id, classroom.Name, classroom.Level, classroom.Capacity, classroom.Cycle, newRowVersion,
            classroom.IsAccelerated, classroom.TargetLevel);
    }
}
