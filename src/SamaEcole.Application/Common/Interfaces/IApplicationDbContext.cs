using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Abstraction du DbContext exposée à Application, implémentée par SamaEcole.Persistence.
/// Application ne référence jamais EF Core directement en dehors de ce contrat minimal.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<School> Schools { get; }
    DbSet<Student> Students { get; }
    DbSet<Classroom> Classrooms { get; }

    /// <summary>Bâtiments physiques de l'établissement (module Infrastructures).</summary>
    DbSet<Building> Buildings { get; }

    /// <summary>Salles physiques d'un bâtiment (module Infrastructures) — indépendant de Classroom.</summary>
    DbSet<Room> Rooms { get; }

    /// <summary>Années scolaires (ticket JGK-C01) : le pivot des inscriptions, des frais et des bulletins.</summary>
    DbSet<SchoolYear> SchoolYears { get; }

    /// <summary>Matières et coefficients par niveau (ticket JGK-C03). Le coefficient pilote les bulletins.</summary>
    DbSet<Subject> Subjects { get; }

    /// <summary>Catégories de frais paramétrables (ticket JGK-F01) : inscription, mensualité, cantine…</summary>
    DbSet<FeeCategory> FeeCategories { get; }

    /// <summary>Barème : le montant d'une catégorie pour une classe (ticket JGK-F01). Verrou optimiste xmin.</summary>
    DbSet<ClassFee> ClassFees { get; }

    /// <summary>Journal append-only des changements de barème (ticket JGK-F01) : on y AJOUTE, jamais plus.</summary>
    DbSet<FeeChangeHistory> FeeChangeHistory { get; }

    /// <summary>Inscriptions/réinscriptions (ticket JGK-E01). Portent le TotalDue calculé. Verrou optimiste xmin.</summary>
    DbSet<Enrollment> Enrollments { get; }

    /// <summary>Détail figé des frais d'une inscription (ticket JGK-E01) : l'instantané du barème pour le reçu.</summary>
    DbSet<EnrollmentFeeLine> EnrollmentFeeLines { get; }

    /// <summary>Encaissements de caisse (ticket JGK-F02). Le solde vit sur l'inscription (verrou optimiste xmin).</summary>
    DbSet<Payment> Payments { get; }

    DbSet<CashierSession> CashierSessions { get; }

    DbSet<PaymentBreakdown> PaymentBreakdowns { get; }

    DbSet<User> Users { get; }
    DbSet<Subscription> Subscriptions { get; }
    DbSet<ScheduleSlot> ScheduleSlots { get; }
    DbSet<Disbursement> Disbursements { get; }

    /// <summary>
    /// Demandes d'inscription self-service (ticket JGK-I01) : table plateforme (hors RLS/tenant) qui
    /// PRÉCÈDE l'existence de l'école. Écrite par le formulaire public anonyme.
    /// </summary>
    DbSet<SchoolRegistrationRequest> SchoolRegistrationRequests { get; }

    /// <summary>Paiements d'abonnement (ticket JGK-I05) : table tenant normale, écrite par le Directeur lui-même.</summary>
    DbSet<SubscriptionPayment> SubscriptionPayments { get; }

    /// <summary>Journal append-only des changements de statut (ticket JGK-A05) : on y AJOUTE, jamais plus.</summary>
    DbSet<UserStatusHistory> UserStatusHistory { get; }

    /// <summary>Paramètres d'établissement (ticket JGK-B02).</summary>
    DbSet<SchoolSettings> SchoolSettings { get; }

    /// <summary>Journal d'audit append-only (ticket JGK-H01) : on y AJOUTE, jamais plus.</summary>
    DbSet<AuditLog> AuditLogs { get; }

    /// <summary>Trimestres d'une année scolaire (ticket JGK-G01), générés automatiquement à sa création.</summary>
    DbSet<Term> Terms { get; }

    /// <summary>Notes (ticket JGK-G01) : une par (élève, matière, trimestre, type d'évaluation). Verrou optimiste xmin.</summary>
    DbSet<Grade> Grades { get; }

    /// <summary>Mentions personnalisables dérivées de la moyenne générale (ticket JGK-G02).</summary>
    DbSet<Mention> Mentions { get; }

    /// <summary>Enseignants (ticket JGK-D03). Matricule généré par le Handler, même règle que Students.</summary>
    DbSet<Teacher> Teachers { get; }

    /// <summary>Matières qu'un enseignant est qualifié à enseigner (ticket JGK-D03).</summary>
    DbSet<TeacherSubject> TeacherSubjects { get; }

    /// <summary>Attributions classe/matière/année d'un enseignant (ticket JGK-D04) — sert d'historique.</summary>
    DbSet<TeacherAssignment> TeacherAssignments { get; }

    /// <summary>Fiches d'appel : un appel par (classe, matière, date, créneau) (ticket JGK-D06).</summary>
    DbSet<AttendanceSheet> AttendanceSheets { get; }

    /// <summary>Statut de chaque élève sur une fiche d'appel (ticket JGK-D06).</summary>
    DbSet<StudentAttendance> StudentAttendances { get; }

    /// <summary>Distinction cochée + observations du conseil des professeurs, par (élève, trimestre) — bulletin JGK-G03.</summary>
    DbSet<ReportCardRemark> ReportCardRemarks { get; }

    DbSet<DisciplineRecord> DisciplineRecords { get; }
    DbSet<AbsenceJustification> AbsenceJustifications { get; }
    DbSet<LateArrival> LateArrivals { get; }
    DbSet<TeacherAttendance> TeacherAttendances { get; }
    DbSet<EarlyDeparture> EarlyDepartures { get; }
    DbSet<ParentSummons> ParentSummons { get; }

    DbSet<EmployeeContract> EmployeeContracts { get; }
    DbSet<FichePaie> FichePaies { get; }
    DbSet<TaxeDeclaration> TaxeDeclarations { get; }
    DbSet<FinancialCommitment> FinancialCommitments { get; }
    DbSet<TeacherHourRecord> TeacherHourRecords { get; }

    /// <summary>
    /// Agrégats plateforme (console Super Admin) : entité SANS CLÉ adossée à la vue PostgreSQL
    /// `v_platform_dashboard_stats`, qui contourne la RLS via `security_invoker = false` +
    /// OWNER sama_ecole (AGENTS.md règle #2, docs/Volume_7_Security.md §8).
    /// </summary>
    DbSet<PlatformDashboardStats> PlatformDashboardStats { get; }

    /// <summary>
    /// Abonnements de toutes les écoles (console Super Admin) : entité SANS CLÉ adossée à la vue
    /// PostgreSQL `v_platform_subscriptions`, même mécanisme que PlatformDashboardStats.
    /// </summary>
    DbSet<PlatformSubscriptionRow> PlatformSubscriptions { get; }

    /// <summary>
    /// Journal d'audit toutes écoles confondues (console Super Admin), une page à la fois — appelle la
    /// fonction SECURITY DEFINER `get_global_audit_logs` (migration AddPlatformAdminViews) via
    /// FromSqlRaw. Encapsulé ici (comme SetOriginalConcurrencyToken/ExecuteInTransactionAsync) : FromSqlRaw
    /// exige le package EF Core Relational, volontairement absent de SamaEcole.Application (règle #1 —
    /// seul SamaEcole.Persistence référence un provider/l'infrastructure relationnelle).
    /// </summary>
    Task<IReadOnlyList<GlobalAuditLogEntry>> GetGlobalAuditLogsAsync(
        int limit, int offset, CancellationToken cancellationToken);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Positionne le jeton de concurrence (xmin) ATTENDU par le client sur une entité déjà suivie,
    /// pour que le prochain SaveChangesAsync refuse en 409 si la ligne a changé entre-temps
    /// (AGENTS.md règle #5). Encapsule l'API de suivi d'EF Core : Application n'a pas à connaître xmin.
    /// </summary>
    void SetOriginalConcurrencyToken<TEntity>(TEntity entity, uint expectedVersion) where TEntity : class;

    /// <summary>
    /// Exécute <paramref name="operation"/> dans UNE seule transaction (la crée si aucune n'est
    /// déjà ouverte). Indispensable dès qu'un matricule est généré : il doit l'être dans la même
    /// transaction que l'insertion, sans quoi un échec d'enregistrement laisserait un numéro
    /// consommé — donc un trou dans la numérotation (AGENTS.md règle #3).
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken);
}
