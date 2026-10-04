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
    public const string ArchivedEntityExists = "ARCHIVED_ENTITY_EXISTS";
    public const string ActiveEntityConflict = "ACTIVE_ENTITY_CONFLICT";

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

        var archived = await set.IgnoreQueryFilters()
            .Where(e => e.SchoolId == schoolId && e.IsDeleted)
            .AnyAsync(identity, cancellationToken);

        if (archived)
        {
            throw new BusinessRuleException(
                $"{description} existe dans les éléments supprimés : restaurez-le au lieu d'en créer un nouveau.",
                ArchivedEntityExists);
        }
    }

    /// <summary>Tombstones de l'école courante, les plus récents d'abord (corbeille).</summary>
    public static async Task<IReadOnlyList<TDto>> ListDeletedAsync<T, TDto>(
        DbSet<T> set, Guid schoolId, Expression<Func<T, TDto>> project, CancellationToken cancellationToken)
        where T : AuditableEntity, ITenantEntity
        => await set.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.SchoolId == schoolId && e.IsDeleted)
            .OrderByDescending(e => e.DeletedAt)
            .Select(project)
            .ToListAsync(cancellationToken);

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
