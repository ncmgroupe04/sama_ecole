using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Subjects.Commands.UpdateSubject;

public class UpdateSubjectCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<UpdateSubjectCommand, UpdateSubjectResult>
{
    public async Task<UpdateSubjectResult> Handle(UpdateSubjectCommand request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la policy RLS bornent déjà la recherche à l'école courante : viser
        // une matière d'une autre école renvoie 404, jamais une modification silencieuse.
        var subject = await dbContext.Subjects
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Matière {request.Id} introuvable.");

        // Cœur du verrou optimiste (AGENTS.md règle #5). Une violation de l'index unique
        // (SchoolId, Level, Name, IsDeleted) — un même (niveau, nom) déjà pris — est traduite au
        // même endroit par SaveChangesAsync.
        dbContext.SetOriginalConcurrencyToken(subject, request.RowVersion);

        subject.Name = request.Name.Trim();
        subject.Level = request.Level.Trim();
        subject.Coefficient = request.Coefficient;

        await dbContext.SaveChangesAsync(cancellationToken);

        var newRowVersion = await dbContext.Subjects.AsNoTracking()
            .Where(s => s.Id == subject.Id)
            .Select(s => EF.Property<uint>(s, "xmin"))
            .FirstAsync(cancellationToken);

        return new UpdateSubjectResult(subject.Id, subject.Name, subject.Level, subject.Coefficient, newRowVersion);
    }
}
