# Suppression guidée des élèves (PR1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permettre de supprimer (logiquement) n'importe quel élève sans impasse, en bloquant uniquement les données financières actives et en faisant rompre explicitement les autres liens, avec un message qui les énumère.

**Architecture:** Un catalogue de dépendances de l'élève (`StudentDependencyCatalog`) décrit chaque lien (libellé, comptage, rupture). Un analyseur (`StudentDeletionAnalyzer`) en déduit un `DeletionImpact` (blocage financier ou liste de liens). Trois actions séparées côté serveur : analyse (GET), rupture explicite (POST), suppression durcie (DELETE, qui ne rompt jamais rien). L'écran « Suppression guidée » est un composant partagé, réutilisé par les PR2 et PR3.

**Tech Stack:** ASP.NET Core 9, EF Core + PostgreSQL (RLS), MediatR, FluentValidation, xUnit + FluentAssertions + Testcontainers, Alpine.js, Razor.

**Spec:** `docs/superpowers/specs/2026-10-17-deletion-dependency-guard-design.md` (§2.1 trois actions, §2.2 suppression logique, §2.3 protection financière, §3 contrat d'API, §4 messages, §5 interface, §6 catalogue, §7 garanties, §8 tests). **Dépend de la PR0** (annulation de paiement, branche `feat/payment-cancellation`) : un paiement annulé ne bloque plus.

## Global Constraints

- Suppression **logique** uniquement (`AuditableEntity.SoftDelete(actor)`), jamais de `DELETE` physique (AGENTS.md règle #6).
- Texte imposé, mot pour mot, pour un élève avec au moins un paiement non annulé : `Impossible de supprimer cet élève car il possède des transactions financières / reçus enregistrés.`
- Texte de blocage par liens : `Cet élève est actuellement rattaché à {liste précise}. Vous devez rompre ces liens avant de pouvoir effectuer la suppression.`
- Codes d'erreur 409 : `FINANCIAL_RECORDS_EXIST`, `DEPENDENCIES_EXIST`, `DEPENDENCIES_NOT_BREAKABLE`.
- La suppression (`DELETE`) **ne rompt jamais** un lien : s'il en reste, elle répond 409 avec la liste. La rupture est une commande distincte, tracée dans le journal d'audit (`IAuditableRequest`).
- Rôles : `Directeur,Secrétariat` (constante `ManageRoles` de `StudentsController`). Aucun nouveau droit.
- Multi-tenant : requêtes via le Global Query Filter + RLS ; toute requête sur des données déjà archivées avec `IgnoreQueryFilters()` filtre explicitement `SchoolId == tenant`.
- Pas de nouvelle migration destructive ; migration autorisée : index uniques partiels sur `students`.
- Tests obligatoires pour tout ce qui touche à la finance et à l'isolation multi-tenant (AGENTS.md).
- Commits : Conventional Commits, avec la ligne `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.
- Commandes : `dotnet test tests/SamaEcole.UnitTests --filter ...`, idem `IntegrationTests` (Docker requis) et `FunctionalTests`.

## File Structure

| Fichier | Responsabilité |
|---|---|
| `src/SamaEcole.Application/Common/Deletion/DeletionContracts.cs` (new) | `DeletionLink`, `DeletionImpact`, `DeletionErrorCodes`, `DeletionMessages` — types et textes partagés par toutes les entités (PR1→PR3). |
| `src/SamaEcole.Application/Students/Deletion/StudentDependency.cs` (new) | Description d'un type de lien d'un élève. |
| `src/SamaEcole.Application/Students/Deletion/StudentDependencyCatalog.cs` (new) | La liste exhaustive des liens et leur rupture. |
| `src/SamaEcole.Application/Students/Deletion/StudentDeletionAnalyzer.cs` (new) | Calcule le `DeletionImpact` d'un élève ; seul endroit qui connaît la règle financière. |
| `src/SamaEcole.Application/Students/Queries/GetStudentDeletionImpact/GetStudentDeletionImpactQuery.cs` (new) | Analyse en lecture seule. |
| `src/SamaEcole.Application/Students/Commands/UnlinkStudentDependencies/UnlinkStudentDependenciesCommand.cs` (new) | Rupture explicite des liens non financiers. |
| `src/SamaEcole.Application/Students/Commands/DeleteStudent/DeleteStudentCommandHandler.cs` (modify) | Durci : délègue à l'analyseur. |
| `src/SamaEcole.Web/Controllers/StudentsController.cs` (modify) | Deux routes nouvelles. |
| `src/SamaEcole.Persistence/Configurations/StudentConfiguration.cs` + migration (modify/new) | Index uniques partiels : matricule et IEN réutilisables après suppression. |
| `src/SamaEcole.Application/Finance/Queries/GetDailyClosingReportPdf/…`, `GetDailyCashRegisterPdf/…`, `GetPaymentReceipt/…` (modify) | L'historique financier d'un élève supprimé reste lisible. |
| `src/SamaEcole.Web/wwwroot/js/guided-delete.js` + `Views/Shared/_GuidedDeleteModal.cshtml` (new) | Composant d'interface partagé. |
| `src/SamaEcole.Web/wwwroot/js/students.js` + `Views/Students/Index.cshtml` (modify) | Branchement de l'écran Élèves. |
| `tests/...` | Voir chaque tâche. |

---

### Task 1: Contrats et messages de suppression

**Files:**
- Create: `src/SamaEcole.Application/Common/Deletion/DeletionContracts.cs`
- Test: `tests/SamaEcole.UnitTests/Common/DeletionMessagesTests.cs`

**Interfaces:**
- Produces:
  - `record DeletionLink(string Kind, string Label, int Count, bool Breakable, string BreakAction)`
  - `record DeletionImpact(bool CanDelete, bool FinancialBlock, string? Message, IReadOnlyList<DeletionLink> Links)`
  - `static class DeletionErrorCodes { FinancialRecordsExist, DependenciesExist, DependenciesNotBreakable }` (chaînes constantes)
  - `static class DeletionMessages { StudentFinancialBlock; CountLabel(int count, string singular, string plural); JoinLabels(IEnumerable<string>); DependenciesBlock(string head, IReadOnlyList<DeletionLink> links) }`

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using SamaEcole.Application.Common.Deletion;
using Xunit;

namespace SamaEcole.UnitTests.Common;

public class DeletionMessagesTests
{
    [Fact]
    public void The_Student_Financial_Message_Is_The_Mandatory_Text()
    {
        DeletionMessages.StudentFinancialBlock.Should().Be(
            "Impossible de supprimer cet élève car il possède des transactions financières / reçus enregistrés.");
    }

    [Theory]
    [InlineData(1, "1 inscription")]
    [InlineData(3, "3 inscriptions")]
    public void A_Count_Label_Takes_The_Right_Plural(int count, string expected)
    {
        DeletionMessages.CountLabel(count, "inscription", "inscriptions").Should().Be(expected);
    }

    [Theory]
    [InlineData(new[] { "1 inscription" }, "1 inscription")]
    [InlineData(new[] { "1 inscription", "3 matières suivies" }, "1 inscription et 3 matières suivies")]
    [InlineData(new[] { "1 inscription", "3 matières suivies", "12 notes" }, "1 inscription, 3 matières suivies et 12 notes")]
    public void Labels_Are_Joined_In_French(string[] labels, string expected)
    {
        DeletionMessages.JoinLabels(labels).Should().Be(expected);
    }

    [Fact]
    public void The_Dependencies_Message_Lists_Every_Link_Precisely()
    {
        var links = new[]
        {
            new DeletionLink("Enrollment", "1 inscription", 1, true, "Annuler l'inscription et l'archiver"),
            new DeletionLink("Grade", "12 notes", 12, true, "Archiver les notes")
        };

        DeletionMessages.DependenciesBlock("Cet élève est actuellement rattaché", links).Should().Be(
            "Cet élève est actuellement rattaché à 1 inscription et 12 notes. "
            + "Vous devez rompre ces liens avant de pouvoir effectuer la suppression.");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SamaEcole.UnitTests --filter "FullyQualifiedName~DeletionMessagesTests"`
Expected: build error `DeletionMessages` / `DeletionLink` introuvables.

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace SamaEcole.Application.Common.Deletion;

/// <summary>
/// Un lien d'un élément à supprimer (spec 2026-10-17, §3). <paramref name="Breakable"/> : l'utilisateur peut le rompre
/// (commande « rupture des liens ») ; sinon il doit être traité ailleurs (ex. matériel prêté à restituer).
/// </summary>
public record DeletionLink(string Kind, string Label, int Count, bool Breakable, string BreakAction);

/// <summary>
/// Résultat de l'analyse avant suppression. <c>FinancialBlock</c> : une donnée financière active interdit la
/// suppression, sans option de rupture. <c>CanDelete</c> : plus aucun blocage ni lien.
/// </summary>
public record DeletionImpact(bool CanDelete, bool FinancialBlock, string? Message, IReadOnlyList<DeletionLink> Links);

/// <summary>Codes 409 stables destinés au client (AGENTS.md règle #9).</summary>
public static class DeletionErrorCodes
{
    public const string FinancialRecordsExist = "FINANCIAL_RECORDS_EXIST";
    public const string DependenciesExist = "DEPENDENCIES_EXIST";
    public const string DependenciesNotBreakable = "DEPENDENCIES_NOT_BREAKABLE";
}

public static class DeletionMessages
{
    /// <summary>Texte IMPOSÉ par le commanditaire — ne pas reformuler.</summary>
    public const string StudentFinancialBlock =
        "Impossible de supprimer cet élève car il possède des transactions financières / reçus enregistrés.";

    public static string CountLabel(int count, string singular, string plural) =>
        count == 1 ? $"1 {singular}" : $"{count} {plural}";

    /// <summary>« a », « a et b », « a, b et c ».</summary>
    public static string JoinLabels(IEnumerable<string> labels)
    {
        var list = labels.ToList();
        return list.Count switch
        {
            0 => string.Empty,
            1 => list[0],
            _ => string.Join(", ", list.Take(list.Count - 1)) + " et " + list[^1]
        };
    }

    public static string DependenciesBlock(string head, IReadOnlyList<DeletionLink> links) =>
        $"{head} à {JoinLabels(links.Select(l => l.Label))}. "
        + "Vous devez rompre ces liens avant de pouvoir effectuer la suppression.";
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SamaEcole.UnitTests --filter "FullyQualifiedName~DeletionMessagesTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add src/SamaEcole.Application/Common/Deletion tests/SamaEcole.UnitTests/Common/DeletionMessagesTests.cs
git commit -m "feat(deletion): contrats et messages partagés de suppression guidée"
```

---

### Task 2: Catalogue des dépendances de l'élève et garde-fou de couverture

**Files:**
- Create: `src/SamaEcole.Application/Students/Deletion/StudentDependency.cs`
- Create: `src/SamaEcole.Application/Students/Deletion/StudentDependencyCatalog.cs`
- Test: `tests/SamaEcole.IntegrationTests/Students/StudentDependencyCoverageTests.cs`

**Interfaces:**
- Consumes: `DeletionMessages.CountLabel` (Task 1), `IApplicationDbContext` (DbSets : `Enrollments, EnrollmentFeeLines, FeeInstallmentPlans, FeeInstallments, FinancialCommitments, DebtorReminderBatchItems, Grades, StudentSubjectEnrollments, StudentSubjectExemptions, BoardingEnrollments, StudentAttendances, ReportCardRemarks, DisciplineRecords, AbsenceJustifications, LateArrivals, EarlyDepartures, ParentSummons, QuranProgresses, QuranEvaluations, StudentHizbStatuses, StudentHizbEvaluations, ItemAssignments, ExamDossiers, StudentMutationCertificates`), `AuditableEntity.SoftDelete(string)`.
- Produces:
  - `sealed record StudentDependency(string Kind, string Singular, string Plural, string BreakAction, bool Breakable, Type EntityType, Func<IApplicationDbContext, Guid, CancellationToken, Task<int>> CountAsync, Func<IApplicationDbContext, Guid, string, CancellationToken, Task<int>> BreakAsync)`
  - `static class StudentDependencyCatalog { IReadOnlyList<StudentDependency> All; IReadOnlySet<Type> IgnoredDependents; }`

- [ ] **Step 1: Write the failing coverage test**

Ce test est le garde-fou structurel de la spec (§7) : toute clé étrangère vers `Student` doit être classée.

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SamaEcole.Application.Students.Deletion;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.Students;

/// <summary>
/// « Tout est supprimable » tient par construction : une table qui référence un élève et qu'aucun développeur n'a
/// classée (lien rompable ou volontairement ignoré) fait échouer la CI — au lieu de laisser une impasse à l'utilisateur.
/// Le modèle EF se lit sans connexion : aucun conteneur nécessaire.
/// </summary>
public class StudentDependencyCoverageTests
{
    [Fact]
    public void Every_Table_That_References_A_Student_Is_Classified()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model-only;Username=x;Password=x")
            .Options;
        using var db = new ApplicationDbContext(options, new StubTenantProvider(null), NullLogger<ApplicationDbContext>.Instance);

        var referencing = db.Model.GetEntityTypes()
            .SelectMany(t => t.GetForeignKeys())
            .Where(fk => fk.PrincipalEntityType.ClrType == typeof(Student))
            .Select(fk => fk.DeclaringEntityType.ClrType)
            .Distinct()
            .ToList();

        var classified = StudentDependencyCatalog.All.Select(d => d.EntityType)
            .Concat(StudentDependencyCatalog.IgnoredDependents)
            .ToHashSet();

        referencing.Should().NotBeEmpty();
        referencing.Where(t => !classified.Contains(t)).Select(t => t.Name)
            .Should().BeEmpty("chaque table liée à un élève doit être classée dans StudentDependencyCatalog (rompable) ou ignorée explicitement");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~StudentDependencyCoverageTests"`
Expected: build error `StudentDependencyCatalog` introuvable.

- [ ] **Step 3: Write the implementation**

`StudentDependency.cs` :

```csharp
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Students.Deletion;

/// <summary>
/// Un type de lien d'un élève : comment le compter, comment le rompre (archivage logique), comment le nommer.
/// <paramref name="Breakable"/> faux : le lien bloque la suppression et ne se rompt pas en un clic (ex. matériel prêté).
/// </summary>
public sealed record StudentDependency(
    string Kind,
    string Singular,
    string Plural,
    string BreakAction,
    bool Breakable,
    Type EntityType,
    Func<IApplicationDbContext, Guid, CancellationToken, Task<int>> CountAsync,
    Func<IApplicationDbContext, Guid, string, CancellationToken, Task<int>> BreakAsync);
```

`StudentDependencyCatalog.cs` — le helper générique `Owned<T>` fait un comptage et un archivage logique pour toute entité dont une colonne désigne l'élève ; les cas particuliers (inscription et ses enfants, matériel prêté) sont écrits à part :

```csharp
using System.Linq.Expressions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Students.Deletion;

/// <summary>
/// Liste exhaustive des liens d'un élève (spec 2026-10-17, §6). Rompre un lien = l'ARCHIVER (suppression logique,
/// auteur et date conservés) : rien n'est effacé. Un test (StudentDependencyCoverageTests) garantit que toute table
/// qui référence un élève est ici, ou dans <see cref="IgnoredDependents"/>.
///
/// Ne figure PAS ici : les paiements (règle financière, traitée par StudentDeletionAnalyzer) et le rattachement à la
/// classe (Student.ClassroomId, obligatoire : il disparaît avec la fiche archivée).
/// </summary>
public static class StudentDependencyCatalog
{
    /// <summary>Tables qui référencent un élève mais n'empêchent ni ne font rompre rien : traces immuables.</summary>
    public static IReadOnlySet<Type> IgnoredDependents { get; } = new HashSet<Type>
    {
        typeof(SmsMessage) // journal des SMS envoyés : trace immuable, reste en base avec la fiche archivée
    };

    public static IReadOnlyList<StudentDependency> All { get; } =
    [
        Enrollments(),
        Owned<BoardingEnrollment>("BoardingEnrollment", "séjour d'internat", "séjours d'internat", "Libérer le lit et archiver le séjour",
            db => db.BoardingEnrollments, id => e => e.StudentId == id),
        Owned<StudentSubjectEnrollment>("SubjectEnrollment", "matière suivie", "matières suivies", "Retirer l'élève de ces matières",
            db => db.StudentSubjectEnrollments, id => e => e.StudentId == id),
        Owned<StudentSubjectExemption>("SubjectExemption", "dispense de matière", "dispenses de matière", "Retirer ces dispenses",
            db => db.StudentSubjectExemptions, id => e => e.StudentId == id),
        Owned<Grade>("Grade", "note", "notes", "Archiver ces notes",
            db => db.Grades, id => e => e.StudentId == id),
        Owned<ReportCardRemark>("ReportCardRemark", "remarque de bulletin", "remarques de bulletin", "Archiver ces remarques",
            db => db.ReportCardRemarks, id => e => e.StudentId == id),
        Owned<StudentAttendance>("Attendance", "pointage de présence", "pointages de présence", "Archiver ces pointages",
            db => db.StudentAttendances, id => e => e.StudentId == id),
        Owned<AbsenceJustification>("AbsenceJustification", "justificatif d'absence", "justificatifs d'absence", "Archiver ces justificatifs",
            db => db.AbsenceJustifications, id => e => e.StudentId == id),
        Owned<LateArrival>("LateArrival", "retard", "retards", "Archiver ces retards",
            db => db.LateArrivals, id => e => e.StudentId == id),
        Owned<EarlyDeparture>("EarlyDeparture", "sortie anticipée", "sorties anticipées", "Archiver ces sorties",
            db => db.EarlyDepartures, id => e => e.StudentId == id),
        Owned<DisciplineRecord>("Discipline", "dossier disciplinaire", "dossiers disciplinaires", "Archiver ces dossiers",
            db => db.DisciplineRecords, id => e => e.StudentId == id),
        Owned<ParentSummons>("ParentSummons", "convocation de parent", "convocations de parent", "Archiver ces convocations",
            db => db.ParentSummons, id => e => e.StudentId == id),
        Owned<QuranProgress>("QuranProgress", "suivi coranique", "suivis coraniques", "Archiver ces suivis",
            db => db.QuranProgresses, id => e => e.StudentId == id),
        Owned<QuranEvaluation>("QuranEvaluation", "évaluation coranique", "évaluations coraniques", "Archiver ces évaluations",
            db => db.QuranEvaluations, id => e => e.StudentId == id),
        Owned<StudentHizbStatus>("HizbStatus", "statut de Hizb", "statuts de Hizb", "Archiver ces statuts",
            db => db.StudentHizbStatuses, id => e => e.StudentId == id),
        Owned<StudentHizbEvaluation>("HizbEvaluation", "évaluation de Hizb", "évaluations de Hizb", "Archiver ces évaluations",
            db => db.StudentHizbEvaluations, id => e => e.StudentId == id),
        Owned<ExamDossier>("ExamDossier", "dossier d'examen", "dossiers d'examen", "Archiver ces dossiers",
            db => db.ExamDossiers, id => e => e.StudentId == id),
        Owned<StudentMutationCertificate>("MutationCertificate", "attestation de mutation", "attestations de mutation", "Archiver ces attestations",
            db => db.StudentMutationCertificates, id => e => e.StudentId == id),
        ReturnedLoans(),
        UnreturnedLoans()
    ];

    private static StudentDependency Owned<T>(
        string kind, string singular, string plural, string breakAction,
        Func<IApplicationDbContext, DbSet<T>> set,
        Func<Guid, Expression<Func<T, bool>>> forStudent) where T : AuditableEntity =>
        new(kind, singular, plural, breakAction, Breakable: true, typeof(T),
            CountAsync: (db, studentId, ct) => set(db).CountAsync(forStudent(studentId), ct),
            BreakAsync: async (db, studentId, actor, ct) => await ArchiveAsync(set(db).Where(forStudent(studentId)), actor, ct));

    private static async Task<int> ArchiveAsync<T>(IQueryable<T> query, string actor, CancellationToken ct)
        where T : AuditableEntity
    {
        var rows = await query.ToListAsync(ct);
        foreach (var row in rows)
        {
            row.SoftDelete(actor);
        }
        return rows.Count;
    }

    /// <summary>
    /// Inscription : la rompre l'archive avec ses enfants (lignes de frais, échéanciers, engagements, relances). Les
    /// paiements ne sont jamais touchés ici : s'il en reste un non annulé, l'analyseur a déjà bloqué la suppression.
    /// </summary>
    private static StudentDependency Enrollments() => new(
        "Enrollment", "inscription", "inscriptions", "Annuler l'inscription et l'archiver", Breakable: true, typeof(Enrollment),
        CountAsync: (db, studentId, ct) => db.Enrollments.CountAsync(e => e.StudentId == studentId, ct),
        BreakAsync: async (db, studentId, actor, ct) =>
        {
            var enrollments = await db.Enrollments.Where(e => e.StudentId == studentId).ToListAsync(ct);
            var ids = enrollments.Select(e => e.Id).ToList();

            await ArchiveAsync(db.EnrollmentFeeLines.Where(l => ids.Contains(l.EnrollmentId)), actor, ct);
            await ArchiveAsync(db.FinancialCommitments.Where(c => ids.Contains(c.EnrollmentId)), actor, ct);
            await ArchiveAsync(db.DebtorReminderBatchItems.Where(i => ids.Contains(i.EnrollmentId)), actor, ct);

            var plans = await db.FeeInstallmentPlans.Where(p => ids.Contains(p.EnrollmentId)).ToListAsync(ct);
            var planIds = plans.Select(p => p.Id).ToList();
            await ArchiveAsync(db.FeeInstallments.Where(i => planIds.Contains(i.FeeInstallmentPlanId)), actor, ct);
            foreach (var plan in plans)
            {
                plan.SoftDelete(actor);
            }

            foreach (var enrollment in enrollments)
            {
                enrollment.Status = EnrollmentStatus.Cancelled;
                enrollment.SoftDelete(actor);
            }

            return enrollments.Count;
        });

    /// <summary>Matériel déjà restitué ou déclaré perdu : de l'historique, archivé avec l'élève.</summary>
    private static StudentDependency ReturnedLoans() => Owned<ItemAssignment>(
        "ReturnedLoan", "prêt de matériel clos", "prêts de matériel clos", "Archiver ces prêts",
        db => db.ItemAssignments,
        id => a => a.StudentId == id && (a.Status == AssignmentStatus.Restitue || a.Status == AssignmentStatus.Perdu));

    /// <summary>
    /// Matériel encore chez l'élève : le stock en dépend (mouvements append-only), on ne l'archive jamais en silence.
    /// Non rompable — l'utilisateur enregistre d'abord la restitution depuis l'Inventaire.
    /// </summary>
    private static StudentDependency UnreturnedLoans() => new(
        "UnreturnedLoan", "matériel prêté non restitué", "matériels prêtés non restitués", string.Empty,
        Breakable: false, typeof(ItemAssignment),
        CountAsync: (db, studentId, ct) => db.ItemAssignments.CountAsync(
            a => a.StudentId == studentId
                 && (a.Status == AssignmentStatus.EnCours || a.Status == AssignmentStatus.PartiellementRestitue), ct),
        BreakAsync: (_, _, _, _) => Task.FromResult(0));
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~StudentDependencyCoverageTests"`
Expected: PASS. Si une table échoue (nom listé), l'ajouter au catalogue (ou à `IgnoredDependents` avec justification) — c'est le rôle du test.

> Vérifier au passage `FeeInstallment.FeeInstallmentPlanId`, `DebtorReminderBatchItem.EnrollmentId`, `FinancialCommitment.EnrollmentId` et `Enrollment.Status` : noms confirmés dans le domaine au moment de la rédaction du plan (`grep` de la spec).

- [ ] **Step 5: Commit**

```bash
git add src/SamaEcole.Application/Students/Deletion tests/SamaEcole.IntegrationTests/Students/StudentDependencyCoverageTests.cs
git commit -m "feat(deletion): catalogue des dépendances de l'élève et garde-fou de couverture"
```

---

### Task 3: Analyseur de suppression d'un élève

**Files:**
- Create: `src/SamaEcole.Application/Students/Deletion/StudentDeletionAnalyzer.cs`
- Test: `tests/SamaEcole.IntegrationTests/Students/StudentDeletionAnalyzerTests.cs`

**Interfaces:**
- Consumes: `StudentDependencyCatalog.All` (Task 2), `DeletionImpact`, `DeletionLink`, `DeletionMessages` (Task 1), `PaymentStatus.Cancelled`.
- Produces: `class StudentDeletionAnalyzer(IApplicationDbContext db)` avec `Task<DeletionImpact> AnalyzeAsync(Guid studentId, CancellationToken ct)` ; lève `KeyNotFoundException` si l'élève n'existe pas (404).

- [ ] **Step 1: Write the failing test**

Jeu de données : une école, une classe, une année, un élève, une inscription. Cas : paiement actif → blocage financier ; paiement annulé → plus de blocage ; liens comptés ; retard (LateArrival) listé ; matériel non restitué non rompable.

```csharp
using FluentAssertions;
using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Students.Deletion;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Students;

[Trait("Category", "MultiTenant")]
public class StudentDeletionAnalyzerTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-9999-1111-1111-111111111111");
    private static readonly Guid AutreEcole = Guid.Parse("22222222-9999-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-9999-0000-0000-00000000000a");
    private static readonly Guid Annee = Guid.Parse("bbbbbbbb-9999-0000-0000-00000000000b");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-9999-0000-0000-00000000000e");
    private static readonly Guid Inscription = Guid.Parse("11111111-9999-0000-0000-0000000000f1");
    private static readonly Guid Caissier = Guid.Parse("dddddddd-9999-0000-0000-00000000000d");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.Schools.Add(new School { Id = AutreEcole, Name = "École B" });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = Ecole, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true
        });
        owner.Students.Add(new Student
        {
            Id = Eleve, SchoolId = Ecole, Matricule = "ELEV-2026-0001", FullName = "Awa Fall",
            BirthDate = new DateOnly(2015, 5, 20), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe
        });
        owner.Enrollments.Add(new Enrollment
        {
            Id = Inscription, SchoolId = Ecole, StudentId = Eleve, SchoolYearId = Annee, ClassroomId = Classe,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            TotalDue = 100_000m, AmountPaid = 0m, ReceiptNumber = "REC-2026-0001", EnrolledAt = DateTimeOffset.UtcNow
        });
        owner.Users.Add(new User
        {
            Id = Caissier, SchoolId = Ecole, Email = "c@ecole-a.sn", PasswordHash = "h", FullName = "Caissier", Role = Role.Finance
        });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task AddPaymentAsync(string receipt, PaymentStatus status)
    {
        await using var owner = _db.NewOwnerContext();
        var payment = new Payment
        {
            SchoolId = Ecole, EnrollmentId = Inscription, Amount = 30_000m, Method = PaymentMethod.Cash, Status = status,
            BalanceAfter = 70_000m, ReceiptNumber = receipt, ReceivedByUserId = Caissier, PaidAt = DateTimeOffset.UtcNow
        };
        if (status == PaymentStatus.Cancelled)
        {
            payment.CancelledAt = DateTimeOffset.UtcNow;
            payment.CancellationReason = "Erreur";
            payment.CancellationOutcome = PaymentCancellationOutcome.KeptBySchool;
        }
        owner.Payments.Add(payment);
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<DeletionImpact> AnalyzeAsync(Guid school, Guid student)
    {
        await using var ctx = _db.NewAppContext(school);
        return await new StudentDeletionAnalyzer(ctx).AnalyzeAsync(student, CancellationToken.None);
    }

    [Fact]
    public async Task A_Student_With_An_Active_Payment_Is_Financially_Blocked_Without_Any_Link_To_Break()
    {
        await AddPaymentAsync("REC-1", PaymentStatus.Partial);

        var impact = await AnalyzeAsync(Ecole, Eleve);

        impact.CanDelete.Should().BeFalse();
        impact.FinancialBlock.Should().BeTrue();
        impact.Message.Should().Be(DeletionMessages.StudentFinancialBlock);
        impact.Links.Should().BeEmpty("aucune option de rupture n'est proposée quand le blocage est financier");
    }

    [Fact]
    public async Task A_Cancelled_Payment_Does_Not_Block()
    {
        await AddPaymentAsync("REC-1", PaymentStatus.Cancelled);

        var impact = await AnalyzeAsync(Ecole, Eleve);

        impact.FinancialBlock.Should().BeFalse();
        impact.Links.Should().ContainSingle(l => l.Kind == "Enrollment").Which.Count.Should().Be(1);
    }

    [Fact]
    public async Task Non_Financial_Links_Are_Counted_And_Described_Precisely()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            owner.LateArrivals.Add(new LateArrival { SchoolId = Ecole, StudentId = Eleve, Date = DateTime.UtcNow.Date, Minutes = 10, Reason = "Transport" });
            owner.LateArrivals.Add(new LateArrival { SchoolId = Ecole, StudentId = Eleve, Date = DateTime.UtcNow.Date.AddDays(-1), Minutes = 5, Reason = "Pluie" });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        var impact = await AnalyzeAsync(Ecole, Eleve);

        impact.CanDelete.Should().BeFalse();
        impact.FinancialBlock.Should().BeFalse();
        impact.Links.Select(l => l.Label).Should().BeEquivalentTo(["1 inscription", "2 retards"]);
        impact.Links.Should().OnlyContain(l => l.Breakable);
        impact.Message.Should().Be(
            "Cet élève est actuellement rattaché à 1 inscription et 2 retards. "
            + "Vous devez rompre ces liens avant de pouvoir effectuer la suppression.");
    }

    [Fact]
    public async Task A_Student_Without_Any_Link_Can_Be_Deleted()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            owner.Students.Add(new Student
            {
                Id = Guid.NewGuid(), SchoolId = Ecole, Matricule = "ELEV-2026-0002", FullName = "Moussa Diop",
                BirthDate = new DateOnly(2014, 1, 2), BirthPlace = "Thiès", Gender = "M", ClassroomId = Classe
            });
            await owner.SaveChangesAsync(CancellationToken.None);
        }
        Guid free;
        await using (var ctx = _db.NewAppContext(Ecole))
        {
            free = ctx.Students.Single(s => s.Matricule == "ELEV-2026-0002").Id;
        }

        var impact = await AnalyzeAsync(Ecole, free);

        impact.CanDelete.Should().BeTrue();
        impact.Links.Should().BeEmpty();
    }

    [Fact]
    public async Task A_Student_Of_Another_School_Is_Not_Found()
    {
        var act = async () => await AnalyzeAsync(AutreEcole, Eleve);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~StudentDeletionAnalyzerTests"`
Expected: build error `StudentDeletionAnalyzer` introuvable.

- [ ] **Step 3: Write minimal implementation**

```csharp
using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Students.Deletion;

/// <summary>
/// Calcule ce qui empêche (ou accompagne) la suppression d'un élève (spec 2026-10-17, §2.3 et §6). Seul endroit qui
/// connaît la règle financière : un paiement NON ANNULÉ rattaché à l'une de ses inscriptions interdit la
/// suppression, sans option de rupture. Un paiement annulé ne bloque plus.
///
/// Lecture seule : ne modifie rien. Le Global Query Filter + la RLS bornent à l'école courante — un élève d'une autre
/// école est introuvable (404).
/// </summary>
public class StudentDeletionAnalyzer(IApplicationDbContext db)
{
    public async Task<DeletionImpact> AnalyzeAsync(Guid studentId, CancellationToken cancellationToken)
    {
        var exists = await db.Students.AnyAsync(s => s.Id == studentId, cancellationToken);
        if (!exists)
        {
            throw new KeyNotFoundException($"Élève {studentId} introuvable.");
        }

        var hasActivePayment = await (
            from p in db.Payments
            join e in db.Enrollments on p.EnrollmentId equals e.Id
            where e.StudentId == studentId && p.Status != PaymentStatus.Cancelled
            select p.Id)
            .AnyAsync(cancellationToken);

        if (hasActivePayment)
        {
            return new DeletionImpact(false, true, DeletionMessages.StudentFinancialBlock, []);
        }

        var links = new List<DeletionLink>();
        foreach (var dependency in StudentDependencyCatalog.All)
        {
            var count = await dependency.CountAsync(db, studentId, cancellationToken);
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

        if (links.Count == 0)
        {
            return new DeletionImpact(true, false, null, links);
        }

        return new DeletionImpact(
            false, false, DeletionMessages.DependenciesBlock("Cet élève est actuellement rattaché", links), links);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~StudentDeletionAnalyzerTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/SamaEcole.Application/Students/Deletion/StudentDeletionAnalyzer.cs tests/SamaEcole.IntegrationTests/Students/StudentDeletionAnalyzerTests.cs
git commit -m "feat(deletion): analyseur de suppression d'un élève (blocage financier et liens)"
```

---

### Task 4: Rupture explicite des liens (commande)

**Files:**
- Create: `src/SamaEcole.Application/Students/Commands/UnlinkStudentDependencies/UnlinkStudentDependenciesCommand.cs`
- Test: `tests/SamaEcole.IntegrationTests/Students/UnlinkStudentDependenciesTests.cs`

**Interfaces:**
- Consumes: `StudentDeletionAnalyzer.AnalyzeAsync`, `StudentDependencyCatalog.All` (`BreakAsync`), `ICurrentUserService.UserId`, `IApplicationDbContext.ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>>, CancellationToken)`, `IKpiCacheService.Invalidate(KpiCacheKeys.DirectorDashboard)`.
- Produces: `record UnlinkStudentDependenciesCommand(Guid StudentId) : IRequest<DeletionImpact>, IAuditableRequest` ; `UnlinkStudentDependenciesCommandHandler(IApplicationDbContext, ICurrentUserService, IKpiCacheService)`.

- [ ] **Step 1: Write the failing test**

Réutiliser exactement le jeu de données de `StudentDeletionAnalyzerTests` (copier `InitializeAsync` et les constantes, avec d'autres GUID : remplacer `9999` par `aaaa`). Tests :

```csharp
[Fact]
public async Task Unlinking_Archives_The_Links_And_Leaves_The_Student_Deletable()
{
    await using (var owner = _db.NewOwnerContext())
    {
        owner.LateArrivals.Add(new LateArrival { SchoolId = Ecole, StudentId = Eleve, Date = DateTime.UtcNow.Date, Minutes = 10, Reason = "Transport" });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    await using var ctx = _db.NewAppContext(Ecole);
    var impact = await NewHandler(ctx).Handle(new UnlinkStudentDependenciesCommand(Eleve), CancellationToken.None);

    impact.CanDelete.Should().BeTrue();

    await using var check = _db.NewAppContext(Ecole);
    (await check.Enrollments.CountAsync(e => e.StudentId == Eleve)).Should().Be(0, "l'inscription est archivée, plus visible");
    (await check.LateArrivals.CountAsync(l => l.StudentId == Eleve)).Should().Be(0);

    // Rien n'est effacé : les lignes existent toujours, marquées supprimées avec l'auteur.
    await using var owner2 = _db.NewOwnerContext();
    var archived = await owner2.Enrollments.IgnoreQueryFilters().SingleAsync(e => e.Id == Inscription);
    archived.IsDeleted.Should().BeTrue();
    archived.DeletedBy.Should().Be(Directeur.ToString());
    archived.Status.Should().Be(EnrollmentStatus.Cancelled);
}

[Fact]
public async Task Unlinking_Is_Refused_When_An_Active_Payment_Exists_And_Touches_Nothing()
{
    await AddPaymentAsync("REC-1", PaymentStatus.Partial);

    await using var ctx = _db.NewAppContext(Ecole);
    var act = async () => await NewHandler(ctx).Handle(new UnlinkStudentDependenciesCommand(Eleve), CancellationToken.None);

    (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(DeletionErrorCodes.FinancialRecordsExist);

    await using var check = _db.NewAppContext(Ecole);
    (await check.Enrollments.CountAsync(e => e.StudentId == Eleve)).Should().Be(1);
}

[Fact]
public async Task Unlinking_Is_Refused_While_Loaned_Equipment_Is_Not_Returned()
{
    await using (var owner = _db.NewOwnerContext())
    {
        var category = new InventoryCategory { Id = Guid.NewGuid(), SchoolId = Ecole, Name = "Manuels" };
        var item = new InventoryItem
        {
            Id = Guid.NewGuid(), SchoolId = Ecole, Name = "Manuel de maths", CategoryId = category.Id,
            QuantityTotal = 10, QuantityAvailable = 9
        };
        owner.InventoryCategories.Add(category);
        owner.InventoryItems.Add(item);
        owner.ItemAssignments.Add(new ItemAssignment
        {
            SchoolId = Ecole, ItemId = item.Id, Quantity = 1, BeneficiaryType = AssignmentBeneficiaryType.Eleve,
            StudentId = Eleve, BeneficiaryLabel = "Awa Fall", AssignedOn = new DateOnly(2026, 10, 12),
            Status = AssignmentStatus.EnCours
        });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    await using var ctx = _db.NewAppContext(Ecole);
    var act = async () => await NewHandler(ctx).Handle(new UnlinkStudentDependenciesCommand(Eleve), CancellationToken.None);

    var error = (await act.Should().ThrowAsync<BusinessRuleException>()).Which;
    error.Code.Should().Be(DeletionErrorCodes.DependenciesNotBreakable);
    error.Message.Should().Contain("matériel prêté non restitué");

    await using var check = _db.NewAppContext(Ecole);
    (await check.Enrollments.CountAsync(e => e.StudentId == Eleve)).Should().Be(1, "rien n'est rompu tant que le matériel n'est pas restitué");
}

[Fact]
public async Task Unlinking_Twice_Is_Harmless()
{
    await using (var ctx = _db.NewAppContext(Ecole))
    {
        await NewHandler(ctx).Handle(new UnlinkStudentDependenciesCommand(Eleve), CancellationToken.None);
    }

    await using var again = _db.NewAppContext(Ecole);
    var impact = await NewHandler(again).Handle(new UnlinkStudentDependenciesCommand(Eleve), CancellationToken.None);

    impact.CanDelete.Should().BeTrue();
}
```

Avec les helpers : `Directeur` (GUID d'un utilisateur Directeur inséré dans `InitializeAsync`), `NewHandler(ctx)` = `new UnlinkStudentDependenciesCommandHandler(ctx, new FixedCurrentUser(Directeur), new NoKpiCache())` (copier les petites classes `FixedCurrentUser` et `NoKpiCache` de `CancelPaymentTests`), `AddPaymentAsync` comme à la Task 3.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~UnlinkStudentDependenciesTests"`
Expected: build error `UnlinkStudentDependenciesCommand` introuvable.

- [ ] **Step 3: Write minimal implementation**

```csharp
using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Students.Deletion;
using MediatR;

namespace SamaEcole.Application.Students.Commands.UnlinkStudentDependencies;

/// <summary>
/// POST /students/{id}/unlink-dependencies — rompt, sur demande EXPLICITE de l'utilisateur, les liens non financiers
/// d'un élève (spec 2026-10-17, §2.1). Ne supprime PAS la fiche : c'est DeleteStudentCommand, qui ne rompt jamais rien.
/// Chaque lien est archivé (suppression logique, auteur et date conservés). IAuditableRequest : la rupture est tracée.
/// </summary>
public record UnlinkStudentDependenciesCommand(Guid StudentId) : IRequest<DeletionImpact>, IAuditableRequest;

public class UnlinkStudentDependenciesCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser,
    IKpiCacheService kpiCache)
    : IRequestHandler<UnlinkStudentDependenciesCommand, DeletionImpact>
{
    public async Task<DeletionImpact> Handle(UnlinkStudentDependenciesCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        var analyzer = new StudentDeletionAnalyzer(dbContext);

        var impact = await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            var before = await analyzer.AnalyzeAsync(request.StudentId, ct);

            if (before.FinancialBlock)
            {
                throw new BusinessRuleException(before.Message!, DeletionErrorCodes.FinancialRecordsExist);
            }

            if (before.Links.Any(l => !l.Breakable))
            {
                var blocking = before.Links.Where(l => !l.Breakable).ToList();
                throw new BusinessRuleException(
                    $"Impossible de rompre ces liens automatiquement : {DeletionMessages.JoinLabels(blocking.Select(l => l.Label))}. "
                    + "Traitez-les d'abord (ex. enregistrez la restitution du matériel).",
                    DeletionErrorCodes.DependenciesNotBreakable) { Details = blocking };
            }

            foreach (var dependency in StudentDependencyCatalog.All.Where(d => d.Breakable))
            {
                await dependency.BreakAsync(dbContext, request.StudentId, actorId.ToString(), ct);
            }

            await dbContext.SaveChangesAsync(ct);

            return await analyzer.AnalyzeAsync(request.StudentId, ct);
        }, cancellationToken);

        // Après commit : les effectifs et l'occupation des classes ont changé.
        kpiCache.Invalidate(KpiCacheKeys.DirectorDashboard);

        return impact;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~UnlinkStudentDependenciesTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/SamaEcole.Application/Students/Commands/UnlinkStudentDependencies tests/SamaEcole.IntegrationTests/Students/UnlinkStudentDependenciesTests.cs
git commit -m "feat(deletion): rupture explicite des liens d'un élève (archivage, audit)"
```

---

### Task 5: Suppression durcie, analyse en lecture et routes

**Files:**
- Modify: `src/SamaEcole.Application/Students/Commands/DeleteStudent/DeleteStudentCommandHandler.cs` (le corps de `Handle`, lignes 13-52)
- Create: `src/SamaEcole.Application/Students/Queries/GetStudentDeletionImpact/GetStudentDeletionImpactQuery.cs`
- Modify: `src/SamaEcole.Web/Controllers/StudentsController.cs` (après la méthode `Delete`, vers la ligne 240)
- Modify: `src/SamaEcole.Application/Students/Commands/DeleteStudent/DeleteStudentCommand.cs` (mettre à jour le commentaire : plus de règle « aucune inscription »)
- Test: `tests/SamaEcole.FunctionalTests/Students/StudentDeletionEndpointsTests.cs`

**Interfaces:**
- Consumes: `StudentDeletionAnalyzer`, `DeletionErrorCodes`, `DeletionMessages`, `IKpiCacheService`.
- Produces: `record GetStudentDeletionImpactQuery(Guid StudentId) : IRequest<DeletionImpact>` ; routes `GET /api/v1/students/{id}/deletion-impact` et `POST /api/v1/students/{id}/unlink-dependencies` (rôles `Directeur,Secretariat`) ; `DELETE /api/v1/students/{id}?rowVersion=` répond 409 `FINANCIAL_RECORDS_EXIST` ou `DEPENDENCIES_EXIST` (avec `details` = liste des liens).

- [ ] **Step 1: Write the failing functional test**

Copier le gabarit de `PaymentsEndpointsTests` (`IClassFixture<AuthApiFactory>`, `TokenAsync`, `SendAsync`, `SeedFeeAsync`, `SeedEnrolledStudentAsync`, `OpenFinanceSessionAsync`, `PaymentBody`) dans une nouvelle classe `StudentDeletionEndpointsTests`, puis :

```csharp
private record StudentDetail(StudentIdentity Identity);
private record StudentIdentity(Guid Id, uint RowVersion);
private record Link(string Kind, string Label, int Count, bool Breakable, string BreakAction);
private record Impact(bool CanDelete, bool FinancialBlock, string? Message, List<Link> Links);
private record ApiError(string Code, string Message);

private async Task<(Guid Id, uint RowVersion)> DetailAsync(string token, string matricule)
{
    var id = await ResolveStudentIdAsync(token, matricule);
    var detail = (await (await SendAsync(HttpMethod.Get, $"/api/v1/students/{id}", token))
        .Content.ReadFromJsonAsync<StudentDetail>())!;
    return (id, detail.Identity.RowVersion);
}

[Fact]
public async Task An_Enrolled_Student_Without_Payment_Is_Blocked_Then_Unlinked_Then_Deleted()
{
    var enrollment = await SeedEnrolledStudentAsync();
    var secretaire = await SecretaireTokenAsync();
    var (id, rowVersion) = await DetailAsync(secretaire, enrollment.Matricule);

    // 1) La suppression directe est refusée et énumère les liens.
    var blocked = await SendAsync(HttpMethod.Delete, $"/api/v1/students/{id}?rowVersion={rowVersion}", secretaire);
    blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
    var error = (await blocked.Content.ReadFromJsonAsync<ApiError>())!;
    error.Code.Should().Be("DEPENDENCIES_EXIST");
    error.Message.Should().Contain("1 inscription").And.Contain("Vous devez rompre ces liens");

    // 2) L'analyse dit la même chose, sans rien modifier.
    var impact = (await (await SendAsync(HttpMethod.Get, $"/api/v1/students/{id}/deletion-impact", secretaire))
        .Content.ReadFromJsonAsync<Impact>())!;
    impact.CanDelete.Should().BeFalse();
    impact.Links.Should().Contain(l => l.Kind == "Enrollment" && l.Breakable);

    // 3) Rupture explicite, puis suppression.
    var unlink = await SendAsync(HttpMethod.Post, $"/api/v1/students/{id}/unlink-dependencies", secretaire);
    unlink.StatusCode.Should().Be(HttpStatusCode.OK);
    (await unlink.Content.ReadFromJsonAsync<Impact>())!.CanDelete.Should().BeTrue();

    var deleted = await SendAsync(HttpMethod.Delete, $"/api/v1/students/{id}?rowVersion={rowVersion}", secretaire);
    deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);

    // 4) L'élève a disparu des listes.
    var list = await SendAsync(HttpMethod.Get, $"/api/v1/students?search={enrollment.Matricule}", secretaire);
    (await list.Content.ReadFromJsonAsync<PaginatedStudents>())!.Items.Should().BeEmpty();
}

[Fact]
public async Task A_Student_With_A_Payment_Cannot_Be_Deleted_Until_The_Payment_Is_Cancelled()
{
    var enrollment = await SeedEnrolledStudentAsync();
    var finance = await FinanceTokenAsync();
    await OpenFinanceSessionAsync(finance);
    var pay = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance, PaymentBody(enrollment.EnrollmentId, 20_000m));
    var payment = (await pay.Content.ReadFromJsonAsync<PaymentResult>())!;

    var directeur = await DirecteurTokenAsync();
    var (id, rowVersion) = await DetailAsync(directeur, enrollment.Matricule);

    // Bloqué, sans option de rupture, avec le texte imposé.
    var blocked = await SendAsync(HttpMethod.Delete, $"/api/v1/students/{id}?rowVersion={rowVersion}", directeur);
    blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
    var error = (await blocked.Content.ReadFromJsonAsync<ApiError>())!;
    error.Code.Should().Be("FINANCIAL_RECORDS_EXIST");
    error.Message.Should().Be("Impossible de supprimer cet élève car il possède des transactions financières / reçus enregistrés.");
    (await SendAsync(HttpMethod.Post, $"/api/v1/students/{id}/unlink-dependencies", directeur))
        .StatusCode.Should().Be(HttpStatusCode.Conflict);

    // Option A : on annule le paiement, l'élève redevient supprimable.
    (await SendAsync(HttpMethod.Post, $"/api/v1/finance/payments/{payment.PaymentId}/cancel", directeur,
        new { reason = "Saisi sur le mauvais élève" })).StatusCode.Should().Be(HttpStatusCode.OK);
    (await SendAsync(HttpMethod.Post, $"/api/v1/students/{id}/unlink-dependencies", directeur))
        .StatusCode.Should().Be(HttpStatusCode.OK);
    (await SendAsync(HttpMethod.Delete, $"/api/v1/students/{id}?rowVersion={rowVersion}", directeur))
        .StatusCode.Should().Be(HttpStatusCode.NoContent);
}

[Fact]
public async Task Finance_Cannot_Use_The_Deletion_Routes()
{
    var enrollment = await SeedEnrolledStudentAsync();
    var finance = await FinanceTokenAsync();
    var id = await ResolveStudentIdAsync(finance, enrollment.Matricule);

    (await SendAsync(HttpMethod.Get, $"/api/v1/students/{id}/deletion-impact", finance)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    (await SendAsync(HttpMethod.Post, $"/api/v1/students/{id}/unlink-dependencies", finance)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
}

[Fact]
public async Task The_Deletion_Routes_Return_404_For_An_Unknown_Student()
{
    var directeur = await DirecteurTokenAsync();
    (await SendAsync(HttpMethod.Get, $"/api/v1/students/{Guid.NewGuid()}/deletion-impact", directeur)).StatusCode.Should().Be(HttpStatusCode.NotFound);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SamaEcole.FunctionalTests --filter "FullyQualifiedName~StudentDeletionEndpointsTests"`
Expected: FAIL — routes 404/405 et suppression qui répond l'ancien message.

- [ ] **Step 3: Write the implementation**

`GetStudentDeletionImpactQuery.cs` :

```csharp
using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Students.Deletion;
using MediatR;

namespace SamaEcole.Application.Students.Queries.GetStudentDeletionImpact;

/// <summary>GET /students/{id}/deletion-impact — ce qui empêche ou accompagne la suppression, sans rien modifier.</summary>
public record GetStudentDeletionImpactQuery(Guid StudentId) : IRequest<DeletionImpact>;

public class GetStudentDeletionImpactQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetStudentDeletionImpactQuery, DeletionImpact>
{
    public Task<DeletionImpact> Handle(GetStudentDeletionImpactQuery request, CancellationToken cancellationToken) =>
        new StudentDeletionAnalyzer(dbContext).AnalyzeAsync(request.StudentId, cancellationToken);
}
```

`DeleteStudentCommandHandler.cs` — remplacer tout le corps de la classe par :

```csharp
public class DeleteStudentCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser,
    IKpiCacheService kpiCache)
    : IRequestHandler<DeleteStudentCommand, Unit>
{
    public async Task<Unit> Handle(DeleteStudentCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Le Global Query Filter + la policy RLS bornent déjà la recherche à l'école courante : viser un élève d'une
        // autre école renvoie 404, jamais une suppression silencieuse.
        var student = await dbContext.Students
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Élève {request.Id} introuvable.");

        // Spec 2026-10-17 : la suppression NE ROMPT JAMAIS un lien. Un paiement actif la refuse (texte imposé) ; tout
        // autre lien restant la refuse en les énumérant — l'utilisateur les rompt avant, explicitement
        // (UnlinkStudentDependenciesCommand).
        var impact = await new StudentDeletionAnalyzer(dbContext).AnalyzeAsync(request.Id, cancellationToken);

        if (impact.FinancialBlock)
        {
            throw new BusinessRuleException(impact.Message!, DeletionErrorCodes.FinancialRecordsExist);
        }

        if (!impact.CanDelete)
        {
            throw new BusinessRuleException(impact.Message!, DeletionErrorCodes.DependenciesExist) { Details = impact.Links };
        }

        // Même verrou optimiste que UpdateStudentCommandHandler (AGENTS.md règle #5).
        dbContext.SetOriginalConcurrencyToken(student, request.RowVersion);

        student.SoftDelete(actorId.ToString());

        await dbContext.SaveChangesAsync(cancellationToken);

        // L'effectif des classes et le quota d'élèves ont changé.
        kpiCache.Invalidate(KpiCacheKeys.DirectorDashboard);

        return Unit.Value;
    }
}
```

(ajouter `using SamaEcole.Application.Common.Deletion; using SamaEcole.Application.Students.Deletion;`).

`StudentsController.cs` — après la méthode `Delete` :

```csharp
    /// <summary>
    /// Ce qui empêche ou accompagne la suppression d'un élève (spec 2026-10-17, §3) : blocage financier (paiement non
    /// annulé) ou liste précise des liens à rompre. Lecture seule.
    /// </summary>
    [HttpGet("{id:guid}/deletion-impact")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<DeletionImpact>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeletionImpact(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetStudentDeletionImpactQuery(id), cancellationToken));

    /// <summary>
    /// Rupture EXPLICITE des liens non financiers d'un élève (archivage logique, tracé). Ne supprime pas la fiche.
    /// 409 : blocage financier ou lien non rompable (matériel non restitué).
    /// </summary>
    [HttpPost("{id:guid}/unlink-dependencies")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<DeletionImpact>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UnlinkDependencies(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new UnlinkStudentDependenciesCommand(id), cancellationToken));
```

avec `using SamaEcole.Application.Common.Deletion; using SamaEcole.Application.Students.Commands.UnlinkStudentDependencies; using SamaEcole.Application.Students.Queries.GetStudentDeletionImpact;`. Mettre à jour le commentaire `<summary>` de `Delete` et de `DeleteStudentCommand` : « Refusée en 409 tant qu'un paiement non annulé ou un lien existe ; voir deletion-impact et unlink-dependencies ».

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/SamaEcole.FunctionalTests --filter "FullyQualifiedName~StudentDeletionEndpointsTests"` puis `dotnet test tests/SamaEcole.FunctionalTests --filter "FullyQualifiedName~Students"` (non-régression : d'anciens tests attendaient l'ancien message de `DELETE`, les adapter au nouveau comportement, jamais les supprimer sans remplaçant) et `dotnet test tests/SamaEcole.UnitTests`.
Expected: PASS. Un test de sécurité transverse (couverture des contrôleurs / matrice de rôles) peut exiger la déclaration des nouvelles routes : le lancer (`dotnet test tests/SamaEcole.FunctionalTests --filter "FullyQualifiedName~Coverage|FullyQualifiedName~Security"`) et le mettre à jour comme dans la PR #95.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(students): suppression guidée (analyse, rupture explicite, suppression durcie)"
```

---

### Task 6: Matricule et IEN réutilisables après suppression (index uniques partiels)

**Files:**
- Modify: `src/SamaEcole.Persistence/Configurations/StudentConfiguration.cs` (lignes 51 et 62-64)
- Create: migration `AddPartialStudentUniqueIndexes` (générée)
- Test: `tests/SamaEcole.IntegrationTests/Students/StudentMatriculeReuseTests.cs`

**Interfaces:**
- Produces: index uniques partiels `IX_students_SchoolId_Matricule` (`WHERE "IsDeleted" = false`) et `IX_students_SchoolId_IenNumber` (`WHERE "IenNumber" IS NOT NULL AND "IsDeleted" = false`).

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task The_Matricule_Of_A_Deleted_Student_Can_Be_Used_Again_But_Not_Twice_Among_Active_Students()
{
    // École + classe comme dans StudentDeletionAnalyzerTests (copier InitializeAsync).
    await using (var owner = _db.NewOwnerContext())
    {
        var first = new Student { SchoolId = Ecole, Matricule = "ELEV-2026-0001", FullName = "Awa Fall",
            BirthDate = new DateOnly(2015, 5, 20), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe };
        owner.Students.Add(first);
        await owner.SaveChangesAsync();
        first.SoftDelete("director");
        await owner.SaveChangesAsync();
    }

    await using (var owner = _db.NewOwnerContext())
    {
        owner.Students.Add(new Student { SchoolId = Ecole, Matricule = "ELEV-2026-0001", FullName = "Moussa Diop",
            BirthDate = new DateOnly(2014, 1, 2), BirthPlace = "Thiès", Gender = "M", ClassroomId = Classe });
        var reuse = async () => await owner.SaveChangesAsync();
        await reuse.Should().NotThrowAsync("le matricule d'une fiche supprimée est libre");
    }

    await using (var owner = _db.NewOwnerContext())
    {
        owner.Students.Add(new Student { SchoolId = Ecole, Matricule = "ELEV-2026-0001", FullName = "Doublon",
            BirthDate = new DateOnly(2013, 3, 4), BirthPlace = "Kaolack", Gender = "M", ClassroomId = Classe });
        var duplicate = async () => await owner.SaveChangesAsync();
        await duplicate.Should().ThrowAsync<DbUpdateException>("deux élèves ACTIFS ne partagent jamais un matricule");
    }
}
```

Le `ApplicationDbContext` peut traduire l'erreur en `DuplicateRecordException` : adapter l'assertion au type réellement levé en regardant `ApplicationDbContext.SaveChangesAsync` (ne pas affaiblir le test en `Exception`).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~StudentMatriculeReuseTests"`
Expected: FAIL à la deuxième insertion (index unique non partiel).

- [ ] **Step 3: Write minimal implementation**

Dans `StudentConfiguration.cs` :

```csharp
        // Un matricule est unique par école parmi les élèves ACTIFS : celui d'une fiche supprimée (logiquement) est
        // libre (spec 2026-10-01 §1.4 et 2026-10-17 §2.2). La numérotation automatique reste un compteur séquentiel
        // sans trou : elle ne réémet pas d'elle-même un numéro supprimé (règle #3).
        builder.HasIndex(s => new { s.SchoolId, s.Matricule })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");
```

et pour l'IEN : `.HasFilter("\"IenNumber\" IS NOT NULL AND \"IsDeleted\" = false")`.

Puis générer la migration (chaîne de connexion factice, la génération ne se connecte pas) :

```bash
ConnectionStrings__Migrations="Host=localhost;Port=5999;Database=x;Username=x;Password=x" \
  dotnet ef migrations add AddPartialStudentUniqueIndexes -p src/SamaEcole.Persistence -s src/SamaEcole.Web
```

Vérifier que `Up` ne contient que `DropIndex` + `CreateIndex` filtrés (aucune perte de données) et que `Down` les rétablit.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~StudentMatriculeReuseTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SamaEcole.Persistence tests/SamaEcole.IntegrationTests/Students/StudentMatriculeReuseTests.cs
git commit -m "feat(students): matricule et IEN réutilisables après suppression (index uniques partiels)"
```

---

### Task 7: L'historique financier d'un élève supprimé reste lisible

**Pourquoi :** un élève supprimé a des paiements ANNULÉS qui restent en base (preuve comptable). Les journaux de caisse et le reçu joignent `Enrollment` et `Student`, tous deux filtrés (`IsDeleted`) : sans correction, `p.Enrollment` vaut `null` dans le rapport de clôture (NullReferenceException) et le reçu annulé devient introuvable.

**Files:**
- Modify: `src/SamaEcole.Application/Finance/Queries/GetDailyClosingReportPdf/GetDailyClosingReportPdfQuery.cs` (le dictionnaire `payments` et `students`)
- Modify: `src/SamaEcole.Application/Finance/Queries/GetDailyCashRegisterPdf/GetDailyCashRegisterPdfQuery.cs` (le dictionnaire `students`)
- Modify: `src/SamaEcole.Application/Finance/Queries/GetPaymentReceipt/GetPaymentReceiptQuery.cs` (les jointures)
- Test: `tests/SamaEcole.IntegrationTests/Finance/DeletedStudentFinancialHistoryTests.cs`

**Interfaces:**
- Consumes: `ITenantProvider.CurrentSchoolId`, `IgnoreQueryFilters()`.
- Produces: un nom d'élève affiché `« Nom (élève supprimé) »` dans les états ; aucune exception.

- [ ] **Step 1: Write the failing test**

Reprendre le jeu de données de `CancellationReportsTests` (session de lundi clôturée, paiement de lundi annulé mardi), puis **archiver l'inscription et l'élève** (`SoftDelete`) avant de générer les états :

```csharp
[Fact]
public async Task The_Closing_Report_And_The_Receipt_Still_Work_After_The_Student_Is_Deleted()
{
    await SeedMondayPaymentCancelledOnTuesdayAsync();     // copie de CancellationReportsTests
    await ArchiveStudentAndEnrollmentAsync();             // Enrollment.SoftDelete + Student.SoftDelete via le contexte propriétaire

    var report = await BuildClosingReportAsync(SessionLundi);   // CapturingGenerator, comme CancellationReportsTests
    report.Transactions.Should().ContainSingle().Which.StudentName.Should().Contain("Awa Fall").And.Contain("(élève supprimé)");

    var receipt = await ReceiptAsync(PaiementLundi);
    receipt.StudentFullName.Should().Contain("Awa Fall");
    receipt.CancellationNotice.Should().NotBeNull();
}

[Fact]
public async Task Another_School_Cannot_Read_The_Deleted_Students_Receipt()
{
    await SeedMondayPaymentCancelledOnTuesdayAsync();
    await ArchiveStudentAndEnrollmentAsync();

    var act = async () => await ReceiptAsync(PaiementLundi, school: AutreEcole);

    await act.Should().ThrowAsync<KeyNotFoundException>("IgnoreQueryFilters retire aussi le filtre de tenant : il est rétabli à la main");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~DeletedStudentFinancialHistoryTests"`
Expected: FAIL — `NullReferenceException` (rapport) / `KeyNotFoundException` (reçu).

- [ ] **Step 3: Write minimal implementation**

Dans chaque requête, remplacer les accès filtrés par un accès `IgnoreQueryFilters()` **borné au tenant** :

```csharp
// Rapport de clôture : ne plus utiliser Include(p => p.Enrollment) (filtré) ; résoudre inscription et élève à part.
var schoolId = tenantProvider.CurrentSchoolId ?? throw new UnauthorizedAccessException();

var enrollmentStudents = await (
    from e in dbContext.Enrollments.IgnoreQueryFilters()
    join s in dbContext.Students.IgnoreQueryFilters() on e.StudentId equals s.Id
    where e.SchoolId == schoolId && s.SchoolId == schoolId && enrollmentIds.Contains(e.Id)
    select new { EnrollmentId = e.Id, s.FullName, s.IsDeleted })
    .ToDictionaryAsync(x => x.EnrollmentId, cancellationToken);

string StudentOf(Guid enrollmentId) =>
    enrollmentStudents.TryGetValue(enrollmentId, out var s)
        ? (s.IsDeleted ? $"{s.FullName} (élève supprimé)" : s.FullName)
        : "Inconnu";
```

Appliquer la même logique à `GetDailyCashRegisterPdfQuery` (matricule et nom) et à `GetPaymentReceiptQuery` (jointures `Enrollments`, `Students` en `IgnoreQueryFilters()` avec `SchoolId == schoolId`; `Classrooms` et `SchoolYears` aussi si archivés). Rappel : le `SchoolId` explicite est **obligatoire** — `IgnoreQueryFilters()` retire le filtre de tenant, seule la RLS PostgreSQL protège ensuite.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~DeletedStudentFinancialHistoryTests|FullyQualifiedName~CancellationReportsTests|FullyQualifiedName~PaymentLedgerTests"`
Expected: PASS (non-régression incluse).

- [ ] **Step 5: Commit**

```bash
git add src/SamaEcole.Application/Finance tests/SamaEcole.IntegrationTests/Finance/DeletedStudentFinancialHistoryTests.cs
git commit -m "fix(finance): l'historique financier d'un élève supprimé reste lisible (journaux et reçu)"
```

---

### Task 8: Écran « Suppression guidée » (composant partagé) et branchement sur les Élèves

**Files:**
- Create: `src/SamaEcole.Web/wwwroot/js/guided-delete.js`
- Create: `src/SamaEcole.Web/Views/Shared/_GuidedDeleteModal.cshtml`
- Modify: `src/SamaEcole.Web/wwwroot/js/students.js` (l'état `deletingStudentRecord…` ligne 247-248 et les méthodes `openDeleteStudent` / `closeDeleteStudent` / `confirmDeleteStudent`, lignes 887-930)
- Modify: `src/SamaEcole.Web/Views/Students/Index.cshtml` (la modale « Supprimer la fiche », lignes 1097-1117, et le bloc `@section Scripts`)

**Interfaces:**
- Produces (réutilisé par les PR2/PR3) : `window.guidedDelete.mixin({ resource: 'students', subject: 'cet élève' })` renvoyant l'état `guidedDelete` (`null` ou `{ id, name, rowVersion, phase, impact, error, busy }`) et les méthodes `openGuidedDelete({ id, name, rowVersion })`, `closeGuidedDelete()`, `confirmGuidedDelete()` ; le composant hôte doit fournir `async afterGuidedDelete()`. Phases : `loading`, `financial` (blocage), `links` (liens à rompre), `ready` (aucun lien, confirmation simple), `done`.

- [ ] **Step 1: Write `guided-delete.js`**

```js
/**
 * Suppression guidée (spec 2026-10-17, §5) — logique PARTAGÉE par tous les écrans qui suppriment un élément.
 * Modal : Views/Shared/_GuidedDeleteModal.cshtml. Trois appels distincts, jamais fusionnés côté serveur :
 *   GET  /{resource}/{id}/deletion-impact         → analyse (lecture seule)
 *   POST /{resource}/{id}/unlink-dependencies      → rupture explicite des liens non financiers
 *   DELETE /{resource}/{id}?rowVersion=            → suppression (ne rompt jamais rien)
 * Usage : Alpine.data('x', () => ({ ...window.guidedDelete.mixin({ resource: 'students', subject: 'cet élève' }),
 *                                    async afterGuidedDelete() { … } }))
 */
(function () {
    window.guidedDelete = {
        mixin(config) {
            const { resource } = config;

            return {
                guidedDelete: null, // { id, name, rowVersion, phase, impact, confirmed, error, busy }
                guidedDeleteSubject: config.subject,

                async openGuidedDelete({ id, name, rowVersion }) {
                    this.guidedDelete = { id, name, rowVersion, phase: 'loading', impact: null, confirmed: false, error: null, busy: false };
                    await this.refreshGuidedDeleteImpact();
                },

                closeGuidedDelete() {
                    this.guidedDelete = null;
                },

                /** Relit l'analyse et choisit la phase d'affichage. */
                async refreshGuidedDeleteImpact() {
                    const state = this.guidedDelete;
                    if (!state) return;
                    try {
                        const impact = await window.api.get(`/${resource}/${state.id}/deletion-impact`);
                        state.impact = impact;
                        state.phase = impact.financialBlock ? 'financial' : impact.canDelete ? 'ready' : 'links';
                        state.error = null;
                    } catch (err) {
                        state.phase = 'links';
                        state.error = (err && err.message) || "Impossible d'analyser les liens.";
                    }
                },

                /** Rupture des liens PUIS suppression : deux appels, après confirmation cochée par l'utilisateur. */
                async confirmGuidedDelete() {
                    const state = this.guidedDelete;
                    if (!state || state.busy) return;
                    if (state.phase === 'links' && !state.confirmed) {
                        state.error = 'Cochez la case pour confirmer la rupture de ces liens.';
                        return;
                    }

                    state.busy = true;
                    state.error = null;
                    try {
                        if (state.phase === 'links') {
                            await window.api.post(`/${resource}/${state.id}/unlink-dependencies`, {});
                        }
                        await window.api.delete(`/${resource}/${state.id}?rowVersion=${state.rowVersion}`);
                        this.guidedDelete = null;
                        await this.afterGuidedDelete();
                    } catch (err) {
                        // Un lien est apparu entre-temps, ou un paiement : on réaffiche l'analyse à jour, message serveur inclus.
                        state.error = (err && err.message) || 'Erreur lors de la suppression.';
                        if (err && err.code === 'CONCURRENCY_CONFLICT') {
                            state.error = 'Cette fiche vient d\'être modifiée par un autre utilisateur. Rafraîchissez la page puis réessayez.';
                        } else {
                            state.confirmed = false;
                            await this.refreshGuidedDeleteImpact();
                            state.error = (err && err.message) || state.error;
                        }
                    } finally {
                        if (this.guidedDelete) this.guidedDelete.busy = false;
                    }
                }
            };
        }
    };
})();
```

- [ ] **Step 2: Write `_GuidedDeleteModal.cshtml`**

Même structure que `_CancelPaymentModal.cshtml` (`modal-shell`, `modal-title`, `btn-modal-*`). Contenu selon `guidedDelete.phase` : `loading` (texte « Analyse… ») ; `financial` (bandeau rouge avec `guidedDelete.impact.message`, aucun bouton de suppression, texte « Annulez d'abord le paiement depuis l'onglet Paiements ») ; `links` (message du serveur, liste `<ul>` des `impact.links` avec `label` et `breakAction`, case à cocher `guidedDelete.confirmed`, bouton « Rompre les liens et supprimer », désactivé tant que des liens ne sont pas rompables : `impact.links.some(l => !l.breakable)` → afficher le message et masquer le bouton) ; `ready` (confirmation simple : « {nom} sera supprimé(e) et n'apparaîtra plus nulle part ; son matricule redeviendra disponible. Une trace de la suppression est conservée. »). Bouton « Retour » toujours présent.

Squelette exact :

```html
<modal-shell open="guidedDelete" on-close="closeGuidedDelete()" size="md">
 <modal-title>Supprimer</modal-title>
 <modal-subtitle><span class="font-semibold text-slate-900" x-text="guidedDelete ? guidedDelete.name : ''"></span></modal-subtitle>

 <div x-show="guidedDelete && guidedDelete.error" x-cloak class="form-error-banner" x-text="guidedDelete ? guidedDelete.error : ''"></div>

 <p x-show="guidedDelete && guidedDelete.phase === 'loading'" class="text-base text-slate-700">Analyse des liens…</p>

 <div x-show="guidedDelete && guidedDelete.phase === 'financial'" x-cloak class="rounded-xl border-l-4 border-rose-500 bg-rose-50 p-4 text-sm text-rose-800">
  <p class="font-semibold" x-text="guidedDelete && guidedDelete.impact ? guidedDelete.impact.message : ''"></p>
  <p class="mt-2">Annulez d'abord le ou les paiements depuis l'onglet « Paiements » de la fiche : la suppression sera alors possible.</p>
 </div>

 <div x-show="guidedDelete && guidedDelete.phase === 'links'" x-cloak class="space-y-3">
  <p class="text-base font-medium leading-relaxed text-slate-700" x-text="guidedDelete && guidedDelete.impact ? guidedDelete.impact.message : ''"></p>
  <ul class="list-disc pl-5 text-sm text-slate-700">
   <template x-for="link in (guidedDelete && guidedDelete.impact ? guidedDelete.impact.links : [])" :key="link.kind">
    <li><span class="font-semibold" x-text="link.label"></span>
     <span class="text-slate-500" x-show="link.breakable" x-text="' — ' + link.breakAction"></span>
     <span class="text-rose-600" x-show="!link.breakable"> — à traiter avant (non rompable ici)</span></li>
   </template>
  </ul>
  <label class="flex items-start gap-2 text-sm text-slate-700" x-show="guidedDelete && guidedDelete.impact && guidedDelete.impact.links.every(l => l.breakable)">
   <input type="checkbox" class="mt-0.5" x-model="guidedDelete.confirmed" />
   <span>Je comprends que ces liens seront rompus (archivés, avec une trace) avant la suppression.</span>
  </label>
 </div>

 <p x-show="guidedDelete && guidedDelete.phase === 'ready'" x-cloak class="text-base font-medium leading-relaxed text-slate-700">
  Cette fiche sera supprimée et n'apparaîtra plus nulle part ; son matricule redeviendra disponible. Une trace de la suppression est conservée.
 </p>

 <div class="bg-white border-t border-slate-100 p-4 px-6 flex justify-end gap-3 -mx-6 -mb-6 mt-6 z-10">
  <button type="button" @@click="closeGuidedDelete()" class="btn-modal-secondary group">Retour</button>
  <button type="button" @@click="confirmGuidedDelete()" class="btn-modal-danger"
    x-show="guidedDelete && (guidedDelete.phase === 'ready' || (guidedDelete.phase === 'links' && guidedDelete.impact && guidedDelete.impact.links.every(l => l.breakable)))"
    :disabled="guidedDelete && guidedDelete.busy">
   <span x-text="guidedDelete && guidedDelete.busy ? 'Suppression...' : (guidedDelete && guidedDelete.phase === 'links' ? 'Rompre les liens et supprimer' : 'Supprimer')"></span>
  </button>
 </div>
</modal-shell>
```

- [ ] **Step 3: Brancher l'écran Élèves**

Dans `students.js` : supprimer `deletingStudentRecord`, `isDeletingStudentRecord`, `deleteStudentRecordError` et les trois méthodes, ajouter `...window.guidedDelete.mixin({ resource: 'students', subject: 'cet élève' }),` à côté de `...window.paymentCancel.mixin(),`, remplacer `openDeleteStudent()` par :

```js
        openDeleteStudent() {
            if (!this.studentDetail || !this.detailStudent) return;
            this.openGuidedDelete({
                id: this.detailStudent.id,
                name: this.studentDetail.identity.fullName,
                rowVersion: this.studentDetail.identity.rowVersion
            });
        },

        /** Appelé par guided-delete.js une fois l'élève supprimé : fermer la fiche, relire la liste, annoncer. */
        async afterGuidedDelete() {
            this.addedStudentName = '';
            this.closeDetail();
            this.page = 1;
            await this.loadStudents();
            this.showStudentDeletedDialog = true;
        },
```

Dans `Index.cshtml` : remplacer la modale `deletingStudentRecord` (lignes 1097-1117) par `<partial name="_GuidedDeleteModal" />` et ajouter `<script src="~/js/guided-delete.js" asp-append-version="true"></script>` avant `students.js`. Vérifier qu'aucun autre fichier ne référence `deletingStudentRecord` : `grep -rn "deletingStudentRecord\|deleteStudentRecordError" src/SamaEcole.Web`.

- [ ] **Step 4: Verify**

Run: `dotnet build src/SamaEcole.Web` puis `dotnet test tests/SamaEcole.FunctionalTests --filter "FullyQualifiedName~PagesRenderTests|FullyQualifiedName~ModalShellRenderTests"`
Expected: build 0 erreur, rendu des pages OK. (La recette réelle des écrans est la Task 9.)

- [ ] **Step 5: Commit**

```bash
git add src/SamaEcole.Web
git commit -m "feat(ui): suppression guidée des élèves (composant partagé, liens à rompre, blocage financier)"
```

---

### Task 9: Recette navigateur, documentation et clôture

**Files:**
- Modify: `openapi.yaml` (nouvelles routes et codes 409)
- Modify: `docs/superpowers/specs/2026-10-17-deletion-dependency-guard-design.md` (§9 : PR1 livrée ; §3 : pas de `rowVersion` sur la rupture)
- Modify: `AGENTS.md` / `ACTIVE_CONTEXT.md` si le périmètre « livré » y est tenu à jour (vérifier la section concernée)

- [ ] **Step 1: Documenter les routes**

Dans `openapi.yaml`, sous `/students/{id}` : documenter `delete` (204 ; 409 `FINANCIAL_RECORDS_EXIST` / `DEPENDENCIES_EXIST` avec `details` = liste de `DeletionLink`), `/students/{id}/deletion-impact` (get, schéma `DeletionImpact`) et `/students/{id}/unlink-dependencies` (post, 200 `DeletionImpact`, 409 `FINANCIAL_RECORDS_EXIST` / `DEPENDENCIES_NOT_BREAKABLE`).

- [ ] **Step 2: Recette dans un vrai navigateur**

Procédure déjà éprouvée (mémoire `reference_recette_navigateur_locale`) : conteneurs `caisse-recette-pg` (5441) et `caisse-recette-redis` (6391), `dotnet run --project tools/SamaEcole.Tools -- migrate`, CSS Tailwind via `NODE_PATH` d'un autre worktree, `dotnet run --project src/SamaEcole.Web --no-launch-profile`, Playwright via `playwright-core` de `UNIKOL-captures/_scripts`. Scénarios à jouer et à photographier :
1. Directeur → fiche d'un élève inscrit sans paiement → « Supprimer » : la modale liste « 1 inscription » (+ liens saisis), la case est obligatoire, « Rompre les liens et supprimer » fait disparaître l'élève de la liste.
2. Élève avec un paiement : la modale affiche le texte imposé, aucun bouton de suppression ; annuler le paiement (onglet Paiements) puis « Supprimer » → possible.
3. Élève sans aucun lien : confirmation simple.
4. Compte Finance : le bouton « Supprimer » n'apparaît pas (`canManageStudent`).
5. Après suppression, créer un élève : aucune erreur ; le matricule supprimé est libre côté base (test d'index déjà couvert).
Arrêter l'app, supprimer les conteneurs et `wwwroot/css/site.css` à la fin.

- [ ] **Step 3: Non-régression complète**

Run: `dotnet test tests/SamaEcole.UnitTests`, `dotnet test tests/SamaEcole.IntegrationTests`, `dotnet test tests/SamaEcole.FunctionalTests`
Expected: tout vert. Corriger toute régression avant de continuer.

- [ ] **Step 4: Commit et PR**

```bash
git add openapi.yaml docs
git commit -m "docs(deletion): routes de suppression guidée des élèves, statut de la spécification"
```

Pousser la branche `feat/guided-student-deletion` (empilée sur `feat/payment-cancellation` tant que la PR0 n'est pas fusionnée) et ouvrir la PR ; description : résumé, texte imposé, règle financière, liste des tests, points de vigilance (matricule : compteur sans trou, pas de réémission automatique).

---

## Self-Review

**Spec coverage** (spec 2026-10-17) : §2.1 trois actions → Tasks 3-5 ; §2.2 suppression logique et matricule réutilisable → Tasks 4-6 ; §2.3 blocage financier, paiement annulé libre → Task 3 ; §3 routes, codes, rôles → Task 5 ; §4 messages (texte imposé, énumération précise) → Tasks 1, 3 ; §5 interface → Task 8 ; §6 catalogue élève → Task 2 (la ligne « Rattachement à une classe » est volontairement absente : `Student.ClassroomId` est obligatoire, il disparaît avec la fiche) ; §7 garanties (transaction, audit, tenant, garde-fou FK, quota/KPI) → Tasks 2, 4, 5, 7 ; §8 tests → chaque tâche. L'historique financier lisible après suppression (conséquence de la spec §2.4) → Task 7.

**Écarts assumés à faire valider** : (1) la rupture n'exige pas de `rowVersion` (elle ne modifie pas la fiche élève ; les inscriptions sont protégées par leur propre verrou xmin) ; (2) le matricule libéré n'est pas réémis automatiquement par le compteur (numérotation sans trou, règle #3) — il redevient seulement disponible en base ; (3) le matériel prêté non restitué est un lien non rompable.

**Placeholder scan** : aucun TODO. Les tests de la Task 4 et de la Task 7 s'appuient sur des gabarits de données déjà présents dans `CancelPaymentTests` / `CancellationReportsTests` (constantes, `InitializeAsync`, petites classes de test) qu'il faut recopier tels quels ; les noms d'entités d'inventaire utilisés sont ceux du domaine (`InventoryCategory`, `InventoryItem`, `ItemAssignment`).

**Type consistency** : `DeletionLink`, `DeletionImpact`, `DeletionErrorCodes`, `DeletionMessages` (Task 1) ; `StudentDependency`, `StudentDependencyCatalog.All/IgnoredDependents` (Task 2) ; `StudentDeletionAnalyzer.AnalyzeAsync` (Task 3) ; `UnlinkStudentDependenciesCommand` / `GetStudentDeletionImpactQuery` (Tasks 4-5) ; `window.guidedDelete.mixin` / `afterGuidedDelete` (Task 8) — noms identiques partout.
