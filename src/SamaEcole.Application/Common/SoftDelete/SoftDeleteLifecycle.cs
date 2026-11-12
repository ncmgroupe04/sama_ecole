using System.Linq.Expressions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Common.SoftDelete;

/// <summary>
/// Mécanique commune du cycle de vie « supprimé puis restauré » (conception soft delete 2026-10-01 §3.2).
/// Les commandes restent TYPÉES par entité (aucune commande polymorphe recevant un nom de type) ; ce helper
/// ne porte que la recherche d'identité et la restauration, pour qu'elles se comportent partout pareil.
///
/// Tout accès à un tombstone passe par <c>IgnoreQueryFilters()</c> — qui lève AUSSI le filtre tenant — puis
/// réimpose <c>SchoolId</c> du contexte courant. La RLS PostgreSQL reste la seconde barrière.
/// </summary>
public static class SoftDeleteLifecycle
{
    /// <summary>
    /// Durée pendant laquelle un élément supprimé reste dans la corbeille et peut être restauré. Passé ce délai
    /// il n'est PLUS listé ni restaurable, mais la ligne reste en base (règle #6 : aucune suppression physique,
    /// l'historique des paiements/échéanciers qui le référence reste intact).
    /// Règle évaluée À LA LECTURE à partir de <c>DeletedAt</c> : aucune tâche planifiée à faire tourner.
    /// </summary>
    public static readonly TimeSpan TrashRetention = TimeSpan.FromDays(7);

    /// <summary>Un élément supprimé AVANT cette date est sorti de la corbeille.</summary>
    public static DateTimeOffset TrashCutoff(DateTimeOffset? now = null) => (now ?? DateTimeOffset.UtcNow) - TrashRetention;

    public const string ArchivedEntityExists = "ARCHIVED_ENTITY_EXISTS";
    public const string ActiveEntityConflict = "ACTIVE_ENTITY_CONFLICT";

    /// <summary>409 : la ressource a encore des éléments vivants dépendants (ex. un pavillon avec des chambres, un lit occupé).</summary>
    public const string ResourceInUse = "RESOURCE_IN_USE";

    /// <summary>
    /// À appeler avant d'insérer une identité réutilisable. Si une ligne ACTIVE porte déjà l'identité, ne fait
    /// rien : l'index unique partiel arbitre (conflit de doublon existant). Si seule une ligne SUPPRIMÉE la porte,
    /// refuse en 409 <see cref="ArchivedEntityExists"/> au lieu de réactiver ou de dupliquer en silence.
    /// </summary>
    public static async Task EnsureNoArchivedIdentityAsync<T>(
        DbSet<T> set, Guid schoolId, Expression<Func<T, bool>> identity, string description,
        CancellationToken cancellationToken)
        where T : AuditableEntity, ITenantEntity
    {
        if (await set.AnyAsync(identity, cancellationToken))
        {
            return;
        }

        // Seuls les éléments ENCORE dans la corbeille bloquent : un élément expiré n'est plus restaurable,
        // donc il ne doit pas empêcher de recréer la même identité (les index uniques ignorent les supprimés).
        var cutoff = TrashCutoff();
        var archived = await set.IgnoreQueryFilters()
            .Where(e => e.SchoolId == schoolId && e.IsDeleted && (e.DeletedAt == null || e.DeletedAt >= cutoff))
            .AnyAsync(identity, cancellationToken);

        if (archived)
        {
            throw new BusinessRuleException(
                $"{description} existe dans les éléments supprimés : restaurez-le au lieu d'en créer un nouveau.",
                ArchivedEntityExists);
        }
    }

    /// <summary>Tombstones de l'école courante supprimés depuis moins de <see cref="TrashRetention"/>, les plus récents d'abord (corbeille).</summary>
    public static async Task<IReadOnlyList<TDto>> ListDeletedAsync<T, TDto>(
        DbSet<T> set, Guid schoolId, Expression<Func<T, TDto>> project, CancellationToken cancellationToken)
        where T : AuditableEntity, ITenantEntity
        => await ListDeletedQuery(set, schoolId)
            .OrderByDescending(e => e.DeletedAt)
            .Select(project)
            .ToListAsync(cancellationToken);

    private static IQueryable<T> ListDeletedQuery<T>(DbSet<T> set, Guid schoolId)
        where T : AuditableEntity, ITenantEntity
    {
        var cutoff = TrashCutoff();
        return set.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.SchoolId == schoolId && e.IsDeleted && (e.DeletedAt == null || e.DeletedAt >= cutoff));
    }

    /// <summary>
    /// Restaure UN tombstone précis du tenant courant : 404 s'il n'existe pas ou appartient à un autre
    /// établissement (sans révéler son existence), 409 <see cref="ActiveEntityConflict"/> si une ligne active
    /// occupe déjà la même identité. <paramref name="beforeRestore"/> permet à l'entité de contrôler ses
    /// dépendances (ex. parent supprimé). Aucune fusion, aucune écriture partielle.
    /// </summary>
    public static async Task<T> RestoreAsync<T>(
        IApplicationDbContext dbContext, DbSet<T> set, Guid schoolId, Guid id, string entityLabel,
        Func<T, Expression<Func<T, bool>>> identityOf,
        Func<T, CancellationToken, Task>? beforeRestore,
        CancellationToken cancellationToken)
        where T : AuditableEntity, ITenantEntity
    {
        var entity = await set.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == id && e.SchoolId == schoolId && e.IsDeleted, cancellationToken)
            ?? throw new KeyNotFoundException($"{entityLabel} supprimé(e) {id} introuvable.");

        if (entity.DeletedAt is { } deletedAt && deletedAt < TrashCutoff())
        {
            // 404 : hors corbeille, il n'existe plus pour l'utilisateur (la ligne reste en base pour l'historique).
            throw new KeyNotFoundException(
                $"{entityLabel} supprimé(e) {id} n'est plus restaurable : il a quitté la corbeille après {TrashRetention.Days} jours.");
        }

        if (await set.AnyAsync(identityOf(entity), cancellationToken))
        {
            throw new BusinessRuleException(
                $"Impossible de restaurer : {entityLabel} actif(ve) occupe déjà la même identité.",
                ActiveEntityConflict);
        }

        if (beforeRestore is not null)
        {
            await beforeRestore(entity, cancellationToken);
        }

        entity.Restore();
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }
}
