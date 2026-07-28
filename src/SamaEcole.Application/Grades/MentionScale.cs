using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Grades;

/// <summary>
/// Résout les mentions ACTIVES d'une école (ticket JGK-G02) : celles personnalisées par le Directeur
/// si au moins une existe, sinon les valeurs par défaut du barème (même principe de secours que
/// SchoolSettingsDefaults — une école neuve a toujours des mentions, même si personne n'y a touché).
/// Partagé par GetGradeSummaryQueryHandler (choix de la mention) et GetMentionsQueryHandler (affichage).
///
/// Les seuils sortent TOUJOURS sur <see cref="MentionScales.Reference"/> (/20), jamais sur
/// SchoolSettings.GradingScale : voir <see cref="MentionScales"/>. Un bulletin dont le barème diffère
/// (Primaire /10) les transpose via <see cref="MentionScales.RescaleTo"/>.
/// </summary>
internal static class MentionScale
{
    public static async Task<IReadOnlyList<(string Label, decimal MinAverage)>> ResolveAsync(
        IApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        var stored = await dbContext.Mentions
            .AsNoTracking()
            .OrderByDescending(m => m.MinAverage)
            .Select(m => new { m.Label, m.MinAverage })
            .ToListAsync(cancellationToken);

        if (stored.Count > 0)
        {
            return stored.Select(m => (m.Label, m.MinAverage)).ToArray();
        }

        return MentionDefaults.ForScale(MentionScales.Reference);
    }
}
