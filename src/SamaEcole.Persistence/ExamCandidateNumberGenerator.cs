using SamaEcole.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Persistence;

/// <summary>
/// Numérotation séquentielle PAR SESSION D'EXAMEN (contrairement aux matricules, séquencés par école
/// et par année — voir <see cref="MatriculeGenerator"/>). Même dispositif de concurrence : un
/// UPDATE ... RETURNING pose un verrou de ligne sur la session, deux attributions simultanées dans la
/// même session sont sérialisées. Pas de gabarit ni d'année à composer : le numéro de table est un
/// simple compteur à 3 chiffres, propre à chaque session (Volume 1 §22.4).
/// </summary>
public class ExamCandidateNumberGenerator(ApplicationDbContext dbContext, TimeProvider timeProvider)
    : IExamCandidateNumberGenerator
{
    public async Task<string> GenerateNextAsync(Guid examSessionId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        // Non composable, comme l'INSERT ... RETURNING de MatriculeGenerator.NextValueAsync :
        // ToListAsync puis Single(), jamais SingleAsync qui envelopperait la requête dans un
        // SELECT ... LIMIT et ferait échouer EF Core sur du SQL non composable.
        var values = await dbContext.Database.SqlQuery<int>(
            $"""
             UPDATE exam_sessions
             SET "NextCandidateSeq" = "NextCandidateSeq" + 1, "UpdatedAt" = {now}
             WHERE "Id" = {examSessionId}
             RETURNING "NextCandidateSeq" AS "Value"
             """)
            .ToListAsync(cancellationToken);

        // Liste vide (aucune ligne mise à jour) => session introuvable. Une valeur réelle est toujours
        // >= 1 (compteur post-incrémenté), 0 ne peut donc provenir que du SingleOrDefault sur liste vide.
        var next = values.SingleOrDefault();

        if (next == 0)
        {
            throw new KeyNotFoundException($"Session d'examen {examSessionId} introuvable.");
        }

        return next.ToString("D3");
    }
}
