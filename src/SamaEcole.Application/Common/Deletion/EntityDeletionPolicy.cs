using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Common.Deletion;

/// <summary>
/// Tout ce qu'il faut savoir sur la suppression d'un type d'élément (spec 2026-10-17, §6) : comment vérifier qu'il
/// existe, sa règle financière éventuelle, la liste exhaustive de ses liens, la phrase d'en-tête du message.
/// </summary>
/// <param name="Resource">Clé stable (« Student », « Classroom »…).</param>
/// <param name="NotFoundMessage">Message 404, avec l'identifiant en argument.</param>
/// <param name="Head">« Cet élève est actuellement rattaché » — suivi de « à {liste}. Vous devez rompre… ».</param>
/// <param name="ExistsAsync">Vrai si l'élément existe dans l'école courante.</param>
/// <param name="FinancialBlockAsync">Le message de blocage financier, ou null s'il n'y a aucune donnée financière active.</param>
public sealed record EntityDeletionPolicy(
    string Resource,
    Func<Guid, string> NotFoundMessage,
    string Head,
    Func<IApplicationDbContext, Guid, CancellationToken, Task<bool>> ExistsAsync,
    Func<IApplicationDbContext, Guid, CancellationToken, Task<string?>> FinancialBlockAsync,
    IReadOnlyList<DeletionDependency> Dependencies);

/// <summary>
/// Moteur commun d'analyse et de rupture. L'analyse est en LECTURE SEULE ; la rupture archive les liens rompables et
/// refuse (409) un blocage financier ou un lien non rompable. La suppression elle-même (handlers <c>Delete…</c>) ne rompt
/// jamais rien : elle s'appuie sur l'analyse.
/// </summary>
public static class EntityDeletionAnalyzer
{
    public static async Task<DeletionImpact> AnalyzeAsync(
        IApplicationDbContext db, EntityDeletionPolicy policy, Guid id, CancellationToken cancellationToken)
    {
        if (!await policy.ExistsAsync(db, id, cancellationToken))
        {
            throw new KeyNotFoundException(policy.NotFoundMessage(id));
        }

        var financial = await policy.FinancialBlockAsync(db, id, cancellationToken);
        if (financial is not null)
        {
            return new DeletionImpact(false, true, financial, []);
        }

        var links = new List<DeletionLink>();
        foreach (var dependency in policy.Dependencies)
        {
            var count = await dependency.CountAsync(db, id, cancellationToken);
            if (count > 0)
            {
                links.Add(new DeletionLink(
                    dependency.Kind,
                    DeletionMessages.CountLabel(count, dependency.Singular, dependency.Plural),
                    count,
                    dependency.Breakable,
                    dependency.BreakAction));
            }
        }

        return links.Count == 0
            ? new DeletionImpact(true, false, null, links)
            : new DeletionImpact(false, false, DeletionMessages.DependenciesBlock(policy.Head, links), links);
    }

    /// <summary>
    /// Rompt les liens rompables (archivage logique) et renvoie l'analyse mise à jour. À appeler dans une transaction.
    /// Refuse (409) : blocage financier (<c>FINANCIAL_RECORDS_EXIST</c>) ou lien non rompable (<c>DEPENDENCIES_NOT_BREAKABLE</c>).
    /// </summary>
    public static async Task<DeletionImpact> UnlinkAsync(
        IApplicationDbContext db, EntityDeletionPolicy policy, Guid id, string actor, CancellationToken cancellationToken)
    {
        var before = await AnalyzeAsync(db, policy, id, cancellationToken);

        if (before.FinancialBlock)
        {
            throw new BusinessRuleException(before.Message!, DeletionErrorCodes.FinancialRecordsExist);
        }

        var notBreakable = before.Links.Where(l => !l.Breakable).ToList();
        if (notBreakable.Count > 0)
        {
            throw new BusinessRuleException(
                $"Impossible de rompre ces liens automatiquement : {DeletionMessages.JoinLabels(notBreakable.Select(l => l.Label))}. "
                + string.Join(" ", notBreakable.Select(l => l.BreakAction + (l.BreakAction.EndsWith('.') ? string.Empty : "."))),
                DeletionErrorCodes.DependenciesNotBreakable) { Details = notBreakable };
        }

        foreach (var dependency in policy.Dependencies.Where(d => d.Breakable))
        {
            await dependency.BreakAsync(db, id, actor, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        return await AnalyzeAsync(db, policy, id, cancellationToken);
    }

    /// <summary>
    /// Garde commune des handlers de suppression : lève le 409 adapté (texte financier imposé, ou liste précise des
    /// liens) tant que l'élément n'est pas libre. Ne modifie rien.
    /// </summary>
    public static async Task EnsureDeletableAsync(
        IApplicationDbContext db, EntityDeletionPolicy policy, Guid id, CancellationToken cancellationToken)
    {
        var impact = await AnalyzeAsync(db, policy, id, cancellationToken);

        if (impact.FinancialBlock)
        {
            throw new BusinessRuleException(impact.Message!, DeletionErrorCodes.FinancialRecordsExist);
        }

        if (!impact.CanDelete)
        {
            throw new BusinessRuleException(impact.Message!, DeletionErrorCodes.DependenciesExist) { Details = impact.Links };
        }
    }
}
