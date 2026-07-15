using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace SamaEcole.Persistence;

/// <summary>
/// Toute entité implémentant ITenantEntity DOIT recevoir ici un Global Query Filter sur
/// SchoolId (AGENTS.md règle #2). Ce filtre est une défense en profondeur : la policy RLS
/// PostgreSQL (voir Migrations/) reste la protection réelle et doit bloquer même si ce
/// filtre est un jour oublié sur une nouvelle entité.
/// </summary>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ITenantProvider tenantProvider)
    : DbContext(options), IApplicationDbContext
{
    public DbSet<School> Schools => Set<School>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Classroom> Classrooms => Set<Classroom>();
    public DbSet<SchoolYear> SchoolYears => Set<SchoolYear>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<FeeCategory> FeeCategories => Set<FeeCategory>();
    public DbSet<ClassFee> ClassFees => Set<ClassFee>();
    public DbSet<FeeChangeHistory> FeeChangeHistory => Set<FeeChangeHistory>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<UserStatusHistory> UserStatusHistory => Set<UserStatusHistory>();
    public DbSet<SchoolSettings> SchoolSettings => Set<SchoolSettings>();

    // Compteurs de matricules : écrits uniquement par MatriculeGenerator (INSERT ... ON CONFLICT),
    // jamais manipulés à la main par un Handler. Volontairement absent d'IApplicationDbContext.
    public DbSet<MatriculeSequence> MatriculeSequences => Set<MatriculeSequence>();

    // Refresh tokens : manipulés uniquement par AuthStore (ticket JGK-A04). Hors IApplicationDbContext,
    // aucun Handler métier n'a de raison d'y toucher.
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // Le verrou optimiste xmin du barème (ClassFee, AGENTS.md règle #5) est configuré dans
        // ClassFeeConfiguration : une propriété fantôme uint marquée IsRowVersion, que la convention
        // Npgsql mappe automatiquement sur la colonne système xmin sans générer de migration.

        // Applique automatiquement le filtre SchoolId à toute entité ITenantEntity,
        // pour ne pas dépendre de la discipline de chaque développeur/agent à chaque ajout de table.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                var method = typeof(ApplicationDbContext)
                    .GetMethod(nameof(SetTenantFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .MakeGenericMethod(entityType.ClrType);
                method.Invoke(this, [modelBuilder]);
            }
        }
    }

    private void SetTenantFilter<TEntity>(ModelBuilder modelBuilder) where TEntity : AuditableEntity, ITenantEntity
    {
        // Combine isolation tenant ET soft delete en un seul filtre : EF Core n'autorise
        // qu'un HasQueryFilter par entité, le second appel écraserait le premier.
        modelBuilder.Entity<TEntity>()
            .HasQueryFilter(e => e.SchoolId == tenantProvider.CurrentSchoolId && !e.IsDeleted);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    break;
            }
        }

        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Verrou optimiste xmin (AGENTS.md règle #5) : la ligne visée a changé depuis sa lecture,
            // l'UPDATE « WHERE xmin = <valeur lue> » n'a donc touché aucune ligne. Ce catch doit
            // précéder celui de DbUpdateException — dont il hérite — sinon il ne serait jamais atteint.
            var entry = ex.Entries.FirstOrDefault();
            var table = entry?.Metadata.GetTableName() ?? "inconnue";
            var key = entry?.Entity is AuditableEntity audited ? audited.Id.ToString() : "inconnue";

            throw new ConcurrencyConflictException(table, key);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            // Traduit ici, et pas dans les Handlers : SamaEcole.Application ne doit pas connaître Npgsql.
            // Une violation d'unicité est un conflit d'écriture concurrent -> 409, jamais un 500 ni un
            // écrasement silencieux (AGENTS.md règle #5, docs/Volume_4_API_Design.md §0.4).
            throw new ConcurrencyConflictException(pg.TableName ?? "inconnue", pg.ConstraintName ?? "contrainte d'unicité");
        }
    }

    /// <summary>
    /// Positionne le jeton de concurrence (xmin) ATTENDU par le client sur une entité déjà suivie.
    /// Le prochain SaveChangesAsync comparera cette valeur à celle en base : si la ligne a changé
    /// entre-temps, il refusera en 409 plutôt que d'écraser (AGENTS.md règle #5). Encapsulé ici pour
    /// que SamaEcole.Application n'ait pas à manipuler l'API de suivi d'EF Core ni à connaître xmin.
    /// </summary>
    public void SetOriginalConcurrencyToken<TEntity>(TEntity entity, uint expectedVersion)
        where TEntity : class
        => Entry(entity).Property("xmin").OriginalValue = expectedVersion;

    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        // Transaction déjà ouverte par l'appelant (ex. test d'intégration qui rollback) : on s'y greffe,
        // sinon la transaction imbriquée committerait un travail que l'appelant voulait pouvoir annuler.
        if (Database.CurrentTransaction is not null)
        {
            return await operation(cancellationToken);
        }

        // ExecutionStrategy : rejoue toute l'opération en cas d'erreur transitoire, transaction comprise.
        var strategy = Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async ct =>
        {
            await using var transaction = await Database.BeginTransactionAsync(ct);

            var result = await operation(ct);

            await transaction.CommitAsync(ct);
            return result;
        }, cancellationToken);
    }
}
