using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Grades;

/// <summary>
/// Barème de l'école (10 ou 20, Volume 1 §8.2), partagé par CreateGradeCommandHandler et
/// UpdateGradeCommandHandler : une note hors plage est une erreur de saisie sur le bon champ (422),
/// pas une exception brute (AGENTS.md règle #9).
/// </summary>
internal static class GradingScaleGuard
{
    public static async Task<int> ResolveScaleAsync(IApplicationDbContext dbContext, CancellationToken cancellationToken)
        => (await dbContext.SchoolSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken))
            ?.GradingScale ?? SchoolSettingsDefaults.GradingScale;

    public static void EnsureWithinScale(decimal value, int gradingScale, string propertyName)
    {
        if (value > gradingScale)
        {
            throw new ValidationException([
                new ValidationFailure(propertyName, $"La note ne peut pas dépasser le barème de l'école ({gradingScale}).")
            ]);
        }
    }
}
