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

    /// <summary>
    /// Annuaire PUBLIC des établissements (B2C) — vue en LECTURE SEULE <c>public_school_directory</c>,
    /// jamais la table <c>schools</c>. La vue fige les colonnes exposées et la condition de consentement
    /// (voir <see cref="PublicSchoolListing"/> et la migration AddPublicSchoolDirectory) : c'est elle,
    /// et non le Handler, qui garantit qu'aucune donnée sensible ni aucune école non consentante n'est
    /// atteignable par ce chemin anonyme.
    /// </summary>
    DbSet<PublicSchoolListing> PublicSchoolDirectory { get; }

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

    /// <summary>Codes promo (module Tarification &amp; Promotions) : table plateforme, comme Subscriptions/Schools.</summary>
    DbSet<PromoCode> PromoCodes { get; }

    /// <summary>Historique des SMS envoyés (offre Premium) : table tenant, une ligne par tentative.</summary>
    DbSet<SmsMessage> SmsMessages { get; }

    /// <summary>
    /// Établissements supplémentaires d'un utilisateur (groupe scolaire). Table plateforme, hors RLS
    /// par nécessité — voir UserSchool.
    /// </summary>
    DbSet<UserSchool> UserSchools { get; }
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

    /// <summary>
    /// Compteurs de matricules par (école, type, année). Incrémentés par
    /// <c>SamaEcole.Persistence.MatriculeGenerator</c> ; exposés ici pour que le Directeur puisse
    /// fixer le numéro de départ d'une année (SetMatriculeSequenceStartCommand).
    /// </summary>
    DbSet<MatriculeSequence> MatriculeSequences { get; }

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

    /// <summary>Cahier de texte : une entrée de journal par séance réellement tenue (ticket JGK-P04).</summary>
    DbSet<ClassJournalEntry> ClassJournalEntries { get; }

    /// <summary>Surcharges de coefficient par série ou par classe (Évolution N°4).</summary>
    DbSet<SubjectCoefficientOverride> SubjectCoefficientOverrides { get; }

    /// <summary>Matières au programme d'une classe, avec leur groupe d'options (Évolution N°6).</summary>
    DbSet<ClassSubject> ClassSubjects { get; }

    /// <summary>Choix d'option d'un élève pour une année scolaire (Évolution N°6).</summary>
    DbSet<StudentSubjectEnrollment> StudentSubjectEnrollments { get; }

    /// <summary>Tranches d'âge par niveau propres à l'école (Évolution N°7, cartographie IEF).</summary>
    DbSet<GradeAgeNorm> GradeAgeNorms { get; }

    /// <summary>Programme officiel d'une matière pour un niveau : chapitres/objectifs (Évolution N°7).</summary>
    DbSet<SyllabusUnit> SyllabusUnits { get; }

    /// <summary>Unités du programme traitées pendant une séance du cahier de texte (Évolution N°7).</summary>
    DbSet<ClassJournalEntryUnit> ClassJournalEntryUnits { get; }

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

    /// <summary>Journal append-only des changements de contrat (Volume 1 §14.1) : on y AJOUTE, jamais plus.</summary>
    DbSet<EmployeeContractHistory> EmployeeContractHistories { get; }
    DbSet<FichePaie> FichePaies { get; }
    DbSet<TaxeDeclaration> TaxeDeclarations { get; }
    DbSet<FinancialCommitment> FinancialCommitments { get; }
    DbSet<TeacherHourRecord> TeacherHourRecords { get; }

    /// <summary>Échéancier personnalisé d'une inscription (Étape 5 — recouvrement). Au plus un actif par inscription.</summary>
    DbSet<FeeInstallmentPlan> FeeInstallmentPlans { get; }

    /// <summary>Échéances d'un FeeInstallmentPlan — montants et dates librement négociés.</summary>
    DbSet<FeeInstallment> FeeInstallments { get; }

    /// <summary>Lot de relance de débiteurs généré chaque nuit par classe (Étape 5 — recouvrement semi-automatique).</summary>
    DbSet<DebtorReminderBatch> DebtorReminderBatches { get; }

    /// <summary>Débiteurs candidats d'un DebtorReminderBatch, avec leur ancienneté de retard au moment de la génération.</summary>
    DbSet<DebtorReminderBatchItem> DebtorReminderBatchItems { get; }

    // ------------------------------------------------------------------ Module Coran/Franco-Arabe (socle)

    /// <summary>Suivi individuel de mémorisation coranique (module Coran/Franco-Arabe). Verrou optimiste xmin.</summary>
    DbSet<QuranProgress> QuranProgresses { get; }

    /// <summary>Notes d'examen oral de récitation coranique (module Coran/Franco-Arabe). Verrou optimiste xmin.</summary>
    DbSet<QuranEvaluation> QuranEvaluations { get; }

    // ------------------------------------------------------------------ Module Inventaire

    /// <summary>Familles de biens (Mobilier, Manuels scolaires, Informatique…), propres à chaque école.</summary>
    DbSet<InventoryCategory> InventoryCategories { get; }

    /// <summary>
    /// Lots de biens du patrimoine. <c>QuantityAvailable</c> est un compteur DÉRIVÉ : il ne se met à
    /// jour que dans la transaction d'un <see cref="StockMovements">mouvement</see>, sous verrou xmin.
    /// </summary>
    DbSet<InventoryItem> InventoryItems { get; }

    /// <summary>Journal APPEND-ONLY des mouvements de stock : on y AJOUTE, jamais plus (comme FeeChangeHistory).</summary>
    DbSet<StockMovement> StockMovements { get; }

    /// <summary>Fiches de prêt/attribution de matériel aux élèves, enseignants et personnel. Verrou optimiste xmin.</summary>
    DbSet<ItemAssignment> ItemAssignments { get; }

    // ------------------------------------------------------------------ Module Examens officiels

    /// <summary>Campagnes d'examen (CFEE/BFEM/BAC) de l'école, par année scolaire et série.</summary>
    DbSet<ExamSession> ExamSessions { get; }

    /// <summary>Dossiers de candidature. Un élève n'a qu'un dossier par session (index unique).</summary>
    DbSet<ExamDossier> ExamDossiers { get; }

    /// <summary>Résultats/mentions à la délibération. Au plus un résultat par dossier.</summary>
    DbSet<ExamResult> ExamResults { get; }

    /// <summary>
    /// Certificats de mutation délivrés (module Intégration étatique, ticket JGK-M06). Table tenant.
    /// Une ligne par pièce remise : c'est elle qui rend le QR code vérifiable et la révocation
    /// possible — voir <see cref="StudentMutationCertificate"/>.
    /// </summary>
    DbSet<StudentMutationCertificate> StudentMutationCertificates { get; }

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
    /// fonction SECURITY DEFINER `get_global_audit_logs` (migrations AddPlatformAdminViews puis
    /// ExtendGlobalAuditLogsFilters pour les 5 filtres) via FromSqlRaw. Encapsulé ici (comme
    /// SetOriginalConcurrencyToken/ExecuteInTransactionAsync) : FromSqlRaw exige le package EF Core
    /// Relational, volontairement absent de SamaEcole.Application (règle #1 — seul SamaEcole.Persistence
    /// référence un provider/l'infrastructure relationnelle). Chaque filtre à `null` désactive sa
    /// condition côté SQL (voir la fonction) — même convention que GetAuditLogsQuery côté tenant.
    /// </summary>
    Task<IReadOnlyList<GlobalAuditLogEntry>> GetGlobalAuditLogsAsync(
        int limit, int offset, string? module, bool? success, Guid? schoolId,
        DateTimeOffset? dateFrom, DateTimeOffset? dateTo, CancellationToken cancellationToken);

    /// <summary>
    /// Vérification PUBLIQUE d'un certificat de mutation scanné depuis son QR (Volume 1 §23.5) —
    /// appelle la fonction SECURITY DEFINER `verify_mutation_certificate` (migration
    /// AddStateIntegrationModule) via FromSqlRaw, même encapsulation que
    /// <see cref="GetGlobalAuditLogsAsync"/>.
    ///
    /// Renvoie <c>null</c> quand aucun certificat ne porte ce code : l'appelant traduit ça en
    /// « inconnu », jamais en erreur. Le résultat ne contient AUCUNE donnée de l'élève.
    /// </summary>
    Task<MutationCertificateVerification?> VerifyMutationCertificateAsync(
        string verificationCode, CancellationToken cancellationToken);

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

    /// <summary>
    /// Exécute <paramref name="query"/> et retourne une liste vide — plutôt que de laisser remonter
    /// une exception 500 brute — si la table sous-jacente n'existe pas encore (migration EF pas
    /// encore appliquée sur cet environnement). Un avertissement est loggé côté serveur. Encapsulé ici
    /// pour la même raison que <see cref="GetGlobalAuditLogsAsync"/> : seul SamaEcole.Persistence a le
    /// droit de connaître Npgsql (AGENTS.md règle #1). Réservé aux écrans où une liste vide est un état
    /// légitime et sans risque (ex. un module dont l'écran d'accueil ne doit jamais planter si la base
    /// n'a pas encore migré) — ne pas l'utiliser pour masquer une vraie panne de lecture.
    /// </summary>
    Task<IReadOnlyList<T>> ToListOrEmptyOnMissingTableAsync<T>(
        IQueryable<T> query, CancellationToken cancellationToken);
}
