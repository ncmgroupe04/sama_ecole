using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.Persistence.Errors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace SamaEcole.Persistence;

/// <summary>
/// Toute entité implémentant ITenantEntity DOIT recevoir ici un Global Query Filter sur
/// SchoolId (AGENTS.md règle #2). Ce filtre est une défense en profondeur : la policy RLS
/// PostgreSQL (voir Migrations/) reste la protection réelle et doit bloquer même si ce
/// filtre est un jour oublié sur une nouvelle entité.
/// </summary>
public class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    ITenantProvider tenantProvider,
    ILogger<ApplicationDbContext> logger)
    : DbContext(options), IApplicationDbContext
{
    public DbSet<School> Schools => Set<School>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Classroom> Classrooms => Set<Classroom>();
    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<SchoolYear> SchoolYears => Set<SchoolYear>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<FeeCategory> FeeCategories => Set<FeeCategory>();
    public DbSet<ClassFee> ClassFees => Set<ClassFee>();
    public DbSet<FeeChangeHistory> FeeChangeHistory => Set<FeeChangeHistory>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<EnrollmentFeeLine> EnrollmentFeeLines => Set<EnrollmentFeeLine>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<CashierSession> CashierSessions => Set<CashierSession>();
    public DbSet<PaymentBreakdown> PaymentBreakdowns => Set<PaymentBreakdown>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<PromoCode> PromoCodes => Set<PromoCode>();
    public DbSet<SmsMessage> SmsMessages => Set<SmsMessage>();
    public DbSet<UserSchool> UserSchools => Set<UserSchool>();
    public DbSet<ScheduleSlot> ScheduleSlots => Set<ScheduleSlot>();
    public DbSet<Disbursement> Disbursements => Set<Disbursement>();
    public DbSet<SchoolRegistrationRequest> SchoolRegistrationRequests => Set<SchoolRegistrationRequest>();
    public DbSet<SubscriptionPayment> SubscriptionPayments => Set<SubscriptionPayment>();
    public DbSet<UserStatusHistory> UserStatusHistory => Set<UserStatusHistory>();
    public DbSet<SchoolSettings> SchoolSettings => Set<SchoolSettings>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Term> Terms => Set<Term>();
    public DbSet<Grade> Grades => Set<Grade>();
    public DbSet<Mention> Mentions => Set<Mention>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<TeacherSubject> TeacherSubjects => Set<TeacherSubject>();
    public DbSet<TeacherAssignment> TeacherAssignments => Set<TeacherAssignment>();
    public DbSet<AttendanceSheet> AttendanceSheets => Set<AttendanceSheet>();
    public DbSet<ClassJournalEntry> ClassJournalEntries => Set<ClassJournalEntry>();
    public DbSet<StudentAttendance> StudentAttendances => Set<StudentAttendance>();
    public DbSet<ReportCardRemark> ReportCardRemarks => Set<ReportCardRemark>();
    public DbSet<DisciplineRecord> DisciplineRecords => Set<DisciplineRecord>();
    public DbSet<AbsenceJustification> AbsenceJustifications => Set<AbsenceJustification>();
    public DbSet<LateArrival> LateArrivals => Set<LateArrival>();
    public DbSet<TeacherAttendance> TeacherAttendances => Set<TeacherAttendance>();
    public DbSet<EmployeeContract> EmployeeContracts => Set<EmployeeContract>();
    public DbSet<EmployeeContractHistory> EmployeeContractHistories => Set<EmployeeContractHistory>();
    public DbSet<FichePaie> FichePaies => Set<FichePaie>();
    public DbSet<TaxeDeclaration> TaxeDeclarations => Set<TaxeDeclaration>();
    public DbSet<EarlyDeparture> EarlyDepartures => Set<EarlyDeparture>();
    public DbSet<ParentSummons> ParentSummons => Set<ParentSummons>();
    public DbSet<FinancialCommitment> FinancialCommitments => Set<FinancialCommitment>();
    public DbSet<TeacherHourRecord> TeacherHourRecords => Set<TeacherHourRecord>();
    public DbSet<FeeInstallmentPlan> FeeInstallmentPlans => Set<FeeInstallmentPlan>();
    public DbSet<FeeInstallment> FeeInstallments => Set<FeeInstallment>();
    public DbSet<DebtorReminderBatch> DebtorReminderBatches => Set<DebtorReminderBatch>();
    public DbSet<DebtorReminderBatchItem> DebtorReminderBatchItems => Set<DebtorReminderBatchItem>();

    // Module Inventaire (patrimoine, stock, prêts de matériel).
    public DbSet<InventoryCategory> InventoryCategories => Set<InventoryCategory>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<ItemAssignment> ItemAssignments => Set<ItemAssignment>();

    // Module Examens officiels (CFEE/BFEM/BAC).
    public DbSet<ExamSession> ExamSessions => Set<ExamSession>();
    public DbSet<ExamDossier> ExamDossiers => Set<ExamDossier>();
    public DbSet<ExamResult> ExamResults => Set<ExamResult>();
    public DbSet<StudentMutationCertificate> StudentMutationCertificates => Set<StudentMutationCertificate>();

    // Console Super Admin — entités SANS CLÉ, jamais gérées par les migrations (voir OnModelCreating) :
    // la première est adossée à une vue réelle, la seconde n'existe qu'à travers FromSqlRaw.
    public DbSet<PlatformDashboardStats> PlatformDashboardStats => Set<PlatformDashboardStats>();
    public DbSet<GlobalAuditLogEntry> GlobalAuditLogEntries => Set<GlobalAuditLogEntry>();
    public DbSet<MutationCertificateVerification> MutationCertificateVerifications => Set<MutationCertificateVerification>();
    public DbSet<PlatformSubscriptionRow> PlatformSubscriptions => Set<PlatformSubscriptionRow>();

    /// <summary>Annuaire public (B2C) — vue en lecture seule, voir <see cref="PublicSchoolListing"/>.</summary>
    public DbSet<PublicSchoolListing> PublicSchoolDirectory => Set<PublicSchoolListing>();

    // Compteurs de matricules : écrits uniquement par MatriculeGenerator (INSERT ... ON CONFLICT),
    // jamais manipulés à la main par un Handler. Volontairement absent d'IApplicationDbContext.
    public DbSet<MatriculeSequence> MatriculeSequences => Set<MatriculeSequence>();

    // Refresh tokens : manipulés uniquement par AuthStore (ticket JGK-A04). Hors IApplicationDbContext,
    // aucun Handler métier n'a de raison d'y toucher.
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>
    /// Jetons de réinitialisation self-service. Comme <see cref="RefreshTokens"/>, hors du contrat
    /// IApplicationDbContext : ce chemin s'exécute sans tenant et n'est atteint que par AuthStore.
    /// </summary>
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // `citext` : type e-mail insensible à la casse pour `users.Email` (voir UserConfiguration).
        // Extension « trusted » depuis PostgreSQL 13 — le rôle propriétaire des migrations peut
        // l'installer sans être superutilisateur. La migration émet CREATE EXTENSION IF NOT EXISTS.
        modelBuilder.HasPostgresExtension("citext");

        // Console Super Admin (contournement RLS auditée, AGENTS.md règle #2) : deux entités SANS CLÉ.
        // La première lit la vue `v_platform_dashboard_stats` (créée par migration, OWNER sama_ecole,
        // security_invoker = false) ; la seconde n'a AUCUNE table/vue propre — elle n'existe que comme
        // forme de résultat pour FromSqlRaw(get_global_audit_logs(...)), ToView(null) l'exclut donc des
        // migrations tout en la gardant interrogeable.
        modelBuilder.Entity<PlatformDashboardStats>(e =>
        {
            e.HasNoKey();
            e.ToView("v_platform_dashboard_stats");
        });

        modelBuilder.Entity<GlobalAuditLogEntry>(e =>
        {
            e.HasNoKey();
            e.ToView(null);
        });

        // Vérification publique d'un certificat de mutation : même mécanisme que GlobalAuditLogEntry —
        // entité SANS CLÉ, sans table/vue propre, interrogée uniquement via FromSqlRaw sur la fonction
        // SECURITY DEFINER `verify_mutation_certificate` (migration AddStateIntegrationModule).
        modelBuilder.Entity<MutationCertificateVerification>(e =>
        {
            e.HasNoKey();
            e.ToView(null);
        });

        // Troisième entité SANS CLÉ de la console Super Admin : lit `v_platform_subscriptions`
        // (créée par migration, OWNER sama_ecole, security_invoker = false), même mécanisme que
        // PlatformDashboardStats.
        modelBuilder.Entity<PlatformSubscriptionRow>(e =>
        {
            e.HasNoKey();
            e.ToView("v_platform_subscriptions");
        });

        // Annuaire PUBLIC (B2C) — quatrième entité SANS CLÉ, même mécanisme : lit
        // `public_school_directory` (migration AddPublicSchoolDirectory), qui fige la liste des
        // colonnes ET la condition de consentement. C'est cette vue, et non le code appelant, qui
        // garantit qu'aucune école sans consentement ni aucune colonne sensible n'est atteignable.
        modelBuilder.Entity<PublicSchoolListing>(e =>
        {
            e.HasNoKey();
            e.ToView("public_school_directory");
        });

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
            // Une violation d'unicité est un conflit d'écriture -> 409, jamais un 500 ni un doublon
            // silencieux (AGENTS.md règle #5, docs/Volume_4_API_Design.md §0.4).
            //
            // Le message vient du catalogue et non des noms Npgsql : `pg.TableName` et
            // `pg.ConstraintName` partaient jusqu'ici tels quels jusqu'à l'écran de l'utilisateur, qui
            // lisait un nom de table et un nom d'index tronqué par PostgreSQL. Ces deux valeurs restent
            // disponibles pour les journaux, via TechnicalDetail.
            throw new DuplicateRecordException(
                UniqueConstraintCatalog.Describe(pg.ConstraintName, pg.TableName),
                $"{pg.TableName ?? "table inconnue"} / {pg.ConstraintName ?? "contrainte inconnue"}");
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

    /// <summary>
    /// Console Super Admin — une page du journal d'audit toutes écoles confondues, via la fonction
    /// SECURITY DEFINER `get_global_audit_logs` (migrations AddPlatformAdminViews puis
    /// ExtendGlobalAuditLogsFilters). IgnoreQueryFilters() est un no-op ici (GlobalAuditLogEntry,
    /// ToView(null), n'a aucun HasQueryFilter), gardé explicite pour documenter que cette lecture est
    /// volontairement hors du cloisonnement tenant.
    ///
    /// Paramètres NOMMÉS avec un NpgsqlDbType explicite plutôt que des `{0}` positionnels : un `null`
    /// C# passé tel quel à FromSqlRaw n'a pas de type Postgres déterminable pour une fonction
    /// surchargeable, et Npgsql échoue à résoudre l'appel (« could not determine polymorphic type »).
    /// Même prudence que SubscriptionAdminStore avec DBNull.Value.
    /// </summary>
    public async Task<IReadOnlyList<GlobalAuditLogEntry>> GetGlobalAuditLogsAsync(
        int limit, int offset, string? module, bool? success, Guid? schoolId,
        DateTimeOffset? dateFrom, DateTimeOffset? dateTo, CancellationToken cancellationToken)
    {
        var parameters = new[]
        {
            new NpgsqlParameter("limit_val", NpgsqlTypes.NpgsqlDbType.Integer) { Value = limit },
            new NpgsqlParameter("offset_val", NpgsqlTypes.NpgsqlDbType.Integer) { Value = offset },
            new NpgsqlParameter("module_filter", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)module ?? DBNull.Value },
            new NpgsqlParameter("success_filter", NpgsqlTypes.NpgsqlDbType.Boolean) { Value = (object?)success ?? DBNull.Value },
            new NpgsqlParameter("school_id_filter", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = (object?)schoolId ?? DBNull.Value },
            new NpgsqlParameter("date_from", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = (object?)dateFrom ?? DBNull.Value },
            new NpgsqlParameter("date_to", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = (object?)dateTo ?? DBNull.Value }
        };

        return await GlobalAuditLogEntries
            .FromSqlRaw(
                """
                SELECT * FROM public.get_global_audit_logs(
                    @limit_val, @offset_val, @module_filter, @success_filter, @school_id_filter, @date_from, @date_to)
                """,
                parameters)
            .IgnoreQueryFilters()
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Vérification publique d'un certificat de mutation (Volume 1 §23.5) — la fonction SECURITY
    /// DEFINER `verify_mutation_certificate` s'exécute hors RLS pour répondre à un appel anonyme, au
    /// périmètre minimal (aucune donnée d'élève). Zéro ligne = code inconnu, renvoyé comme `null`.
    /// </summary>
    public async Task<MutationCertificateVerification?> VerifyMutationCertificateAsync(
        string verificationCode, CancellationToken cancellationToken)
        => await MutationCertificateVerifications
            .FromSqlRaw("SELECT * FROM public.verify_mutation_certificate({0})", verificationCode)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc cref="IApplicationDbContext.ToListOrEmptyOnMissingTableAsync{T}" />
    public async Task<IReadOnlyList<T>> ToListOrEmptyOnMissingTableAsync<T>(
        IQueryable<T> query, CancellationToken cancellationToken)
    {
        try
        {
            return await query.ToListAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // TableName n'est pas peuplé par Postgres pour cette classe d'erreur (contrairement aux
            // violations de contrainte) : MessageText, lui, cite toujours la relation manquante.
            logger.LogWarning(ex,
                "Lecture impossible : {PostgresMessage}. Les migrations EF sont-elles à jour sur cet " +
                "environnement ? Lancez 'dotnet ef database update -p src/SamaEcole.Persistence " +
                "-s src/SamaEcole.Web'. Liste vide renvoyée en attendant.",
                ex.MessageText);
            return [];
        }
    }

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
