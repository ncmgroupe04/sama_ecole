using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Quran.Commands.UpdateQuranProgress;

public class UpdateQuranProgressCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<UpdateQuranProgressCommand, QuranProgressDto>
{
    public async Task<QuranProgressDto> Handle(UpdateQuranProgressCommand request, CancellationToken cancellationToken)
    {
        // Global Query Filter + policy RLS bornent déjà à l'école courante : viser une ligne d'une
        // autre école renvoie 404, jamais une modification silencieuse.
        var entry = await dbContext.QuranProgresses
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Suivi coranique {request.Id} introuvable.");

        // Cœur du verrou optimiste (AGENTS.md règle #5) : le jeton lu par le client devient la valeur
        // d'origine imposée à EF. Périmé → SaveChangesAsync refuse en 409.
        dbContext.SetOriginalConcurrencyToken(entry, request.RowVersion);

        entry.Status = request.Status;
        entry.EvaluationDate = request.EvaluationDate;
        entry.Notes = request.Notes;

        await dbContext.SaveChangesAsync(cancellationToken);

        var rowVersion = await dbContext.QuranProgresses.AsNoTracking()
            .Where(p => p.Id == entry.Id)
            .Select(p => EF.Property<uint>(p, "xmin"))
            .FirstAsync(cancellationToken);

        return new QuranProgressDto(
            entry.Id, entry.StudentId, entry.JuzNumber, entry.HizbNumber, entry.SurahNumber,
            entry.Status, entry.EvaluationDate, entry.Notes, rowVersion);
    }
}
