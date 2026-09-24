using Microsoft.EntityFrameworkCore;
using FluentValidation.Results;
using SamaEcole.Application.Classrooms;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Coefficients;

/// <summary>
/// Règles PARTAGÉES par les commandes et requêtes de surcharge de coefficient (Évolution N°4) — une seule
/// définition, sans quoi l'écriture et la grille finiraient par accepter des choses différentes.
/// </summary>
public static class CoefficientRules
{
    /// <summary>Même plafond que <c>CreateSubjectCommandValidator</c> : un coefficient au-delà de 20 semble irréaliste.</summary>
    public const decimal MaxCoefficient = 20m;

    public const string PrimaryMessage =
        "Au primaire et en maternelle, le coefficient est toujours 1 : il ne se surcharge pas.";

    public const string SourceSubject = "Subject";
    public const string SourceSeries = "Series";
    public const string SourceClassroom = "Classroom";

    /// <summary>
    /// L'année ACTIVE, résolue serveur — jamais fournie par le client (règle #10, même convention que
    /// Enrollment et l'appel). Sans année active, rien à quoi rattacher une surcharge.
    /// </summary>
    public static async Task<Guid> ActiveSchoolYearIdAsync(IApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        var yearId = await dbContext.SchoolYears.AsNoTracking()
            .Where(y => y.IsActive)
            .Select(y => (Guid?)y.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return yearId
            ?? throw new ValidationException([
                new ValidationFailure("SchoolYear",
                    "Aucune année scolaire active. Activez une année scolaire avant de régler les coefficients.")
            ]);
    }

    /// <summary>
    /// Une matière de primaire/maternelle ne se surcharge pas (le coefficient y est neutralisé à 1 par le
    /// calcul). Le niveau d'une matière est un texte libre : on le juge comme le fait le calcul, par
    /// <see cref="ClassroomCycle"/> — un niveau non reconnu vaut Collège, donc surchargeable.
    /// </summary>
    public static bool IsPrimaryLevel(string level) => ClassroomCycle.CycleFor(level).UsesSimplifiedGrading();

    /// <summary>Cycle d'une classe, ou null si elle n'existe pas dans l'école courante (RLS + filtre).</summary>
    public static Task<CycleType?> ClassroomCycleAsync(
        IApplicationDbContext dbContext, Guid classroomId, CancellationToken cancellationToken)
        => dbContext.Classrooms.AsNoTracking()
            .Where(c => c.Id == classroomId)
            .Select(c => (CycleType?)c.Cycle)
            .FirstOrDefaultAsync(cancellationToken);
}
