using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Subjects;

/// <summary>
/// Les invariants de la hiérarchie d'évaluation (domaine → activité), partagés par CreateSubjectCommand
/// et UpdateSubjectCommand — deux points d'entrée qui doivent poser exactement les mêmes règles, sans
/// quoi une structure interdite à la création s'obtiendrait par une simple modification.
///
/// Ils dépendent tous de l'état en base (le parent existe-t-il ? porte-t-il déjà un parent ?) : leur
/// place est donc dans les Handlers, pas dans les Validators de forme (Volume 1 §8.2). Ils sortent en
/// 422 sur le champ fautif, jamais en violation de contrainte remontée en 500 (AGENTS.md règle #9).
///
/// Le Global Query Filter + la policy RLS bornent chaque lecture à l'école courante : désigner comme
/// parent une matière d'une autre école revient à désigner une matière introuvable.
/// </summary>
internal static class SubjectHierarchyGuard
{
    /// <summary>
    /// Le nom du champ tel que le connaît le client (SubjectDto.parentSubjectId) — l'écran des matières
    /// attache l'erreur au bon sélecteur.
    /// </summary>
    private const string Field = "ParentSubjectId";

    /// <summary>
    /// Vérifie qu'un rattachement à un domaine est légal, et renvoie le NIVEAU que la matière doit porter :
    /// celui de son domaine. Une activité ne peut pas vivre à un autre niveau que le domaine qui la
    /// contient — le bulletin d'une classe lit la grille de son niveau, une activité égarée y serait
    /// simplement invisible. Le niveau demandé est donc ignoré au profit de celui du parent, plutôt que
    /// refusé : l'écran ne propose même pas le champ pour une activité.
    ///
    /// <paramref name="subjectId"/> est null à la création, l'identifiant de la matière modifiée sinon.
    /// </summary>
    public static async Task<string> ResolveLevelForParentAsync(
        IApplicationDbContext dbContext,
        Guid? parentSubjectId,
        Guid? subjectId,
        string requestedLevel,
        CancellationToken cancellationToken)
    {
        if (parentSubjectId is not { } parentId)
        {
            return requestedLevel;
        }

        if (subjectId == parentId)
        {
            throw Invalid("Une matière ne peut pas être son propre domaine.");
        }

        var parent = await dbContext.Subjects.AsNoTracking()
            .Where(s => s.Id == parentId)
            .Select(s => new { s.Level, s.ParentSubjectId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw Invalid("Le domaine indiqué n'existe pas dans votre établissement.");

        // Deux niveaux, pas trois : le bulletin n'imprime qu'UNE colonne de regroupement, dont le RowSpan
        // se calcule sur le nombre d'activités du domaine. Une profondeur libre n'aurait aucune
        // traduction visuelle et laisserait des lignes sans colonne où s'afficher.
        if (parent.ParentSubjectId is not null)
        {
            throw Invalid(
                "Ce domaine est déjà une activité rattachée à un autre domaine : la structure d'évaluation ne compte que deux niveaux.");
        }

        return parent.Level;
    }

    /// <summary>
    /// Refuse qu'une matière qui porte déjà des activités devienne elle-même l'activité d'un tiers —
    /// l'autre moitié de la règle des deux niveaux, celle que <see cref="ResolveLevelForParentAsync"/>
    /// ne peut pas voir (elle regarde le parent visé, pas la matière déplacée).
    /// </summary>
    public static async Task EnsureCanBecomeChildAsync(
        IApplicationDbContext dbContext, Guid subjectId, CancellationToken cancellationToken)
    {
        if (await dbContext.Subjects.AnyAsync(s => s.ParentSubjectId == subjectId, cancellationToken))
        {
            throw Invalid(
                "Cette matière porte déjà des activités : elle ne peut pas être rattachée à un autre domaine. Détachez-les d'abord.");
        }
    }

    private static ValidationException Invalid(string message) =>
        new([new ValidationFailure(Field, message)]);
}
