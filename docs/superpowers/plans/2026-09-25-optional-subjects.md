# Matières optionnelles & dispenses — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal :** Un élève peut être dispensé d'une matière optionnelle (LV2, option scientifique) : elle disparaît de la saisie des notes, du bulletin et des moyennes, et le total des coefficients s'adapte. Sans dispense enregistrée, tout est strictement comme avant.

**Architecture :** `Subject` gagne `IsOptional` + `OptionGroup`. Une table tenant `enrollment_subject_exemptions` (RLS + Global Query Filter) porte les **dispenses** d'une inscription (« aucune ligne » = « suit tout »). Une classe statique `SubjectExemptions` (requêtes, sans état) est appelée par les six lecteurs de la spécification §4.2 ; une règle pure `OptionSelectionRules` valide un choix (au plus une matière par groupe) et en déduit les dispenses. Le choix s'écrit soit à l'inscription (`optionSubjectIds`, même transaction), soit par `PUT /api/v1/enrollments/{id}/options`.

**Tech Stack :** ASP.NET Core 9, EF Core/Npgsql (RLS, Global Query Filter), MediatR + FluentValidation, Alpine.js, xUnit + FluentAssertions (Testcontainers Postgres), `node --test`.

**Spec :** `docs/superpowers/specs/2026-09-25-optional-subjects-design.md` (commit `bfedc99`). Ce plan la **précise** sur quatre points — voir « Écarts assumés par rapport à la spécification », à valider en même temps que le plan.

## Global Constraints

- PostgreSQL uniquement ; migration EF Core **nouvelle**, jamais de migration appliquée modifiée. — `AGENTS.md`.
- Toute nouvelle table tenant : `SchoolId`, **Global Query Filter + policy RLS** (les deux), `GRANT SELECT, INSERT, UPDATE` (jamais `DELETE` : suppression logique), **et** ajout à `reset_school_data` + `delete_school_year` par une migration à part. Modèle : `20260924213235_AddSubjectCoefficientOverrides.cs` et `20260924213342_AddSubjectCoefficientOverridesToPurges.cs`.
- Aucune suppression physique (`IsDeleted`, `DeletedAt`, `DeletedBy` via `SoftDelete(actor)`). — règle #6.
- CQRS MediatR ; aucune logique métier dans un contrôleur ni une entité. Erreurs au format normalisé (`ValidationException` → 422, `KeyNotFoundException` → 404). — règles #7 à #9.
- `schoolId` jamais lu depuis un paramètre client (JWT / `ITenantProvider`). — règle #10.
- **Invariant :** sans dispense, le résultat de chaque lecteur est strictement celui d'avant. Les constructeurs des handlers existants **ne changent pas** (aucun paramètre ajouté) : les tests existants restent compilables tels quels.
- Le bulletin ne change pas de mise en page (règle #12) : seules les lignes affichées changent.
- Pas de `SubjectExemptionLoader` scopé ni de cache : la classe statique interroge la base à chaque appel (voir écart E1).
- Le propriétaire lance lui-même la suite complète `dotnet test` (consigne du 17/09/2026) ; ce plan n'exécute que des tests **ciblés** (`--filter`). `dotnet build` et `node --test` restent libres.
- Les tests d'intégration exigent Docker (Testcontainers, `RlsTestDatabase`). Base de développement : conteneur `sama-ecole-postgres`, base `sama_ecole_dev`, utilisateur `sama_ecole`.
- Après génération d'une migration : appliquer `dotnet ef database update` sur la base de dev **avant** de relancer l'app (sinon « Une erreur inattendue » partout). Les colonnes ajoutées ont un défaut : l'ancien code continue de fonctionner sur cette base.
- Code de production et tests de Notes/isolation tenant : jamais l'un sans l'autre (`AGENTS.md`).
- Conventional Commits, un commit par tâche, terminé par `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`. Staging **explicite** (jamais `git add .`). Branche : `feature/optional-subjects`, worktree `C:\Users\NCM\Documents\antigravity\sama_ecole-optional-subjects` (toutes les commandes s'y exécutent).

## Écarts assumés par rapport à la spécification (à valider avec le plan)

| # | Spécification | Plan | Pourquoi |
|---|---|---|---|
| E1 | §4.1 : `SubjectExemptionLoader` (service scopé, paramètre de constructeur) | Classe **statique** `SubjectExemptions` (deux méthodes) | Ajouter un paramètre de constructeur aux 8 handlers concernés casserait ~30 constructions directes dans les tests existants. Le loader des coefficients ne mémoïse qu'à l'intérieur d'un même (élève, année) : le gain est nul d'un élève à l'autre. |
| E2 | §4.2 : import — ligne d'un élève dispensé « signalée comme ignorée » | **Erreur de validation** (422) sur la ligne, avec un message dédié | Ignorer silencieusement une note d'un fichier serait pire que refuser : l'utilisateur ne saurait pas qu'elle est perdue. |
| E3 | §3.1 : `IsOptional` autonome seulement | Idem, **et** une dispense ne joue que si la matière est **encore optionnelle** (`Subject.IsOptional`) au moment de la lecture | Repasser une matière en « obligatoire » doit la rendre immédiatement à tous, sans avoir à purger des lignes. |
| E4 | §5.2 / §10.4 : niveau des options tiré de la classe de l'inscription | Niveau tiré de la classe **courante de l'élève** (`Student.ClassroomId`) ; `PUT` refusé (422) si l'inscription est annulée ou n'est pas celle de l'année **active** | `UpdateStudentCommand` change `Student.ClassroomId` sans toucher l'inscription : c'est la classe courante que voient les feuilles de notes. Les dispenses restent rattachées à l'inscription ; celles d'un autre niveau sont supprimées logiquement au prochain `PUT`. |

Constat correctif sur la spécification §1 : le bulletin **secondaire** n'imprime aujourd'hui que les matières ayant une note (`GetGradeSummaryQueryHandler` part des lignes de `Grades`) ; l'effet visible est donc la **feuille de saisie** (l'élève n'y figure plus), le **masquage des notes d'une option abandonnée** et la grille **APC** (`EvaluationStructureBuilder` imprime toutes les lignes du niveau, notées ou non). Le §1 est corrigé dans la Tâche 9.

## Constat sur le code (à connaître avant de commencer)

- `Subject` : niveau en **texte libre** (`Level`), `OptionGroup` sera lui aussi un texte libre comparé sans casse ni espaces de bord (`OptionSelectionRules.LevelMatches`).
- Lecteurs de la liste d'élèves d'une classe pour la saisie : `Student.ClassroomId` partout (`GetClassGrades`, `GetGradeSheetPdf`, `GetGradeSheetExcel`, `ImportGradeSheet`).
- L'**année** d'un trimestre : `Term.SchoolYearId`. `GetClassGrades`, `GetGradeSheetExcel`, `ImportGradeSheet` et `CreateGrade` ne chargent aujourd'hui le trimestre que par `AnyAsync` : ils doivent en lire l'année.
- `UpdateSubjectCommand` est un **PUT complet** : un client qui omet `isOptional`/`optionGroup` les remet à `false`/`null`. `subjects.js` → `saveSubject` (réordonner, entêtes) renvoie déjà tous les champs : il faut y ajouter les deux nouveaux, sous peine de perdre le réglage à chaque réordonnancement (piège documenté au commentaire de `saveSubject`).
- `ReportCardDataService` (14 constructions dans les tests) a `dbContext` : il appelle la classe statique, sans nouveau paramètre.
- Le bulletin d'un élève dispensé : le rang par matière (`GetReportCardPdfQuery` l. ~259) lit `cs.Subjects.FirstOrDefault(...)?.Average` des camarades — un camarade dispensé donne `null` et sort du classement de cette matière, ce qui est voulu.

## File Structure

| Fichier | Rôle |
|---|---|
| `src/SamaEcole.Domain/Entities/Subject.cs` (modifier) | `IsOptional`, `OptionGroup`. |
| `src/SamaEcole.Domain/Entities/EnrollmentSubjectExemption.cs` (créer) | Dispense : (inscription, matière). |
| `src/SamaEcole.Persistence/Configurations/EnrollmentSubjectExemptionConfiguration.cs` (créer), `SubjectConfiguration.cs` (modifier), `ApplicationDbContext.cs` (modifier), `Migrations/*` | Table, colonnes, RLS, purges. |
| `src/SamaEcole.Application/Common/Interfaces/IApplicationDbContext.cs` (modifier) | `DbSet<EnrollmentSubjectExemption>`. |
| `src/SamaEcole.Application/OptionalSubjects/OptionSelectionRules.cs` (créer) | Règle pure : validation d'un choix, dispenses déduites. |
| `src/SamaEcole.Application/OptionalSubjects/SubjectExemptions.cs` (créer) | Requêtes statiques lues par les six lecteurs. |
| `src/SamaEcole.Application/OptionalSubjects/EnrollmentOptionsPlanner.cs` (créer) | Options d'un niveau + plan de dispenses (partagé Create/Set). |
| `src/SamaEcole.Application/Enrollments/Commands/SetEnrollmentOptions/*`, `Queries/GetEnrollmentOptions/*` (créer) | Écriture et lecture du choix. |
| `src/SamaEcole.Application/Subjects/*`, `Grades/*`, `ReportCards/*`, `Students/*`, `Enrollments/Commands/CreateEnrollment/*` (modifier) | Drapeau sur la matière ; lecteurs filtrés ; choix à l'inscription. |
| `src/SamaEcole.Web/Controllers/{Subjects,Enrollments}Controller.cs` (modifier) | Champs de la matière ; routes `options`. |
| `src/SamaEcole.Web/wwwroot/js/subject-options.js` (créer) | Logique pure des groupes/choix (testable sous `node --test`). |
| `src/SamaEcole.Web/wwwroot/js/{subjects,enrollments,students,help}.js`, `Views/{Subjects,Enrollments,Students}/Index.cshtml` (modifier) | Écrans. |
| `tests/SamaEcole.IntegrationTests/OptionalSubjects/*`, `tests/SamaEcole.UnitTests/OptionalSubjects/*`, `src/SamaEcole.Web/tests/js/*` (créer) | Tests. |

---

### Task 1: Modèle de données, migrations et isolation

**Files:**
- Modify: `src/SamaEcole.Domain/Entities/Subject.cs` (après `SectionType`, l. ~94)
- Create: `src/SamaEcole.Domain/Entities/EnrollmentSubjectExemption.cs`
- Create: `src/SamaEcole.Persistence/Configurations/EnrollmentSubjectExemptionConfiguration.cs`
- Modify: `src/SamaEcole.Persistence/Configurations/SubjectConfiguration.cs` (après la ligne `Column2Header`, l. ~52)
- Modify: `src/SamaEcole.Persistence/ApplicationDbContext.cs` (l. 59), `src/SamaEcole.Application/Common/Interfaces/IApplicationDbContext.cs` (l. 126)
- Create: `src/SamaEcole.Persistence/Migrations/<horodatage>_AddOptionalSubjects.cs` (+ `.Designer.cs`), `<horodatage>_AddOptionalSubjectsToPurges.cs` (+ `.Designer.cs`)
- Modify: `tests/SamaEcole.FunctionalTests/Common/AuthApiFactory.cs` (l. ~458)
- Test: `tests/SamaEcole.IntegrationTests/OptionalSubjects/EnrollmentExemptionIsolationTests.cs`

**Interfaces:**
- Produces: `Subject.IsOptional : bool`, `Subject.OptionGroup : string?` ; `EnrollmentSubjectExemption { Guid Id, Guid SchoolId, Guid EnrollmentId, Guid SubjectId }` (+ `AuditableEntity`) ; `IApplicationDbContext.EnrollmentSubjectExemptions : DbSet<EnrollmentSubjectExemption>` ; table `enrollment_subject_exemptions`.

- [ ] **Step 1: Écrire le test d'isolation (SQL brut, rôle applicatif)**

Créer `tests/SamaEcole.IntegrationTests/OptionalSubjects/EnrollmentExemptionIsolationTests.cs` :

```csharp
using FluentAssertions;
using Npgsql;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.OptionalSubjects;

/// <summary>
/// Matières optionnelles — la table <c>enrollment_subject_exemptions</c> tient-elle son isolation, son
/// unicité et ses purges DANS LA BASE ? SQL BRUT sous le rôle applicatif, sans EF Core : seuls la
/// policy RLS, les index et les fonctions de purge font foi (même méthode que
/// <c>CoefficientOverrideIsolationTests</c>).
/// </summary>
[Trait("Category", "MultiTenant")]
public class EnrollmentExemptionIsolationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("91111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("92222222-2222-2222-2222-222222222222");

    private static readonly Guid AnneeA1 = Guid.Parse("91111111-0000-0000-0000-000000000001");
    private static readonly Guid AnneeA2 = Guid.Parse("91111111-0000-0000-0000-000000000002");
    private static readonly Guid AnneeB = Guid.Parse("92222222-0000-0000-0000-000000000001");

    private static readonly Guid ClasseA = Guid.Parse("9aaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid ClasseB = Guid.Parse("9bbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid EleveA = Guid.Parse("9eeeeeee-0000-0000-0000-0000000000a1");
    private static readonly Guid EleveB = Guid.Parse("9eeeeeee-0000-0000-0000-0000000000b1");

    private static readonly Guid InscriptionA1 = Guid.Parse("9fffffff-0000-0000-0000-0000000000a1");
    private static readonly Guid InscriptionA2 = Guid.Parse("9fffffff-0000-0000-0000-0000000000a2");
    private static readonly Guid InscriptionB = Guid.Parse("9fffffff-0000-0000-0000-0000000000b1");

    private static readonly Guid ArabeA = Guid.Parse("9ccccccc-0000-0000-0000-0000000000a1");
    private static readonly Guid ArabeB = Guid.Parse("9ccccccc-0000-0000-0000-0000000000b1");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        owner.SchoolYears.AddRange(
            new SchoolYear { Id = AnneeA1, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true },
            new SchoolYear { Id = AnneeA2, SchoolId = EcoleA, Label = "2025-2026", StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) },
            new SchoolYear { Id = AnneeB, SchoolId = EcoleB, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true });

        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 });

        owner.Subjects.AddRange(
            new Subject { Id = ArabeA, SchoolId = EcoleA, Name = "Arabe", Level = "Collège", Coefficient = 2, IsOptional = true, OptionGroup = "LV2" },
            new Subject { Id = ArabeB, SchoolId = EcoleB, Name = "Arabe", Level = "Collège", Coefficient = 2, IsOptional = true, OptionGroup = "LV2" });

        owner.Students.AddRange(
            NewStudent(EleveA, EcoleA, "ELEV-A1", "Awa A", ClasseA),
            NewStudent(EleveB, EcoleB, "ELEV-B1", "Awa B", ClasseB));

        owner.Enrollments.AddRange(
            NewEnrollment(InscriptionA1, EcoleA, EleveA, AnneeA1, ClasseA, "R-A1"),
            NewEnrollment(InscriptionA2, EcoleA, EleveA, AnneeA2, ClasseA, "R-A2"),
            NewEnrollment(InscriptionB, EcoleB, EleveB, AnneeB, ClasseB, "R-B1"));

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // 1 — la RLS masque les dispenses des autres écoles.
    [Fact]
    public async Task RawSqlQuery_Should_Never_Return_Other_School_Exemptions()
    {
        await InsertAsync(EcoleA, EcoleA, InscriptionA1, ArabeA);
        await InsertAsync(EcoleB, EcoleB, InscriptionB, ArabeB);

        (await CountAsync(EcoleA)).Should().Be(1);
        (await CountAsync(EcoleB)).Should().Be(1);
    }

    // 2 — sans tenant, rien n'est visible.
    [Fact]
    public async Task Session_Without_Tenant_Should_See_No_Exemption_At_All()
    {
        await InsertAsync(EcoleA, EcoleA, InscriptionA1, ArabeA);

        (await CountAsync(schoolId: null)).Should().Be(0);
    }

    // 3 — le WITH CHECK refuse d'écrire dans une autre école.
    [Fact]
    public async Task Writing_An_Exemption_Into_Another_School_Should_Be_Rejected()
    {
        var act = async () => await InsertAsync(sessionSchool: EcoleA, schoolId: EcoleB, InscriptionB, ArabeB);

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    // 4 — unicité par (inscription, matière) ; la suppression logique libère la clé.
    [Fact]
    public async Task An_Exemption_Is_Unique_Per_Enrollment_And_Subject_Until_It_Is_Soft_Deleted()
    {
        var first = await InsertAsync(EcoleA, EcoleA, InscriptionA1, ArabeA);

        var duplicate = async () => await InsertAsync(EcoleA, EcoleA, InscriptionA1, ArabeA);
        await duplicate.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.UniqueViolation);

        await SoftDeleteAsync(EcoleA, first);
        await InsertAsync(EcoleA, EcoleA, InscriptionA1, ArabeA); // recréation permise
    }

    // 5 — la purge d'une année (mode test) emporte les dispenses de ses inscriptions, et elles seules.
    [Fact]
    public async Task Deleting_A_School_Year_Removes_Its_Exemptions_And_Keeps_The_Other_Years()
    {
        await InsertAsync(EcoleA, EcoleA, InscriptionA1, ArabeA);
        await InsertAsync(EcoleA, EcoleA, InscriptionA2, ArabeA);

        await using (var connection = await _db.OpenRawAppConnectionAsync(EcoleA))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT count(*) FROM delete_school_year(@school, @year);";
            command.Parameters.AddWithValue("school", EcoleA);
            command.Parameters.AddWithValue("year", AnneeA2);
            await command.ExecuteScalarAsync();
        }

        (await CountAsync(EcoleA)).Should().Be(1);
    }

    // 6 — « Réinitialiser les données » emporte les dispenses AVANT inscriptions et matières (FK RESTRICT).
    [Fact]
    public async Task Resetting_The_School_Data_Removes_The_Exemptions_Without_A_Foreign_Key_Error()
    {
        await InsertAsync(EcoleA, EcoleA, InscriptionA1, ArabeA);

        await using (var connection = await _db.OpenRawAppConnectionAsync(EcoleA))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT count(*) FROM reset_school_data(@school);";
            command.Parameters.AddWithValue("school", EcoleA);
            await command.ExecuteScalarAsync();
        }

        (await CountAsync(EcoleA)).Should().Be(0);
    }

    private async Task<Guid> InsertAsync(Guid sessionSchool, Guid schoolId, Guid enrollmentId, Guid subjectId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(sessionSchool);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO enrollment_subject_exemptions
                ("Id", "SchoolId", "EnrollmentId", "SubjectId", "CreatedAt", "IsDeleted")
            VALUES (@id, @schoolId, @enrollmentId, @subjectId, NOW(), FALSE);
            """;
        var id = Guid.NewGuid();
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("schoolId", schoolId);
        command.Parameters.AddWithValue("enrollmentId", enrollmentId);
        command.Parameters.AddWithValue("subjectId", subjectId);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private async Task SoftDeleteAsync(Guid schoolId, Guid id)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = """UPDATE enrollment_subject_exemptions SET "IsDeleted" = TRUE, "DeletedAt" = NOW() WHERE "Id" = @id;""";
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> CountAsync(Guid? schoolId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT count(*) FROM enrollment_subject_exemptions WHERE NOT "IsDeleted";""";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static Student NewStudent(Guid id, Guid schoolId, string matricule, string name, Guid classroomId) => new()
    {
        Id = id, SchoolId = schoolId, Matricule = matricule, FullName = name,
        BirthDate = new DateOnly(2011, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = classroomId
    };

    private static Enrollment NewEnrollment(Guid id, Guid schoolId, Guid studentId, Guid yearId, Guid classroomId, string receipt) => new()
    {
        Id = id, SchoolId = schoolId, StudentId = studentId, SchoolYearId = yearId, ClassroomId = classroomId,
        Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, ReceiptNumber = receipt
    };
}
```

- [ ] **Step 2: Vérifier qu'il échoue**

Run: `dotnet build tests/SamaEcole.IntegrationTests`
Expected: FAIL — `Subject` ne contient pas `IsOptional`/`OptionGroup` (CS0117).

- [ ] **Step 3: Domaine**

Dans `Subject.cs`, après la propriété `SectionType` :

```csharp

    /// <summary>
    /// Matière AU CHOIX (LV2, option scientifique) : un élève peut en être DISPENSÉ par son inscription
    /// (<see cref="EnrollmentSubjectExemption"/>). Faux — la valeur de toutes les matières existantes —
    /// signifie « suivie par tous », exactement le comportement d'avant. Réservé aux matières autonomes
    /// (ni domaine parent, ni activité APC) : la validation Create/Update le tient.
    /// </summary>
    public bool IsOptional { get; set; }

    /// <summary>
    /// Groupe d'exclusion des options (« LV2 », « Option scientifique »), texte libre. Un élève suit AU
    /// PLUS une matière par groupe ; une option sans groupe est cumulable. Comparé sans tenir compte de la
    /// casse ni des espaces de bord. Sans effet si <see cref="IsOptional"/> est faux.
    /// </summary>
    public string? OptionGroup { get; set; }
```

Créer `EnrollmentSubjectExemption.cs` :

```csharp
using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Dispense d'un élève pour une matière optionnelle, portée par son INSCRIPTION (donc par l'année
/// scolaire). « Aucune ligne » signifie « l'élève suit toutes les matières » : c'est ce qui garantit que
/// rien ne change tant que personne n'a enregistré de choix. Une dispense ne joue que si la matière est
/// encore <see cref="Subject.IsOptional"/> au moment de la lecture (SubjectExemptions).
///
/// Suppression logique uniquement (règle #6) : « refaire son choix » retire les lignes en trop et la clé
/// redevient libre grâce à l'index unique partiel. Pas de verrou xmin : ce n'est pas une donnée sensible
/// au sens de la règle #5, et l'écriture est un remplacement idempotent.
/// </summary>
public class EnrollmentSubjectExemption : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid EnrollmentId { get; set; }

    public Guid SubjectId { get; set; }
}
```

- [ ] **Step 4: Persistance**

Dans `SubjectConfiguration.cs`, après `builder.Property(s => s.Column2Header).HasMaxLength(40);` :

```csharp
        // Pas de HasDefaultValue(false) : EF avertit qu'un défaut base sur un bool « écraserait » false à l'insertion.
        // La migration générée pose de toute façon `defaultValue: false` sur la colonne NOT NULL ajoutée.
        builder.Property(s => s.IsOptional).IsRequired();
        builder.Property(s => s.OptionGroup).HasMaxLength(50);
```

Créer `EnrollmentSubjectExemptionConfiguration.cs` :

```csharp
using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity. La policy RLS PostgreSQL
/// équivalente vit dans la migration AddOptionalSubjects (AGENTS.md règle #2).
/// </summary>
public class EnrollmentSubjectExemptionConfiguration : IEntityTypeConfiguration<EnrollmentSubjectExemption>
{
    public void Configure(EntityTypeBuilder<EnrollmentSubjectExemption> builder)
    {
        builder.ToTable("enrollment_subject_exemptions");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.SchoolId).IsRequired();

        // Une dispense par (inscription, matière). Index PARTIEL (« NOT IsDeleted ») — même convention que
        // SubjectCoefficientOverrideConfiguration : refaire son choix ne doit pas se heurter à la ligne archivée.
        builder.HasIndex(e => new { e.SchoolId, e.EnrollmentId, e.SubjectId })
            .IsUnique()
            .HasDatabaseName("UX_enrollment_subject_exemptions_key")
            .HasFilter("NOT \"IsDeleted\"");

        // « Quels élèves sont dispensés de cette matière cette année ? » (feuilles de notes).
        builder.HasIndex(e => new { e.SchoolId, e.SubjectId });

        // FK COMPOSITES tenant-safe : le croisement de tenants devient structurellement impossible.
        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(e => e.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Enrollment>()
            .WithMany()
            .HasForeignKey(e => new { e.SchoolId, e.EnrollmentId })
            .HasPrincipalKey(x => new { x.SchoolId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Subject>()
            .WithMany()
            .HasForeignKey(e => new { e.SchoolId, e.SubjectId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

Dans `ApplicationDbContext.cs`, après la ligne `SubjectCoefficientOverrides` :

```csharp
    public DbSet<EnrollmentSubjectExemption> EnrollmentSubjectExemptions => Set<EnrollmentSubjectExemption>();
```

Dans `IApplicationDbContext.cs`, après `SubjectCoefficientOverrides` :

```csharp

    /// <summary>Dispenses d'un élève pour les matières optionnelles, portées par son inscription.</summary>
    DbSet<EnrollmentSubjectExemption> EnrollmentSubjectExemptions { get; }
```

- [ ] **Step 5: Générer la migration de schéma et y ajouter la RLS**

Run:
```
dotnet ef migrations add AddOptionalSubjects -p src/SamaEcole.Persistence -s src/SamaEcole.Web
```
Expected: 2 fichiers `..._AddOptionalSubjects.cs` et `.Designer.cs`, plus le snapshot mis à jour. Le `Up` doit contenir `AddColumn IsOptional` (défaut `false`), `AddColumn OptionGroup` et `CreateTable enrollment_subject_exemptions`. Si le serveur de dev tourne et verrouille les DLL : ajouter `--configuration Release`.

Éditer la migration générée : ajouter en tête de la classe
```csharp
        private static readonly string[] TenantTables = ["enrollment_subject_exemptions"];

        private const string AppRole = "sama_ecole_app";
```
puis, **à la fin de `Up`** (après le dernier `CreateIndex`) — bloc identique à `AddSubjectCoefficientOverrides` :

```csharp
            // --- Isolation multi-tenant (AGENTS.md règle #2) ---
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($"ALTER TABLE \"{table}\" ENABLE ROW LEVEL SECURITY;");

                migrationBuilder.Sql($$"""
                    CREATE POLICY {{table}}_tenant_isolation ON "{{table}}"
                        USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                    """);

                // Aucun DELETE nulle part : le soft delete n'émet jamais de SQL DELETE (règle #6).
                migrationBuilder.Sql($$"""
                    DO $inner$
                    BEGIN
                        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{AppRole}}') THEN
                            EXECUTE 'GRANT SELECT, INSERT, UPDATE ON "{{table}}" TO {{AppRole}}';
                        ELSE
                            RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table {{table}}.', '{{AppRole}}';
                        END IF;
                    END
                    $inner$;
                    """);
            }
```
et **au début de `Down`** :
```csharp
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS {table}_tenant_isolation ON \"{table}\";");
            }
```
Ajouter au résumé XML de la classe : « Matières optionnelles & dispenses — colonnes `subjects.IsOptional/OptionGroup` et table tenant `enrollment_subject_exemptions`. Les policies RLS sont ajoutées à la main (EF ne les génère pas) ; les fonctions de purge sont corrigées par la migration SUIVANTE. »

- [ ] **Step 6: Migration des purges**

Run: `dotnet ef migrations add AddOptionalSubjectsToPurges -p src/SamaEcole.Persistence -s src/SamaEcole.Web` (les `Up`/`Down` générés sont vides). Remplacer le corps de la classe par celui de `20260924213342_AddSubjectCoefficientOverridesToPurges.cs`, avec ces trois différences — table `enrollment_subject_exemptions`, libellé `Dispenses de matières optionnelles`, et **ancres**. Les ancres ont été vérifiées uniques dans la base de dev le 25/09/2026 (`string_to_array` → 2 segments) :

- `reset_school_data` : ancre `[''enrollments'',` (la dispense référence `enrollments` ET `subjects` en RESTRICT ; `enrollments` figure **avant** `subjects` dans la liste — l'enfant doit précéder ses deux parents) :

```csharp
            migrationBuilder.Sql("""
                DO $patch$
                DECLARE
                    v_def    text;
                    v_anchor text := '[''enrollments'',';
                BEGIN
                    v_def := pg_get_functiondef('reset_school_data(uuid)'::regprocedure);

                    IF position('enrollment_subject_exemptions' IN v_def) > 0 THEN
                        RETURN;
                    END IF;

                    IF array_length(string_to_array(v_def, v_anchor), 1) <> 2 THEN
                        RAISE EXCEPTION 'reset_school_data : ancre % introuvable ou ambiguë — la fonction a changé de forme, corriger cette migration.', v_anchor;
                    END IF;

                    EXECUTE replace(v_def, v_anchor,
                        '[''enrollment_subject_exemptions'', ''Dispenses de matières optionnelles''],' || chr(10)
                        || '                        ' || v_anchor);
                END
                $patch$;
                """);
```

- `delete_school_year` : ancre `DELETE FROM enrollments`, étape insérée avant elle, limitée à l'année demandée :

```csharp
            migrationBuilder.Sql("""
                DO $patch$
                DECLARE
                    v_def    text;
                    v_anchor text := 'DELETE FROM enrollments';
                    v_step   text := $step$DELETE FROM enrollment_subject_exemptions x
                    USING enrollments e
                    WHERE x."EnrollmentId" = e."Id"
                      AND e."SchoolYearId" = p_school_year_id AND x."SchoolId" = p_school_id;
                    GET DIAGNOSTICS v_deleted = ROW_COUNT;
                    label := 'Dispenses de matières optionnelles'; rows_deleted := v_deleted; RETURN NEXT;

                    $step$;
                BEGIN
                    v_def := pg_get_functiondef('delete_school_year(uuid, uuid)'::regprocedure);

                    IF position('enrollment_subject_exemptions' IN v_def) > 0 THEN
                        RETURN;
                    END IF;

                    IF array_length(string_to_array(v_def, v_anchor), 1) <> 2 THEN
                        RAISE EXCEPTION 'delete_school_year : ancre % introuvable ou ambiguë — la fonction a changé de forme, corriger cette migration.', v_anchor;
                    END IF;

                    EXECUTE replace(v_def, v_anchor, v_step || v_anchor);
                END
                $patch$;
                """);
```

`Down` : retirer exactement ce qu'`Up` a inséré, comme dans le modèle (mêmes variables `v_insert` = `'[''enrollment_subject_exemptions'', ''Dispenses de matières optionnelles''],' || chr(10) || '                        '` et `v_step` identique à ci-dessus).

- [ ] **Step 7: Nettoyage des tests fonctionnels**

Dans `AuthApiFactory.cs`, juste avant `DELETE FROM subject_coefficient_overrides;` :

```csharp
        // Dispenses de matières optionnelles : FK Restrict vers enrollments ET subjects, donc AVANT les deux.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM enrollment_subject_exemptions;");
```
Vérifier la position : cette purge doit précéder la ligne qui supprime `enrollments` et celle qui supprime `subjects` dans le même fichier (`grep -n "DELETE FROM enrollments\|DELETE FROM subjects\|DELETE FROM enrollment_subject_exemptions" tests/SamaEcole.FunctionalTests/Common/AuthApiFactory.cs`). Si elle les suit, la déplacer avant la première.

- [ ] **Step 8: Appliquer sur la base de dev puis lancer les tests ciblés**

Run:
```
dotnet ef database update -p src/SamaEcole.Persistence -s src/SamaEcole.Web
dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~EnrollmentExemptionIsolationTests|FullyQualifiedName~RlsCoverageTests|FullyQualifiedName~ResetSchoolDataTests|FullyQualifiedName~DeleteSchoolYearTests"
```
Expected: PASS (6 tests d'isolation ; `RlsCoverageTests` et `Every_Restrict_Foreign_Key_Into_A_Purged_Table_Must_Come_From_A_Purged_Table_Too` détectent seuls la nouvelle table).

- [ ] **Step 9: Commit**

```bash
git add src/SamaEcole.Domain/Entities/Subject.cs src/SamaEcole.Domain/Entities/EnrollmentSubjectExemption.cs \
  src/SamaEcole.Persistence/Configurations/EnrollmentSubjectExemptionConfiguration.cs \
  src/SamaEcole.Persistence/Configurations/SubjectConfiguration.cs src/SamaEcole.Persistence/ApplicationDbContext.cs \
  src/SamaEcole.Application/Common/Interfaces/IApplicationDbContext.cs src/SamaEcole.Persistence/Migrations \
  tests/SamaEcole.FunctionalTests/Common/AuthApiFactory.cs tests/SamaEcole.IntegrationTests/OptionalSubjects
git commit -m "feat(options): table des dispenses de matières optionnelles (RLS, purges)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 2: Le drapeau « optionnelle » sur la matière

**Files:**
- Modify: `src/SamaEcole.Application/Subjects/Commands/CreateSubject/{CreateSubjectCommand,CreateSubjectCommandHandler,CreateSubjectCommandValidator}.cs`
- Modify: `src/SamaEcole.Application/Subjects/Commands/UpdateSubject/{UpdateSubjectCommand,UpdateSubjectCommandHandler,UpdateSubjectCommandValidator}.cs`
- Modify: `src/SamaEcole.Application/Subjects/Queries/GetSubjects/{GetSubjectsQuery,GetSubjectsQueryHandler}.cs`
- Modify: `src/SamaEcole.Web/Controllers/SubjectsController.cs`
- Create: `src/SamaEcole.Application/OptionalSubjects/OptionSelectionRules.cs` (partiel : `NormalizeGroup` — complété Tâche 3)
- Test: `tests/SamaEcole.UnitTests/Subjects/OptionalSubjectValidatorTests.cs`, `tests/SamaEcole.IntegrationTests/OptionalSubjects/SubjectOptionalFlagTests.cs`

**Interfaces:**
- Consumes: `Subject.IsOptional`, `Subject.OptionGroup` (Tâche 1).
- Produces: `CreateSubjectCommand.IsOptional : bool`, `.OptionGroup : string?` ; `UpdateSubjectCommand(..., string? NameAr = null, bool IsOptional = false, string? OptionGroup = null)` ; `SubjectDto`, `SubjectResult`, `UpdateSubjectResult` portent `IsOptional` et `OptionGroup` ; `OptionSelectionRules.NormalizeGroup(string?) : string?`.

- [ ] **Step 1: Écrire les tests du validateur (unitaires)**

Créer `tests/SamaEcole.UnitTests/Subjects/OptionalSubjectValidatorTests.cs` (la commande de base est construite ici, sans dépendre des gabarits d'un autre fichier) :

```csharp
using FluentAssertions;
using SamaEcole.Application.Subjects.Commands.CreateSubject;
using SamaEcole.Application.Subjects.Commands.UpdateSubject;
using Xunit;

namespace SamaEcole.UnitTests.Subjects;

public class OptionalSubjectValidatorTests
{
    private static CreateSubjectCommand Create(bool isOptional = false, string? group = null, Guid? parent = null) => new()
    {
        Name = "Arabe", Level = "Collège", Coefficient = 2,
        IsOptional = isOptional, OptionGroup = group, ParentSubjectId = parent
    };

    private static UpdateSubjectCommand Update(bool isOptional = false, string? group = null, Guid? parent = null) =>
        new(Guid.NewGuid(), "Arabe", "Collège", 2, 1u, parent, null, 0, null, null, null, isOptional, group);

    [Fact]
    public void A_Mandatory_Subject_Without_Group_Is_Valid_As_Before()
        => new CreateSubjectCommandValidator().Validate(Create()).IsValid.Should().BeTrue();

    [Fact]
    public void An_Optional_Subject_With_A_Group_Is_Valid()
        => new CreateSubjectCommandValidator().Validate(Create(isOptional: true, group: "LV2")).IsValid.Should().BeTrue();

    [Fact]
    public void A_Group_Without_The_Optional_Flag_Is_Refused()
    {
        var result = new CreateSubjectCommandValidator().Validate(Create(isOptional: false, group: "LV2"));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "OptionGroup");
    }

    [Fact]
    public void An_Optional_Subject_Cannot_Be_An_Activity_Of_A_Domain()
    {
        var result = new CreateSubjectCommandValidator().Validate(Create(isOptional: true, parent: Guid.NewGuid()));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "IsOptional");
    }

    [Fact]
    public void A_Group_Longer_Than_Fifty_Characters_Is_Refused()
    {
        var result = new CreateSubjectCommandValidator().Validate(Create(isOptional: true, group: new string('x', 51)));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "OptionGroup");
    }

    [Fact]
    public void The_Same_Rules_Apply_On_Update()
    {
        var validator = new UpdateSubjectCommandValidator();

        validator.Validate(Update(isOptional: true, group: "LV2")).IsValid.Should().BeTrue();
        validator.Validate(Update(isOptional: false, group: "LV2")).Errors.Should().ContainSingle(e => e.PropertyName == "OptionGroup");
        validator.Validate(Update(isOptional: true, parent: Guid.NewGuid())).Errors.Should().ContainSingle(e => e.PropertyName == "IsOptional");
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/SamaEcole.UnitTests --filter "FullyQualifiedName~OptionalSubjectValidatorTests"`
Expected: FAIL à la compilation (`IsOptional` absent de `CreateSubjectCommand`).

- [ ] **Step 3: Implémenter**

Créer `src/SamaEcole.Application/OptionalSubjects/OptionSelectionRules.cs` (version complétée à la Tâche 3) avec, pour l'instant :

```csharp
namespace SamaEcole.Application.OptionalSubjects;

/// <summary>
/// Règles PURES (aucun accès base) des matières optionnelles : normalisation d'un groupe, appariement de
/// niveau, validation d'un choix et dispenses qui en découlent (spécification §4.3, §5.2).
/// </summary>
public static class OptionSelectionRules
{
    /// <summary>Groupe nettoyé : blanc → null, espaces de bord retirés.</summary>
    public static string? NormalizeGroup(string? group) =>
        string.IsNullOrWhiteSpace(group) ? null : group.Trim();
}
```

`CreateSubjectCommand` : ajouter deux propriétés (mêmes commentaires de style que les voisines) :

```csharp
    /// <summary>Matière au choix : un élève peut en être dispensé (LV2, option scientifique). Faux par défaut.</summary>
    public bool IsOptional { get; init; }

    /// <summary>Groupe d'exclusion des options (« LV2 »). Significatif seulement si <see cref="IsOptional"/>.</summary>
    public string? OptionGroup { get; init; }
```
et étendre `SubjectResult` : `..., string? NameAr = null, bool IsOptional = false, string? OptionGroup = null);`

`CreateSubjectCommandHandler` : dans l'initialiseur de `Subject`, après `DisplayOrder` :
```csharp
            IsOptional = request.IsOptional,
            OptionGroup = request.IsOptional ? OptionSelectionRules.NormalizeGroup(request.OptionGroup) : null,
```
(ajouter `using SamaEcole.Application.OptionalSubjects;`), et étendre le `new SubjectResult(...)` : `..., subject.NameAr, subject.IsOptional, subject.OptionGroup)`.

`CreateSubjectCommandValidator` : ajouter à la fin du constructeur :

```csharp
        // Matières optionnelles : forme uniquement. « Ni domaine ni activité » — le refus d'une matière qui
        // PORTE déjà des activités dépend de la base, donc de UpdateSubjectCommandHandler.
        RuleFor(x => x.OptionGroup).MaximumLength(50).NoHtml();

        RuleFor(x => x.OptionGroup)
            .Must((command, group) => command.IsOptional || string.IsNullOrWhiteSpace(group))
            .WithMessage("Un groupe d'options n'a de sens que pour une matière optionnelle.");

        RuleFor(x => x.IsOptional)
            .Must((command, isOptional) => !isOptional || command.ParentSubjectId is null)
            .WithMessage("Une activité d'un domaine d'évaluation ne peut pas être une matière optionnelle.");
```

`UpdateSubjectCommand` : ajouter les deux paramètres **en dernier, avec défauts** (compatibilité source des appels existants) : `string? NameAr = null, bool IsOptional = false, string? OptionGroup = null)` ; étendre `UpdateSubjectResult` de la même façon.

`UpdateSubjectCommandValidator` : ajouter à la fin du constructeur (après `RuleFor(x => x.Column2Header)…`) les mêmes règles qu'à la création :

```csharp

        // Matières optionnelles : mêmes règles qu'à la création, à la lettre. Le refus d'un domaine qui porte
        // déjà des activités dépend de la base, donc de UpdateSubjectCommandHandler.
        RuleFor(x => x.OptionGroup).MaximumLength(50).NoHtml();

        RuleFor(x => x.OptionGroup)
            .Must((command, group) => command.IsOptional || string.IsNullOrWhiteSpace(group))
            .WithMessage("Un groupe d'options n'a de sens que pour une matière optionnelle.");

        RuleFor(x => x.IsOptional)
            .Must((command, isOptional) => !isOptional || command.ParentSubjectId is null)
            .WithMessage("Une activité d'un domaine d'évaluation ne peut pas être une matière optionnelle.");
```

`UpdateSubjectCommandHandler` : après le contrôle `EnsureCanBecomeChildAsync` et **avant** l'affectation des champs :

```csharp
        // Une matière qui PORTE des activités est un domaine d'évaluation : jamais optionnelle (le
        // validateur ne voit que la forme de la requête, pas la base).
        if (request.IsOptional && await dbContext.Subjects.AnyAsync(s => s.ParentSubjectId == subject.Id, cancellationToken))
        {
            throw new ValidationException([
                new FluentValidation.Results.ValidationFailure(nameof(request.IsOptional),
                    "Ce domaine d'évaluation porte des activités : il ne peut pas être une matière optionnelle.")
            ]);
        }
```
(ajouter `using SamaEcole.Application.Common.Exceptions;` — c'est la `ValidationException` maison, comme dans `SubjectHierarchyGuard`), puis après `subject.DisplayOrder = request.DisplayOrder;` :

```csharp
        subject.IsOptional = request.IsOptional;
        subject.OptionGroup = request.IsOptional ? OptionSelectionRules.NormalizeGroup(request.OptionGroup) : null;
```
et étendre le `new UpdateSubjectResult(...)` : `..., subject.NameAr, subject.IsOptional, subject.OptionGroup)`.

`GetSubjectsQuery.cs` : `SubjectDto(..., string? NameAr = null, bool IsOptional = false, string? OptionGroup = null)` ; `GetSubjectsQueryHandler` : `..., s.Column2Header, s.NameAr, s.IsOptional, s.OptionGroup))`.

`SubjectsController.cs` : `UpdateSubjectRequest(..., string? NameAr = null, bool IsOptional = false, string? OptionGroup = null)` et la construction de la commande : `..., request.Column1Header, request.Column2Header, request.NameAr, request.IsOptional, request.OptionGroup)`. `Create` reçoit `CreateSubjectCommand` tel quel : rien à changer.

- [ ] **Step 2 bis: Test d'intégration du handler (domaine parent)**

Créer `tests/SamaEcole.IntegrationTests/OptionalSubjects/SubjectOptionalFlagTests.cs` :

```csharp
using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Subjects.Commands.UpdateSubject;
using SamaEcole.Application.Subjects.Queries.GetSubjects;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.OptionalSubjects;

[Trait("Category", "MultiTenant")]
public class SubjectOptionalFlagTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("93333333-3333-3333-3333-333333333333");
    private static readonly Guid Domaine = Guid.Parse("93333333-0000-0000-0000-0000000000d1");
    private static readonly Guid Activite = Guid.Parse("93333333-0000-0000-0000-0000000000d2");
    private static readonly Guid Arabe = Guid.Parse("93333333-0000-0000-0000-0000000000d3");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = Ecole, Name = "École" });
        owner.Subjects.AddRange(
            new Subject { Id = Domaine, SchoolId = Ecole, Name = "Lang & Com.", Level = "CE1", Coefficient = 1 },
            new Subject { Id = Activite, SchoolId = Ecole, Name = "Vocabulaire", Level = "CE1", Coefficient = 1, ParentSubjectId = Domaine },
            new Subject { Id = Arabe, SchoolId = Ecole, Name = "Arabe", Level = "Collège", Coefficient = 2 });
        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task A_Subject_Can_Be_Flagged_Optional_With_A_Trimmed_Group_And_The_List_Returns_It()
    {
        await using var db = _db.NewAppContext(Ecole);
        var current = (await new GetSubjectsQueryHandler(db).Handle(new GetSubjectsQuery(), default)).Single(s => s.Id == Arabe);

        var result = await new UpdateSubjectCommandHandler(db).Handle(
            new UpdateSubjectCommand(Arabe, "Arabe", "Collège", 2, current.RowVersion,
                IsOptional: true, OptionGroup: "  LV2 "), default);

        result.IsOptional.Should().BeTrue();
        result.OptionGroup.Should().Be("LV2");

        await using var reread = _db.NewAppContext(Ecole);
        var dto = (await new GetSubjectsQueryHandler(reread).Handle(new GetSubjectsQuery(), default)).Single(s => s.Id == Arabe);
        dto.IsOptional.Should().BeTrue();
        dto.OptionGroup.Should().Be("LV2");
    }

    [Fact]
    public async Task Flagging_Off_Clears_The_Group()
    {
        await using var db = _db.NewAppContext(Ecole);
        var rv = (await new GetSubjectsQueryHandler(db).Handle(new GetSubjectsQuery(), default)).Single(s => s.Id == Arabe).RowVersion;
        var on = await new UpdateSubjectCommandHandler(db).Handle(
            new UpdateSubjectCommand(Arabe, "Arabe", "Collège", 2, rv, IsOptional: true, OptionGroup: "LV2"), default);

        await using var db2 = _db.NewAppContext(Ecole);
        var off = await new UpdateSubjectCommandHandler(db2).Handle(
            new UpdateSubjectCommand(Arabe, "Arabe", "Collège", 2, on.RowVersion, IsOptional: false, OptionGroup: "LV2"), default);

        off.IsOptional.Should().BeFalse();
        off.OptionGroup.Should().BeNull("le groupe n'a de sens que pour une option");
    }

    [Fact]
    public async Task A_Domain_That_Carries_Activities_Cannot_Become_Optional()
    {
        await using var db = _db.NewAppContext(Ecole);
        var rv = (await new GetSubjectsQueryHandler(db).Handle(new GetSubjectsQuery(), default)).Single(s => s.Id == Domaine).RowVersion;

        var act = () => new UpdateSubjectCommandHandler(db).Handle(
            new UpdateSubjectCommand(Domaine, "Lang & Com.", "CE1", 1, rv, IsOptional: true), default);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
```
Note : `UpdateSubjectCommandHandler` n'a que `dbContext` en constructeur (vérifié) ; `NewAppContext` ouvre un contexte lié au tenant.

- [ ] **Step 4: Lancer les tests**

Run:
```
dotnet test tests/SamaEcole.UnitTests --filter "FullyQualifiedName~OptionalSubjectValidatorTests|FullyQualifiedName~CreateSubjectCommandValidatorTests"
dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~SubjectOptionalFlagTests|FullyQualifiedName~Subjects"
```
Expected: PASS (les tests de matières existants restent verts : les nouveaux paramètres ont des défauts).

- [ ] **Step 5: Commit**

```bash
git add src/SamaEcole.Application/OptionalSubjects src/SamaEcole.Application/Subjects \
  src/SamaEcole.Web/Controllers/SubjectsController.cs tests/SamaEcole.UnitTests/Subjects/OptionalSubjectValidatorTests.cs \
  tests/SamaEcole.IntegrationTests/OptionalSubjects/SubjectOptionalFlagTests.cs
git commit -m "feat(options): drapeau « matière optionnelle » et groupe d'options

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 3: Règles pures et requêtes de dispenses

**Files:**
- Modify: `src/SamaEcole.Application/OptionalSubjects/OptionSelectionRules.cs`
- Create: `src/SamaEcole.Application/OptionalSubjects/SubjectExemptions.cs`
- Create: `src/SamaEcole.Application/OptionalSubjects/EnrollmentOptionsPlanner.cs`
- Test: `tests/SamaEcole.UnitTests/OptionalSubjects/OptionSelectionRulesTests.cs`, `tests/SamaEcole.IntegrationTests/OptionalSubjects/SubjectExemptionsTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record OptionSubject(Guid Id, string Name, string? Group)`
  - `OptionSelectionRules.LevelMatches(string a, string b) : bool`
  - `OptionSelectionRules.Validate(IReadOnlyList<OptionSubject> levelOptions, IReadOnlyCollection<Guid> chosen) : string?` — `null` si valide, sinon le message français
  - `OptionSelectionRules.ExemptedSubjectIds(IReadOnlyList<OptionSubject> levelOptions, IReadOnlyCollection<Guid> chosen) : IReadOnlySet<Guid>`
  - `SubjectExemptions.ForStudentAsync(IApplicationDbContext db, Guid studentId, Guid schoolYearId, CancellationToken ct) : Task<IReadOnlySet<Guid>>` — matières dont l'élève est dispensé
  - `SubjectExemptions.StudentsExemptFromAsync(IApplicationDbContext db, Guid subjectId, Guid schoolYearId, CancellationToken ct) : Task<IReadOnlySet<Guid>>` — élèves dispensés d'une matière
  - `EnrollmentOptionsPlanner.LoadLevelOptionsAsync(IApplicationDbContext db, string level, CancellationToken ct) : Task<IReadOnlyList<OptionSubject>>`
  - `EnrollmentOptionsPlanner.PlanExemptionsAsync(IApplicationDbContext db, string level, IReadOnlyCollection<Guid> chosen, string field, CancellationToken ct) : Task<IReadOnlySet<Guid>>` — lève `ValidationException` (422) sur `field`

- [ ] **Step 1: Écrire les tests de la règle pure**

```csharp
using FluentAssertions;
using SamaEcole.Application.OptionalSubjects;
using Xunit;

namespace SamaEcole.UnitTests.OptionalSubjects;

public class OptionSelectionRulesTests
{
    private static readonly Guid Espagnol = Guid.NewGuid();
    private static readonly Guid Arabe = Guid.NewGuid();
    private static readonly Guid Allemand = Guid.NewGuid();
    private static readonly Guid Pc = Guid.NewGuid();
    private static readonly Guid Svt = Guid.NewGuid();
    private static readonly Guid Dessin = Guid.NewGuid();

    private static readonly IReadOnlyList<OptionSubject> Options =
    [
        new(Espagnol, "Espagnol", "LV2"),
        new(Arabe, "Arabe", " lv2 "),
        new(Allemand, "Allemand", "LV2"),
        new(Pc, "PC", "Option scientifique"),
        new(Svt, "SVT", "Option scientifique"),
        new(Dessin, "Dessin", null)
    ];

    [Fact]
    public void One_Choice_Per_Group_Is_Valid_And_The_Others_Are_Exempted()
    {
        OptionSelectionRules.Validate(Options, [Espagnol, Pc]).Should().BeNull();

        OptionSelectionRules.ExemptedSubjectIds(Options, [Espagnol, Pc])
            .Should().BeEquivalentTo([Arabe, Allemand, Svt, Dessin]);
    }

    [Fact]
    public void Two_Choices_In_The_Same_Group_Are_Refused_Whatever_The_Case_And_Spaces_Of_The_Group()
    {
        var message = OptionSelectionRules.Validate(Options, [Espagnol, Arabe]);

        message.Should().NotBeNull().And.Contain("LV2");
    }

    [Fact]
    public void Ungrouped_Options_Can_Be_Combined_And_An_Empty_Choice_Exempts_Everything()
    {
        OptionSelectionRules.Validate(Options, [Dessin]).Should().BeNull();

        OptionSelectionRules.Validate(Options, []).Should().BeNull();
        OptionSelectionRules.ExemptedSubjectIds(Options, []).Should().HaveCount(Options.Count);
    }

    [Fact]
    public void A_Subject_Outside_The_Level_Options_Is_Refused()
    {
        var stranger = Guid.NewGuid();

        OptionSelectionRules.Validate(Options, [stranger]).Should().NotBeNull();
    }

    [Fact]
    public void A_Duplicate_Id_Is_Not_Counted_Twice()
        => OptionSelectionRules.Validate(Options, [Espagnol, Espagnol]).Should().BeNull();

    [Theory]
    [InlineData("Collège", " collège ", true)]
    [InlineData("Terminale S2", "terminale s2", true)]
    [InlineData("Collège", "Lycée", false)]
    public void Levels_Match_Ignoring_Case_And_Edge_Spaces(string a, string b, bool expected)
        => OptionSelectionRules.LevelMatches(a, b).Should().Be(expected);
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/SamaEcole.UnitTests --filter "FullyQualifiedName~OptionSelectionRulesTests"`
Expected: FAIL à la compilation (`OptionSubject`, `Validate`… absents).

- [ ] **Step 3: Compléter `OptionSelectionRules.cs`**

Remplacer le fichier par :

```csharp
namespace SamaEcole.Application.OptionalSubjects;

/// <summary>Une matière optionnelle d'un niveau, telle que la règle la voit (aucune dépendance EF).</summary>
public sealed record OptionSubject(Guid Id, string Name, string? Group);

/// <summary>
/// Règles PURES (aucun accès base) des matières optionnelles : normalisation d'un groupe, appariement de
/// niveau, validation d'un choix et dispenses qui en découlent (spécification §4.3, §5.2).
///
/// Un « choix » est l'ensemble des options que l'élève SUIT. Ses dispenses sont toutes les autres options
/// du niveau. Un choix vide dispense donc de toutes les options — c'est le cas d'un élève qui n'en suit
/// aucune, distinct de « aucun choix enregistré » (aucune ligne de dispense : il suit tout).
/// </summary>
public static class OptionSelectionRules
{
    /// <summary>Groupe nettoyé : blanc → null, espaces de bord retirés.</summary>
    public static string? NormalizeGroup(string? group) =>
        string.IsNullOrWhiteSpace(group) ? null : group.Trim();

    /// <summary>
    /// Deux niveaux sont le même niveau s'ils ne diffèrent que par la casse ou les espaces de bord — la
    /// même tolérance qu'<c>EvaluationStructureBuilder</c> : le niveau est un texte libre par école.
    /// </summary>
    public static bool LevelMatches(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Null si le choix est valide, sinon le message (français) à renvoyer en 422.</summary>
    public static string? Validate(IReadOnlyList<OptionSubject> levelOptions, IReadOnlyCollection<Guid> chosen)
    {
        var byId = levelOptions.ToDictionary(o => o.Id);
        var distinct = chosen.Distinct().ToList();

        if (distinct.Any(id => !byId.ContainsKey(id)))
        {
            return "Une des matières choisies n'est pas une option du niveau de cette classe.";
        }

        // Au plus une matière par groupe. Les options sans groupe sont cumulables : jamais comptées ici.
        var tooMany = distinct
            .Select(id => byId[id])
            .Where(o => NormalizeGroup(o.Group) is not null)
            .GroupBy(o => NormalizeGroup(o.Group)!, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        return tooMany is null
            ? null
            : $"Une seule matière peut être suivie dans le groupe « {tooMany.Key} » "
              + $"({string.Join(", ", tooMany.Select(o => o.Name))}).";
    }

    /// <summary>Les options du niveau que l'élève ne suit pas : ce sont ses dispenses.</summary>
    public static IReadOnlySet<Guid> ExemptedSubjectIds(
        IReadOnlyList<OptionSubject> levelOptions, IReadOnlyCollection<Guid> chosen)
    {
        var followed = chosen.ToHashSet();
        return levelOptions.Where(o => !followed.Contains(o.Id)).Select(o => o.Id).ToHashSet();
    }
}
```

- [ ] **Step 4: Vérifier que la règle passe**

Run: `dotnet test tests/SamaEcole.UnitTests --filter "FullyQualifiedName~OptionSelectionRulesTests"`
Expected: PASS (7 cas).

- [ ] **Step 5: Écrire les tests des requêtes de dispenses**

Créer `tests/SamaEcole.IntegrationTests/OptionalSubjects/SubjectExemptionsTests.cs` :

```csharp
using FluentAssertions;
using SamaEcole.Application.OptionalSubjects;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.OptionalSubjects;

/// <summary>Quelles matières un élève ne suit pas (et quels élèves ne suivent pas une matière) ?</summary>
[Trait("Category", "MultiTenant")]
public class SubjectExemptionsTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("94444444-4444-4444-4444-444444444444");
    private static readonly Guid Autre = Guid.Parse("95555555-5555-5555-5555-555555555555");
    private static readonly Guid Annee1 = Guid.Parse("94444444-0000-0000-0000-000000000001");
    private static readonly Guid Annee0 = Guid.Parse("94444444-0000-0000-0000-000000000002");
    private static readonly Guid AnneeAutre = Guid.Parse("95555555-0000-0000-0000-000000000001");
    private static readonly Guid Classe = Guid.Parse("94444444-0000-0000-0000-0000000000c1");
    private static readonly Guid ClasseAutre = Guid.Parse("95555555-0000-0000-0000-0000000000c1");
    private static readonly Guid Arabe = Guid.Parse("94444444-0000-0000-0000-0000000000a1");
    private static readonly Guid ArabeAutre = Guid.Parse("95555555-0000-0000-0000-0000000000a1");

    private static readonly Guid EleveDispense = Guid.Parse("94444444-0000-0000-0000-0000000000e1");
    private static readonly Guid EleveAnnule = Guid.Parse("94444444-0000-0000-0000-0000000000e2");
    private static readonly Guid EleveLibre = Guid.Parse("94444444-0000-0000-0000-0000000000e3");
    private static readonly Guid EleveAutre = Guid.Parse("95555555-0000-0000-0000-0000000000e1");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(new School { Id = Ecole, Name = "A" }, new School { Id = Autre, Name = "B" });
        owner.SchoolYears.AddRange(
            new SchoolYear { Id = Annee1, SchoolId = Ecole, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true },
            new SchoolYear { Id = Annee0, SchoolId = Ecole, Label = "2025-2026", StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) },
            new SchoolYear { Id = AnneeAutre, SchoolId = Autre, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true });
        owner.Classrooms.AddRange(
            new Classroom { Id = Classe, SchoolId = Ecole, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 },
            new Classroom { Id = ClasseAutre, SchoolId = Autre, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 });
        owner.Subjects.AddRange(
            new Subject { Id = Arabe, SchoolId = Ecole, Name = "Arabe", Level = "Collège", Coefficient = 2, IsOptional = true, OptionGroup = "LV2" },
            new Subject { Id = ArabeAutre, SchoolId = Autre, Name = "Arabe", Level = "Collège", Coefficient = 2, IsOptional = true, OptionGroup = "LV2" });
        owner.Students.AddRange(
            Student(EleveDispense, Ecole, "E1", Classe), Student(EleveAnnule, Ecole, "E2", Classe),
            Student(EleveLibre, Ecole, "E3", Classe), Student(EleveAutre, Autre, "E4", ClasseAutre));

        var inscriptionDispense = Enrollment(EleveDispense, Ecole, Annee1, Classe, EnrollmentStatus.Confirmed, "R1");
        var inscriptionAnnulee = Enrollment(EleveAnnule, Ecole, Annee1, Classe, EnrollmentStatus.Cancelled, "R2");
        var inscriptionLibre = Enrollment(EleveLibre, Ecole, Annee1, Classe, EnrollmentStatus.Confirmed, "R3");
        var inscriptionAutre = Enrollment(EleveAutre, Autre, AnneeAutre, ClasseAutre, EnrollmentStatus.Confirmed, "R4");
        owner.Enrollments.AddRange(inscriptionDispense, inscriptionAnnulee, inscriptionLibre, inscriptionAutre);

        owner.EnrollmentSubjectExemptions.AddRange(
            new EnrollmentSubjectExemption { SchoolId = Ecole, EnrollmentId = inscriptionDispense.Id, SubjectId = Arabe },
            new EnrollmentSubjectExemption { SchoolId = Ecole, EnrollmentId = inscriptionAnnulee.Id, SubjectId = Arabe },
            new EnrollmentSubjectExemption { SchoolId = Autre, EnrollmentId = inscriptionAutre.Id, SubjectId = ArabeAutre });

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task An_Exempted_Student_Is_Reported_For_The_Year_Of_His_Enrollment()
    {
        await using var db = _db.NewAppContext(Ecole);

        (await SubjectExemptions.ForStudentAsync(db, EleveDispense, Annee1, default)).Should().BeEquivalentTo([Arabe]);
    }

    [Fact]
    public async Task A_Student_Without_Any_Exemption_Follows_Everything()
    {
        await using var db = _db.NewAppContext(Ecole);

        (await SubjectExemptions.ForStudentAsync(db, EleveLibre, Annee1, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task An_Exemption_Never_Applies_To_Another_School_Year()
    {
        await using var db = _db.NewAppContext(Ecole);

        (await SubjectExemptions.ForStudentAsync(db, EleveDispense, Annee0, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_Cancelled_Enrollment_Exempts_Nobody()
    {
        await using var db = _db.NewAppContext(Ecole);

        (await SubjectExemptions.ForStudentAsync(db, EleveAnnule, Annee1, default)).Should().BeEmpty();
        (await SubjectExemptions.StudentsExemptFromAsync(db, Arabe, Annee1, default)).Should().BeEquivalentTo([EleveDispense]);
    }

    [Fact]
    public async Task An_Exemption_Is_Ignored_Once_The_Subject_Is_No_Longer_Optional()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            var subject = await owner.Subjects.FindAsync(Arabe);
            subject!.IsOptional = false;
            await owner.SaveChangesAsync();
        }

        await using var db = _db.NewAppContext(Ecole);

        (await SubjectExemptions.ForStudentAsync(db, EleveDispense, Annee1, default)).Should().BeEmpty();
        (await SubjectExemptions.StudentsExemptFromAsync(db, Arabe, Annee1, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_Soft_Deleted_Exemption_Is_Ignored()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            var row = owner.EnrollmentSubjectExemptions.Single(x => x.SchoolId == Ecole && x.SubjectId == Arabe && !x.IsDeleted
                && owner.Enrollments.Any(e => e.Id == x.EnrollmentId && e.StudentId == EleveDispense));
            row.SoftDelete("test");
            await owner.SaveChangesAsync();
        }

        await using var db = _db.NewAppContext(Ecole);

        (await SubjectExemptions.ForStudentAsync(db, EleveDispense, Annee1, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Another_School_Never_Sees_These_Exemptions()
    {
        await using var db = _db.NewAppContext(Autre);

        (await SubjectExemptions.StudentsExemptFromAsync(db, Arabe, Annee1, default)).Should().BeEmpty();
        (await SubjectExemptions.ForStudentAsync(db, EleveDispense, Annee1, default)).Should().BeEmpty();
        (await SubjectExemptions.StudentsExemptFromAsync(db, ArabeAutre, AnneeAutre, default)).Should().BeEquivalentTo([EleveAutre]);
    }

    private static Student Student(Guid id, Guid school, string matricule, Guid classroom) => new()
    {
        Id = id, SchoolId = school, Matricule = matricule, FullName = matricule,
        BirthDate = new DateOnly(2011, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = classroom
    };

    private static Enrollment Enrollment(Guid student, Guid school, Guid year, Guid classroom, EnrollmentStatus status, string receipt) => new()
    {
        SchoolId = school, StudentId = student, SchoolYearId = year, ClassroomId = classroom,
        Type = EnrollmentType.NewEnrollment, Status = status, ReceiptNumber = receipt
    };
}
```

- [ ] **Step 6: Vérifier l'échec**

Run: `dotnet build tests/SamaEcole.IntegrationTests`
Expected: FAIL — `SubjectExemptions` n'existe pas.

- [ ] **Step 7: Implémenter `SubjectExemptions.cs`**

```csharp
using System.Collections.Frozen;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.OptionalSubjects;

/// <summary>
/// Requêtes de dispenses lues par les six lecteurs de la spécification §4.2 (sommaire de notes, fiche
/// élève, structure APC, feuilles de notes, import, saisie). Classe STATIQUE et sans état : aucun
/// paramètre de constructeur à ajouter aux handlers, aucun cache à invalider (spécification, écart E1).
///
/// Deux règles tenues ICI, pour qu'aucun lecteur ne les réimplémente :
/// — l'inscription doit être ACTIVE pour l'année (hors <see cref="EnrollmentStatus.Cancelled"/>, même
///   convention que <c>CoefficientOverrideLoader</c>) ;
/// — la matière doit être ENCORE optionnelle : repasser une matière en « obligatoire » la rend
///   immédiatement à tous, sans purge de lignes (écart E3).
///
/// Aucun filtre SchoolId à la main : le Global Query Filter et la policy RLS bornent tout à l'école
/// courante (règle #2). Une dispense supprimée logiquement est déjà écartée par le filtre.
/// </summary>
public static class SubjectExemptions
{
    /// <summary>Les matières dont l'élève est dispensé pour cette année. Vide = il suit tout.</summary>
    public static async Task<IReadOnlySet<Guid>> ForStudentAsync(
        IApplicationDbContext dbContext, Guid studentId, Guid schoolYearId, CancellationToken cancellationToken)
    {
        var ids = await (
            from x in dbContext.EnrollmentSubjectExemptions.AsNoTracking()
            join e in dbContext.Enrollments.AsNoTracking() on x.EnrollmentId equals e.Id
            join s in dbContext.Subjects.AsNoTracking() on x.SubjectId equals s.Id
            where e.StudentId == studentId
                  && e.SchoolYearId == schoolYearId
                  && e.Status != EnrollmentStatus.Cancelled
                  && s.IsOptional
            select x.SubjectId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return ids.Count == 0 ? FrozenSet<Guid>.Empty : ids.ToFrozenSet();
    }

    /// <summary>Les élèves dispensés de cette matière pour cette année (feuilles de notes, import, saisie).</summary>
    public static async Task<IReadOnlySet<Guid>> StudentsExemptFromAsync(
        IApplicationDbContext dbContext, Guid subjectId, Guid schoolYearId, CancellationToken cancellationToken)
    {
        var ids = await (
            from x in dbContext.EnrollmentSubjectExemptions.AsNoTracking()
            join e in dbContext.Enrollments.AsNoTracking() on x.EnrollmentId equals e.Id
            join s in dbContext.Subjects.AsNoTracking() on x.SubjectId equals s.Id
            where x.SubjectId == subjectId
                  && e.SchoolYearId == schoolYearId
                  && e.Status != EnrollmentStatus.Cancelled
                  && s.IsOptional
            select e.StudentId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return ids.Count == 0 ? FrozenSet<Guid>.Empty : ids.ToFrozenSet();
    }
}
```

- [ ] **Step 8: Implémenter `EnrollmentOptionsPlanner.cs`**

```csharp
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.OptionalSubjects;

/// <summary>
/// Passerelle entre la base et <see cref="OptionSelectionRules"/> : charge les options d'un niveau puis
/// transforme un choix en dispenses, ou refuse (422). Partagée par la création d'inscription et par
/// <c>SetEnrollmentOptionsCommand</c> pour que les deux entrées appliquent exactement la même règle.
/// </summary>
public static class EnrollmentOptionsPlanner
{
    /// <summary>
    /// Matières optionnelles AUTONOMES du niveau (le niveau est un texte libre, comparé en mémoire sans
    /// casse ni espaces de bord — un WHERE SQL le ferait mal). Triées par groupe puis par nom.
    /// </summary>
    public static async Task<IReadOnlyList<OptionSubject>> LoadLevelOptionsAsync(
        IApplicationDbContext dbContext, string level, CancellationToken cancellationToken)
    {
        var rows = await dbContext.Subjects.AsNoTracking()
            .Where(s => s.IsOptional && s.ParentSubjectId == null)
            .Select(s => new { s.Id, s.Name, s.Level, s.OptionGroup })
            .ToListAsync(cancellationToken);

        return rows
            .Where(s => OptionSelectionRules.LevelMatches(s.Level, level))
            .Select(s => new OptionSubject(s.Id, s.Name, OptionSelectionRules.NormalizeGroup(s.OptionGroup)))
            .OrderBy(o => o.Group ?? "\uffff", StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(o => o.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>Les matières à dispenser pour ce choix ; lève <see cref="ValidationException"/> sur <paramref name="field"/> si le choix est invalide.</summary>
    public static async Task<IReadOnlySet<Guid>> PlanExemptionsAsync(
        IApplicationDbContext dbContext, string level, IReadOnlyCollection<Guid> chosen, string field,
        CancellationToken cancellationToken)
    {
        var options = await LoadLevelOptionsAsync(dbContext, level, cancellationToken);

        if (OptionSelectionRules.Validate(options, chosen) is { } message)
        {
            throw new ValidationException([new ValidationFailure(field, message)]);
        }

        return OptionSelectionRules.ExemptedSubjectIds(options, chosen);
    }
}
```

- [ ] **Step 9: Lancer les tests ciblés**

Run:
```
dotnet test tests/SamaEcole.UnitTests --filter "FullyQualifiedName~OptionSelectionRulesTests"
dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~SubjectExemptionsTests"
```
Expected: PASS (7 + 7).

- [ ] **Step 10: Commit**

```bash
git add src/SamaEcole.Application/OptionalSubjects tests/SamaEcole.UnitTests/OptionalSubjects \
  tests/SamaEcole.IntegrationTests/OptionalSubjects/SubjectExemptionsTests.cs
git commit -m "feat(options): règles de choix et requêtes de dispenses

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 4: Moyennes, fiche élève et bulletin

**Files:**
- Modify: `src/SamaEcole.Application/Grades/Queries/GetGradeSummary/GetGradeSummaryQueryHandler.cs`
- Modify: `src/SamaEcole.Application/Students/Queries/GetStudentDetail/GetStudentDetailQuery.cs` (`BuildTermReportsAsync`, l. ~371-395)
- Modify: `src/SamaEcole.Application/ReportCards/EvaluationStructureBuilder.cs` (signature l. 40-46, filtre l. 56-58)
- Modify: `src/SamaEcole.Application/ReportCards/Queries/GetReportCardPdf/GetReportCardPdfQuery.cs` (appel l. ~336)
- Modify: `src/SamaEcole.Application/StateIntegration/Queries/GetSkillsBookletPdf/GetSkillsBookletPdfQueryHandler.cs` (appel l. ~84)
- Test: `tests/SamaEcole.IntegrationTests/OptionalSubjects/OptionalSubjectsCalculationTests.cs`

**Interfaces:**
- Consumes: `SubjectExemptions.ForStudentAsync` (Tâche 3).
- Produces: `EvaluationStructureBuilder.BuildAsync(IApplicationDbContext dbContext, string classroomLevel, int gradingScale, IReadOnlyList<SubjectGradeDto> gradedSubjects, IReadOnlyList<(string Label, decimal MinAverage)> mentionsOnReferenceScale, IReadOnlySet<Guid> exemptSubjectIds, CancellationToken cancellationToken)` — le paramètre `exemptSubjectIds` est inséré **avant** `cancellationToken`.

- [ ] **Step 1: Écrire les tests de calcul**

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Coefficients;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Application.ReportCards;
using SamaEcole.Application.Students.Queries.GetStudentDetail;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.OptionalSubjects;

/// <summary>
/// Matières optionnelles — le sommaire de notes, la fiche élève et la structure APC ignorent les matières
/// dont l'élève est dispensé, et NE CHANGENT RIEN tant qu'aucune dispense n'existe.
///
/// Jeu : Maths (coef 4, note 12), Français (2, 16), Espagnol (3, 14, option LV2), Arabe (3, 10, option LV2).
/// Un élève sans dispense : coefficients 12, points 48+32+42+30 = 152. Dispensé d'Arabe : coefficients 9,
/// points 48+32+42 = 122 — l'Arabe reste NOTÉ en base mais n'entre plus nulle part.
/// </summary>
[Trait("Category", "MultiTenant")]
public class OptionalSubjectsCalculationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("96666666-6666-6666-6666-666666666666");
    private static readonly Guid Annee1 = Guid.Parse("96666666-0000-0000-0000-000000000001");
    private static readonly Guid Annee0 = Guid.Parse("96666666-0000-0000-0000-000000000002");
    private static readonly Guid Trimestre1 = Guid.Parse("96666666-0000-0000-0000-0000000000d1");
    private static readonly Guid Trimestre0 = Guid.Parse("96666666-0000-0000-0000-0000000000d0");
    private static readonly Guid Classe = Guid.Parse("96666666-0000-0000-0000-0000000000c1");

    private static readonly Guid Maths = Guid.Parse("96666666-0000-0000-0000-0000000000a1");
    private static readonly Guid Francais = Guid.Parse("96666666-0000-0000-0000-0000000000a2");
    private static readonly Guid Espagnol = Guid.Parse("96666666-0000-0000-0000-0000000000a3");
    private static readonly Guid Arabe = Guid.Parse("96666666-0000-0000-0000-0000000000a4");

    private static readonly Guid EleveLibre = Guid.Parse("96666666-0000-0000-0000-0000000000e1");
    private static readonly Guid EleveDispense = Guid.Parse("96666666-0000-0000-0000-0000000000e2");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = Ecole, Name = "Collège A" });
        owner.SchoolYears.AddRange(
            new SchoolYear { Id = Annee1, SchoolId = Ecole, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true },
            new SchoolYear { Id = Annee0, SchoolId = Ecole, Label = "2025-2026", StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) });
        owner.Terms.AddRange(
            new Term { Id = Trimestre1, SchoolId = Ecole, SchoolYearId = Annee1, Label = "1er trimestre", Order = 1, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 12, 20) },
            new Term { Id = Trimestre0, SchoolId = Ecole, SchoolYearId = Annee0, Label = "1er trimestre", Order = 1, StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2025, 12, 20) });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 });
        owner.Subjects.AddRange(
            new Subject { Id = Maths, SchoolId = Ecole, Name = "Mathématiques", Level = "Collège", Coefficient = 4 },
            new Subject { Id = Francais, SchoolId = Ecole, Name = "Français", Level = "Collège", Coefficient = 2 },
            new Subject { Id = Espagnol, SchoolId = Ecole, Name = "Espagnol", Level = "Collège", Coefficient = 3, IsOptional = true, OptionGroup = "LV2" },
            new Subject { Id = Arabe, SchoolId = Ecole, Name = "Arabe", Level = "Collège", Coefficient = 3, IsOptional = true, OptionGroup = "LV2" });
        owner.Students.AddRange(NewStudent(EleveLibre, "ELEV-0001"), NewStudent(EleveDispense, "ELEV-0002"));

        var inscriptionLibre = NewEnrollment(EleveLibre, Annee1, "R-1");
        var inscriptionDispense = NewEnrollment(EleveDispense, Annee1, "R-2");
        owner.Enrollments.AddRange(inscriptionLibre, inscriptionDispense, NewEnrollment(EleveDispense, Annee0, "R-0"));
        owner.EnrollmentSubjectExemptions.Add(
            new EnrollmentSubjectExemption { SchoolId = Ecole, EnrollmentId = inscriptionDispense.Id, SubjectId = Arabe });

        foreach (var student in new[] { EleveLibre, EleveDispense })
        {
            AddGrades(owner, student, Trimestre1);
        }

        AddGrades(owner, EleveDispense, Trimestre0);
        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // 1 — L'INVARIANT : sans dispense, le calcul est celui d'avant.
    [Fact]
    public async Task Without_Any_Exemption_Every_Optional_Subject_Counts_As_Before()
    {
        await using var db = _db.NewAppContext(Ecole);

        var summary = await SummaryAsync(db, EleveLibre, Trimestre1);

        summary.Subjects.Select(s => s.SubjectId).Should().BeEquivalentTo([Maths, Francais, Espagnol, Arabe]);
        summary.TotalCoefficients.Should().Be(12m);
        summary.TotalPoints.Should().Be(152m);
        summary.GeneralAverage.Should().Be(152m / 12m);
    }

    // 2 — la matière dispensée disparaît, et le total des coefficients s'adapte.
    [Fact]
    public async Task An_Exempted_Subject_Leaves_The_Summary_And_The_Coefficient_Total_Adapts()
    {
        await using var db = _db.NewAppContext(Ecole);

        var summary = await SummaryAsync(db, EleveDispense, Trimestre1);

        summary.Subjects.Select(s => s.SubjectId).Should().BeEquivalentTo([Maths, Francais, Espagnol]);
        summary.TotalCoefficients.Should().Be(9m);
        summary.TotalPoints.Should().Be(122m);
        summary.GeneralAverage.Should().Be(122m / 9m);
    }

    // 3 — la note n'est jamais supprimée (règle #6) : seulement masquée.
    [Fact]
    public async Task The_Grade_Of_An_Exempted_Subject_Is_Kept_In_The_Database()
    {
        await using var db = _db.NewAppContext(Ecole);

        (await db.Grades.CountAsync(g => g.StudentId == EleveDispense && g.SubjectId == Arabe)).Should().Be(2);
    }

    // 4 — une dispense posée sur l'inscription d'une année ne touche pas l'autre année.
    [Fact]
    public async Task An_Exemption_Of_This_Year_Never_Hides_The_Subject_In_Another_Year()
    {
        await using var db = _db.NewAppContext(Ecole);

        var lastYear = await SummaryAsync(db, EleveDispense, Trimestre0);

        lastYear.Subjects.Select(s => s.SubjectId).Should().Contain(Arabe);
        lastYear.TotalCoefficients.Should().Be(12m);
    }

    // 5 — repasser la matière en « obligatoire » la rend immédiatement à tous (écart E3).
    [Fact]
    public async Task A_Subject_That_Stops_Being_Optional_Comes_Back_For_Everybody()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            (await owner.Subjects.FindAsync(Arabe))!.IsOptional = false;
            await owner.SaveChangesAsync();
        }

        await using var db = _db.NewAppContext(Ecole);

        var summary = await SummaryAsync(db, EleveDispense, Trimestre1);

        summary.Subjects.Select(s => s.SubjectId).Should().Contain(Arabe);
        summary.TotalCoefficients.Should().Be(12m);
    }

    // 6 — la fiche élève ne contredit jamais le bulletin.
    [Fact]
    public async Task The_Student_Sheet_Hides_The_Same_Subject_And_Shows_The_Same_Average_As_The_Summary()
    {
        await using var db = _db.NewAppContext(Ecole);

        var summary = await SummaryAsync(db, EleveDispense, Trimestre1);
        var detail = await new GetStudentDetailQueryHandler(
                db, new TestCurrentUser(role: Role.Directeur), new CoefficientOverrideLoader(db))
            .Handle(new GetStudentDetailQuery(EleveDispense), default);

        var term = detail.Grades.Single(t => t.TermId == Trimestre1);
        term.Subjects.Select(s => s.SubjectId).Should().BeEquivalentTo([Maths, Francais, Espagnol]);
        term.GeneralAverage.Should().Be(summary.GeneralAverage, "une seule règle, deux lecteurs");
    }

    // 7 — la grille APC n'imprime pas la ligne d'une matière dispensée, même sans note.
    [Fact]
    public async Task The_Evaluation_Structure_Omits_The_Exempted_Line_Even_When_Ungraded()
    {
        var domaine = Guid.NewGuid();
        var activite = Guid.NewGuid();
        var option = Guid.NewGuid();
        await using (var owner = _db.NewOwnerContext())
        {
            owner.Subjects.AddRange(
                new Subject { Id = domaine, SchoolId = Ecole, Name = "Lang & Com.", Level = "CE1", Coefficient = 1 },
                new Subject { Id = activite, SchoolId = Ecole, Name = "Vocabulaire", Level = "CE1", Coefficient = 1, ParentSubjectId = domaine },
                new Subject { Id = option, SchoolId = Ecole, Name = "Arabe CE1", Level = "CE1", Coefficient = 1, IsOptional = true });
            await owner.SaveChangesAsync();
        }

        await using var db = _db.NewAppContext(Ecole);

        var all = await EvaluationStructureBuilder.BuildAsync(
            db, "CE1", 10, [], [], new HashSet<Guid>(), default);
        var without = await EvaluationStructureBuilder.BuildAsync(
            db, "CE1", 10, [], [], new HashSet<Guid> { option }, default);

        all!.Groups.Select(g => g.SubjectId).Should().Contain(option);
        without!.Groups.Select(g => g.SubjectId).Should().NotContain(option).And.Contain(domaine);
    }

    private static Task<GradeSummaryDto> SummaryAsync(SamaEcole.Persistence.ApplicationDbContext db, Guid student, Guid term)
        => new GetGradeSummaryQueryHandler(db, new CoefficientOverrideLoader(db))
            .Handle(new GetGradeSummaryQuery(student, term), default);

    private static Student NewStudent(Guid id, string matricule) => new()
    {
        Id = id, SchoolId = Ecole, Matricule = matricule, FullName = matricule,
        BirthDate = new DateOnly(2011, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe
    };

    private static Enrollment NewEnrollment(Guid student, Guid year, string receipt) => new()
    {
        SchoolId = Ecole, StudentId = student, SchoolYearId = year, ClassroomId = Classe,
        Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, ReceiptNumber = receipt
    };

    private static void AddGrades(SamaEcole.Persistence.ApplicationDbContext owner, Guid student, Guid term)
    {
        foreach (var (subject, value) in new[] { (Maths, 12m), (Francais, 16m), (Espagnol, 14m), (Arabe, 10m) })
        {
            owner.Grades.Add(new Grade
            {
                SchoolId = Ecole, StudentId = student, SubjectId = subject, TermId = term,
                EvaluationType = EvaluationType.Composition, Value = value
            });
        }
    }
}
```
Note pour l'exécutant : `GetStudentDetailQueryHandler` peut exiger d'autres données de l'élève (classe, école) que `EffectiveCoefficientTests` fournit déjà avec le même jeu minimal ; si le test 6 échoue sur un autre motif que l'assertion, comparer avec `EffectiveCoefficientTests.The_Student_Sheet_Shows_The_Same_Coefficient_And_Average_As_The_Grade_Summary` (même appel, même jeu).

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~OptionalSubjectsCalculationTests"`
Expected: FAIL à la compilation (`BuildAsync` n'accepte pas `exemptSubjectIds`).

- [ ] **Step 3: `GetGradeSummaryQueryHandler`**

Ajouter `using SamaEcole.Application.OptionalSubjects;`. Après la ligne qui calcule `overrides` (l. ~49) :

```csharp

        // Matières optionnelles : les matières dont l'élève est dispensé cette année sortent du calcul. Leurs
        // notes restent en base (règle #6) mais n'entrent ni dans le total des coefficients ni dans celui des
        // points — d'où « 22 au lieu de 25 ». Sans dispense l'ensemble est vide : calcul strictement d'avant.
        var exempt = await SubjectExemptions.ForStudentAsync(
            dbContext, request.StudentId, schoolYearId, cancellationToken);
```
puis remplacer `var subjects = rows\n            .GroupBy(` par :

```csharp
        var subjects = rows
            .Where(r => !exempt.Contains(r.SubjectId))
            .GroupBy(
```
(le `.GroupBy(r => new { ... })` qui suit est inchangé : ne déplacer que le `.Where` juste avant).

- [ ] **Step 4: `GetStudentDetailQueryHandler.BuildTermReportsAsync`**

Ajouter `using SamaEcole.Application.OptionalSubjects;`. Juste après le `.ToListAsync(cancellationToken);` qui remplit `gradeRows` et **avant** le commentaire « Aucune note : liste vide » :

```csharp

        // Matières optionnelles : même filtre que GetGradeSummaryQueryHandler, année par année (la dispense
        // est portée par l'inscription de l'année) — la fiche ne contredit jamais le bulletin. Filtré AVANT
        // le contrôle « aucune note » ci-dessous : un trimestre dont toutes les notes sont masquées ne doit
        // pas laisser un bloc fantôme sans matière.
        var exemptionsByYear = new Dictionary<Guid, IReadOnlySet<Guid>>();
        foreach (var yearId in gradeRows.Select(r => r.SchoolYearId).Distinct())
        {
            exemptionsByYear[yearId] = await SubjectExemptions.ForStudentAsync(
                dbContext, studentId, yearId, cancellationToken);
        }

        gradeRows = gradeRows
            .Where(r => !exemptionsByYear[r.SchoolYearId].Contains(r.SubjectId))
            .ToList();
```

- [ ] **Step 5: `EvaluationStructureBuilder`**

Signature : insérer avant `CancellationToken cancellationToken` le paramètre
```csharp
        IReadOnlySet<Guid> exemptSubjectIds,
```
et documenter dans le résumé XML : « <paramref name="exemptSubjectIds"/> : matières dont l'élève est dispensé — leur ligne n'est pas imprimée, même sans note (la grille imprime toutes les lignes du niveau). ». Puis remplacer le filtre de `levelSubjects` :

```csharp
        var levelSubjects = all
            .Where(s => string.Equals(s.Level.Trim(), classroomLevel.Trim(), StringComparison.OrdinalIgnoreCase)
                        && !exemptSubjectIds.Contains(s.Id))
            .ToList();
```

- [ ] **Step 6: Appelants de `BuildAsync`**

`GetReportCardPdfQuery.cs` (`ReportCardDataService.BuildAsync`, avant l'appel l. ~336) :

```csharp
        // Matières optionnelles : la grille APC ne doit pas imprimer la ligne d'une matière dont l'élève est
        // dispensé cette année (sa note, si elle existe, est déjà retirée du sommaire).
        var exemptSubjects = await SubjectExemptions.ForStudentAsync(
            dbContext, student.Id, term.SchoolYearId, cancellationToken);

        var evaluationStructure = await EvaluationStructureBuilder.BuildAsync(
            dbContext, classroom.Level, gradingScale, summary.Subjects, mentionScale, exemptSubjects, cancellationToken);
```
(ajouter `using SamaEcole.Application.OptionalSubjects;`).

`GetSkillsBookletPdfQueryHandler.cs` (boucle `foreach (var term in terms)`) :

```csharp
            var exemptSubjects = await SubjectExemptions.ForStudentAsync(
                dbContext, student.Id, term.SchoolYearId, cancellationToken);

            structurePerTerm.Add(await EvaluationStructureBuilder.BuildAsync(
                dbContext, classroom.Level, gradingScale, summary.Subjects, mentionScale, exemptSubjects, cancellationToken));
```
Si `terms` n'est pas une liste d'entités `Term` (pas de `SchoolYearId`), lire la déclaration de `terms` dans ce handler et utiliser l'identifiant de l'année qu'il expose ; le compilateur le signale.

- [ ] **Step 7: Compiler puis lancer les tests ciblés**

Run:
```
dotnet build
dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~OptionalSubjectsCalculationTests|FullyQualifiedName~GradeSummaryTests|FullyQualifiedName~EffectiveCoefficientTests|FullyQualifiedName~ApcEvaluationStructureTests|FullyQualifiedName~GetStudentDetailQueryTests"
```
Expected: PASS. Les tests existants n'ont pas changé : aucune signature publique de handler n'a bougé, seul `EvaluationStructureBuilder.BuildAsync` (appelé uniquement par les deux sources ci-dessus) a gagné un paramètre. Si un test existant l'appelle directement, ajouter `new HashSet<Guid>()` avant le `CancellationToken`.

- [ ] **Step 8: Commit**

```bash
git add src/SamaEcole.Application/Grades/Queries/GetGradeSummary src/SamaEcole.Application/Students/Queries/GetStudentDetail \
  src/SamaEcole.Application/ReportCards src/SamaEcole.Application/StateIntegration/Queries/GetSkillsBookletPdf \
  tests/SamaEcole.IntegrationTests/OptionalSubjects/OptionalSubjectsCalculationTests.cs
git commit -m "feat(options): moyennes, fiche élève et bulletin ignorent les matières dispensées

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 5: Feuilles de notes, import et saisie

**Files:**
- Modify: `src/SamaEcole.Application/Grades/Queries/GetClassGrades/GetClassGradesQueryHandler.cs`
- Modify: `src/SamaEcole.Application/Grades/Queries/GetGradeSheetPdf/GetGradeSheetPdfQueryHandler.cs`
- Modify: `src/SamaEcole.Application/Grades/Queries/GetGradeSheetExcel/GetGradeSheetExcelQuery.cs`
- Modify: `src/SamaEcole.Application/Grades/Commands/ImportGradeSheet/ImportGradeSheetCommandHandler.cs`
- Modify: `src/SamaEcole.Application/Grades/Commands/CreateGrade/CreateGradeCommandHandler.cs`
- Test: `tests/SamaEcole.IntegrationTests/OptionalSubjects/GradeEntryExemptionTests.cs`

**Interfaces:**
- Consumes: `SubjectExemptions.StudentsExemptFromAsync`, `SubjectExemptions.ForStudentAsync` (Tâche 3).
- Produces: aucun nouveau type. Comportement : feuille sans les élèves dispensés ; import → erreur de ligne ; saisie → 422 sur `SubjectId`.

- [ ] **Step 1: Écrire les tests**

Créer `tests/SamaEcole.IntegrationTests/OptionalSubjects/GradeEntryExemptionTests.cs`. Jeu : une école, une année active, un trimestre, la classe « 4ème A » (Collège), deux élèves inscrits — `ELEV-0001` suit tout, `ELEV-0002` est dispensé d'Arabe (matière optionnelle LV2) — et Maths obligatoire. Les doublures privées sont celles de `GradeCorrectionTests` (parseur, tenant) et de `GetGradeSheetPdfQueryTests` (générateur espion, logo).

```csharp
using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Grades;
using SamaEcole.Application.Grades.Commands.CreateGrade;
using SamaEcole.Application.Grades.Commands.ImportGradeSheet;
using SamaEcole.Application.Grades.Queries.GetClassGrades;
using SamaEcole.Application.Grades.Queries.GetGradeSheetExcel;
using SamaEcole.Application.Grades.Queries.GetGradeSheetPdf;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.OptionalSubjects;

/// <summary>
/// Matières optionnelles — la saisie des notes ne propose ni n'accepte un élève dispensé : grille de saisie,
/// fiche imprimée, import Excel et saisie unitaire (spécification §4.2).
/// </summary>
[Trait("Category", "MultiTenant")]
public class GradeEntryExemptionTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("97777777-7777-7777-7777-777777777777");
    private static readonly Guid Annee = Guid.Parse("97777777-0000-0000-0000-000000000001");
    private static readonly Guid Trimestre = Guid.Parse("97777777-0000-0000-0000-0000000000d1");
    private static readonly Guid Classe = Guid.Parse("97777777-0000-0000-0000-0000000000c1");
    private static readonly Guid Maths = Guid.Parse("97777777-0000-0000-0000-0000000000a1");
    private static readonly Guid Arabe = Guid.Parse("97777777-0000-0000-0000-0000000000a2");
    private static readonly Guid EleveLibre = Guid.Parse("97777777-0000-0000-0000-0000000000e1");
    private static readonly Guid EleveDispense = Guid.Parse("97777777-0000-0000-0000-0000000000e2");
    private static readonly Guid Directeur = Guid.Parse("97777777-0000-0000-0000-0000000000f1");

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    private sealed class StubParser(params GradeSheetRow[] rows) : IGradeSheetImportParser
    {
        public IReadOnlyList<GradeSheetRow> Parse(byte[] fileContent, string fileName) => rows;
    }

    private sealed class SpyGenerator : IGradeSheetPdfGenerator
    {
        public GradeSheetPdfDto? Captured { get; private set; }

        public byte[] Generate(GradeSheetPdfDto sheet, byte[]? logo)
        {
            Captured = sheet;
            return [0x25, 0x50, 0x44, 0x46];
        }
    }

    private sealed class FixedLogoProvider(byte[]? logo) : ISchoolLogoProvider
    {
        public Task<byte[]?> TryFetchAsync(string? logoUrl, CancellationToken cancellationToken) => Task.FromResult(logo);
    }

    private sealed class SpyExcel : IGradeSheetExcelGenerator
    {
        public IReadOnlyList<GradeSheetStudentRow> Rows { get; private set; } = [];

        public byte[] Generate(IReadOnlyList<GradeSheetStudentRow> rows, int gradingScale)
        {
            Rows = rows;
            return [0x50, 0x4B];
        }
    }

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = Ecole, Name = "Collège A" });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = Ecole, Label = "2026-2027",
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true
        });
        owner.Terms.Add(new Term
        {
            Id = Trimestre, SchoolId = Ecole, SchoolYearId = Annee, Label = "1er trimestre", Order = 1,
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 12, 20)
        });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 });
        owner.Subjects.AddRange(
            new Subject { Id = Maths, SchoolId = Ecole, Name = "Mathématiques", Level = "Collège", Coefficient = 4 },
            new Subject { Id = Arabe, SchoolId = Ecole, Name = "Arabe", Level = "Collège", Coefficient = 2, IsOptional = true, OptionGroup = "LV2" });
        owner.Students.AddRange(
            new Student { Id = EleveLibre, SchoolId = Ecole, Matricule = "ELEV-0001", FullName = "Awa Fall", BirthDate = new DateOnly(2011, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe },
            new Student { Id = EleveDispense, SchoolId = Ecole, Matricule = "ELEV-0002", FullName = "Modou Diop", BirthDate = new DateOnly(2011, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = Classe });

        var inscriptionLibre = new Enrollment
        {
            SchoolId = Ecole, StudentId = EleveLibre, SchoolYearId = Annee, ClassroomId = Classe,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, ReceiptNumber = "R-1"
        };
        var inscriptionDispense = new Enrollment
        {
            SchoolId = Ecole, StudentId = EleveDispense, SchoolYearId = Annee, ClassroomId = Classe,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, ReceiptNumber = "R-2"
        };
        owner.Enrollments.AddRange(inscriptionLibre, inscriptionDispense);
        owner.EnrollmentSubjectExemptions.Add(
            new EnrollmentSubjectExemption { SchoolId = Ecole, EnrollmentId = inscriptionDispense.Id, SubjectId = Arabe });

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static TestCurrentUser Chef => new(Directeur, Role.Directeur);

    [Fact]
    public async Task The_Grade_Grid_Lists_Only_The_Students_Who_Follow_The_Subject()
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var handler = new GetClassGradesQueryHandler(
            ctx, new GradeCorrectionAuthorizer(ctx, Chef, TimeProvider.System));

        var arabe = await handler.Handle(new GetClassGradesQuery(Classe, Arabe, Trimestre), default);
        var maths = await handler.Handle(new GetClassGradesQuery(Classe, Maths, Trimestre), default);

        arabe.Select(r => r.StudentId).Should().BeEquivalentTo([EleveLibre]);
        maths.Select(r => r.StudentId).Should().BeEquivalentTo([EleveLibre, EleveDispense],
            "une matière obligatoire garde toute la classe");
    }

    [Fact]
    public async Task Entering_A_Grade_For_An_Exempted_Subject_Is_Refused_On_The_Subject_Field()
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var handler = new CreateGradeCommandHandler(ctx, new StubTenantProvider(Ecole), Chef);

        var act = () => handler.Handle(
            new CreateGradeCommand(EleveDispense, Arabe, Trimestre, EvaluationType.Devoir1, 12m), default);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().Contain(e => e.PropertyName == "SubjectId" && e.ErrorMessage.Contains("dispensé"));
    }

    [Fact]
    public async Task The_Same_Student_Can_Still_Be_Graded_In_A_Mandatory_Subject_And_The_Others_In_The_Option()
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var handler = new CreateGradeCommandHandler(ctx, new StubTenantProvider(Ecole), Chef);

        var maths = await handler.Handle(
            new CreateGradeCommand(EleveDispense, Maths, Trimestre, EvaluationType.Devoir1, 12m), default);
        var arabe = await handler.Handle(
            new CreateGradeCommand(EleveLibre, Arabe, Trimestre, EvaluationType.Devoir1, 14m), default);

        maths.Value.Should().Be(12m);
        arabe.Value.Should().Be(14m);
    }

    [Fact]
    public async Task An_Import_Row_For_An_Exempted_Student_Is_Rejected_With_A_Dedicated_Message()
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var user = Chef;
        var handler = new ImportGradeSheetCommandHandler(
            ctx, new StubTenantProvider(Ecole),
            new StubParser(new GradeSheetRow(2, "ELEV-0001", "8", "", ""), new GradeSheetRow(3, "ELEV-0002", "9", "", "")),
            user, new GradeCorrectionAuthorizer(ctx, user, TimeProvider.System));

        var act = () => handler.Handle(
            new ImportGradeSheetCommand(Classe, Arabe, Trimestre, true, [0x1], "notes.xlsx"), default);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainSingle()
            .Which.Should().Match<FluentValidation.Results.ValidationFailure>(
                e => e.PropertyName == "Ligne 3" && e.ErrorMessage.Contains("dispensé"));
    }

    [Fact]
    public async Task An_Import_Of_Students_Who_All_Follow_The_Subject_Is_Accepted()
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var user = Chef;
        var handler = new ImportGradeSheetCommandHandler(
            ctx, new StubTenantProvider(Ecole),
            new StubParser(new GradeSheetRow(2, "ELEV-0001", "8", "", "")),
            user, new GradeCorrectionAuthorizer(ctx, user, TimeProvider.System));

        var act = () => handler.Handle(
            new ImportGradeSheetCommand(Classe, Arabe, Trimestre, true, [0x1], "notes.xlsx"), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task The_Printed_Grade_Sheet_Omits_The_Exempted_Student()
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var spy = new SpyGenerator();
        var handler = new GetGradeSheetPdfQueryHandler(ctx, new StubTenantProvider(Ecole), spy, new FixedLogoProvider(null));

        await handler.Handle(new GetGradeSheetPdfQuery(Classe, Arabe, Trimestre, EvaluationType.Devoir1), default);

        spy.Captured!.Students.Select(s => s.Matricule).Should().Equal("ELEV-0001");
    }

    [Fact]
    public async Task The_Excel_Grade_Sheet_Omits_The_Exempted_Student()
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var spy = new SpyExcel();
        var handler = new GetGradeSheetExcelQueryHandler(ctx, spy);

        await handler.Handle(new GetGradeSheetExcelQuery(Classe, Arabe, Trimestre), default);

        spy.Rows.Select(r => r.Matricule).Should().Equal("ELEV-0001");
    }
}
```
Notes pour l'exécutant : `ValidationException` est ici la classe maison (`SamaEcole.Application.Common.Exceptions`) et expose `Errors` (liste de `ValidationFailure`) ; si le nom de la propriété diffère, lire `Common/Exceptions/ValidationException.cs`. Le `ValidationFailure` de la ligne d'import s'écrit `PropertyName = "Ligne 3"` (le handler formate `$"Ligne {row.RowNumber}"`). `GradeSheetRow`, `IGradeSheetImportParser`, `IGradeSheetPdfGenerator` et `ISchoolLogoProvider` sont importés par les usings ci-dessus — si l'un manque, le compilateur nomme son espace de noms (voir les `using` de `GradeCorrectionTests.cs` et `GetGradeSheetPdfQueryTests.cs`, qui les utilisent tels quels). `IGradeSheetExcelGenerator.Generate` est appelé par le handler avec `(rows, gradingScale)` : si l'interface déclare un autre type de collection (`IEnumerable`, `List`), adapter la signature de `SpyExcel` — l'assertion ne dépend que de la propriété `Matricule` de `GradeSheetStudentRow`.

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~GradeEntryExemptionTests"`
Expected: FAIL — la grille d'Arabe liste encore les deux élèves ; la saisie est acceptée.

- [ ] **Step 3: `GetClassGradesQueryHandler`**

`using SamaEcole.Application.OptionalSubjects;`. Remplacer le contrôle du trimestre par la lecture de son année :

```csharp
        var schoolYearId = await dbContext.Terms.AsNoTracking()
            .Where(t => t.Id == request.TermId)
            .Select(t => (Guid?)t.SchoolYearId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Période {request.TermId} introuvable dans votre établissement.");
```
Puis, après la liste `students` :

```csharp
        // Matières optionnelles : l'élève dispensé n'apparaît pas dans la liste de saisie de cette matière.
        var exempt = await SubjectExemptions.StudentsExemptFromAsync(
            dbContext, request.SubjectId, schoolYearId, cancellationToken);
        students = students.Where(s => !exempt.Contains(s.Id)).ToList();
```

- [ ] **Step 4: PDF, Excel, import, saisie**

`GetGradeSheetPdfQueryHandler` — `term` est déjà une entité chargée. Remplacer le bloc `var students = (await ... ToListAsync ...)` par :

```csharp
        var exempt = await SubjectExemptions.StudentsExemptFromAsync(
            dbContext, request.SubjectId, term.SchoolYearId, cancellationToken);

        var students = (await dbContext.Students.AsNoTracking()
                .Where(s => s.ClassroomId == request.ClassroomId)
                .Select(s => new { s.Id, s.Matricule, s.FullName })
                .ToListAsync(cancellationToken))
            .Where(s => !exempt.Contains(s.Id))
            .Select(s => new GradeSheetPdfStudent(s.Matricule, s.FullName))
            .OrderBy(s => s.FullName, FrenchOrder)
            .ThenBy(s => s.Matricule, StringComparer.Ordinal)
            .ToList();
```

`GetGradeSheetExcelQueryHandler` — remplacer le `AnyAsync` du trimestre par la même lecture d'année (`schoolYearId`, message identique), et après `students` : `students = students.Where(s => !exempt.Contains(s.Id)).ToList();` avec `exempt` calculé comme ci-dessus.

`ImportGradeSheetCommandHandler` — même lecture d'année à la place du `AnyAsync` du trimestre ; calculer `var exempt = await SubjectExemptions.StudentsExemptFromAsync(dbContext, request.SubjectId, schoolYearId, cancellationToken);` après le `roster`, puis, **juste après** le bloc `if (!byMatricule.TryGetValue(matricule, out var studentId)) { ... continue; }` :

```csharp
            // Matières optionnelles : une note pour un élève dispensé serait invisible partout. On refuse la
            // ligne (plutôt que de l'ignorer) : l'utilisateur doit savoir qu'elle n'est pas enregistrée.
            if (exempt.Contains(studentId))
            {
                errors.Add(new ValidationFailure(
                    field,
                    $"Matricule « {matricule} » : cet élève est dispensé de cette matière, aucune note ne peut y être saisie."));
                continue;
            }
```

`CreateGradeCommandHandler` — remplacer le `AnyAsync` du trimestre par :

```csharp
        var schoolYearId = await dbContext.Terms.AsNoTracking()
            .Where(t => t.Id == request.TermId)
            .Select(t => (Guid?)t.SchoolYearId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ValidationException([
                new ValidationFailure(nameof(request.TermId), "Le trimestre indiqué n'existe pas dans votre établissement.")
            ]);

        // Matières optionnelles : une note saisie pour un élève dispensé serait invisible sur le bulletin et
        // dans les moyennes. Filet de sécurité serveur — l'écran ne la propose déjà pas (spec §4.2).
        var exempt = await SubjectExemptions.ForStudentAsync(dbContext, request.StudentId, schoolYearId, cancellationToken);
        if (exempt.Contains(request.SubjectId))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.SubjectId),
                    "Cet élève est dispensé de cette matière : aucune note ne peut y être saisie.")
            ]);
        }
```
(`using SamaEcole.Application.OptionalSubjects;` dans chaque fichier modifié.)

- [ ] **Step 5: Lancer les tests ciblés**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~GradeEntryExemptionTests|FullyQualifiedName~GradeCorrectionTests|FullyQualifiedName~GetGradeSheetPdfQueryTests|FullyQualifiedName~GradeSummaryTests"`
Expected: PASS (les tests existants d'import/saisie/feuille restent verts : sans dispense, l'ensemble est vide).

- [ ] **Step 6: Commit**

```bash
git add src/SamaEcole.Application/Grades tests/SamaEcole.IntegrationTests/OptionalSubjects/GradeEntryExemptionTests.cs
git commit -m "feat(options): feuilles de notes, import et saisie excluent les élèves dispensés

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 6: Enregistrer et lire le choix d'options d'une inscription

**Files:**
- Create: `src/SamaEcole.Application/Enrollments/Commands/SetEnrollmentOptions/SetEnrollmentOptionsCommand.cs`
- Create: `src/SamaEcole.Application/Enrollments/Queries/GetEnrollmentOptions/GetEnrollmentOptionsQuery.cs`
- Modify: `src/SamaEcole.Application/Enrollments/Commands/CreateEnrollment/{CreateEnrollmentCommand,CreateEnrollmentCommandHandler}.cs`
- Modify: `src/SamaEcole.Web/Controllers/EnrollmentsController.cs`
- Test: `tests/SamaEcole.IntegrationTests/OptionalSubjects/EnrollmentOptionsTests.cs`

**Interfaces:**
- Consumes: `EnrollmentOptionsPlanner.PlanExemptionsAsync`, `.LoadLevelOptionsAsync`, `OptionSelectionRules` (Tâche 3), `EnrollmentSubjectExemption` (Tâche 1).
- Produces:
  - `SetEnrollmentOptionsCommand(Guid EnrollmentId, IReadOnlyList<Guid> SubjectIds) : IRequest<Unit>, IAuditableRequest`
  - `GetEnrollmentOptionsQuery(Guid EnrollmentId) : IRequest<EnrollmentOptionsDto>`
  - `EnrollmentOptionsDto(Guid EnrollmentId, bool HasExplicitChoice, IReadOnlyList<OptionGroupDto> Groups)` ; `OptionGroupDto(string? Group, IReadOnlyList<OptionSubjectDto> Subjects)` ; `OptionSubjectDto(Guid SubjectId, string Name, bool IsFollowed, int GradeCount)`
  - `CreateEnrollmentCommand.OptionSubjectIds : IReadOnlyList<Guid>?` (`null` = aucun choix ; `[]` = aucune option suivie)
  - routes `GET /api/v1/enrollments/{id}/options`, `PUT /api/v1/enrollments/{id}/options` (corps `{ "subjectIds": [...] }`, 204)

- [ ] **Step 1: Écrire les tests**

Créer `tests/SamaEcole.IntegrationTests/OptionalSubjects/EnrollmentOptionsTests.cs`. Jeu : une école (+ une autre pour l'isolation), une année active et une année passée, la classe « 4ème A » (Collège), un élève inscrit dans les deux années, des matières optionnelles `Espagnol`/`Arabe`/`Allemand` (groupe LV2), `PC`/`SVT` (groupe « Option scientifique »), `Dessin` (sans groupe), `Maths` obligatoire, et une note d'Arabe de l'élève. La fabrique du handler d'inscription est celle d'`EnrollmentTests` (doublure `NoOpKpiCacheService` locale au fichier, `_db.NewGenerator`).

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments.Commands.CreateEnrollment;
using SamaEcole.Application.Enrollments.Commands.SetEnrollmentOptions;
using SamaEcole.Application.Enrollments.Queries.GetEnrollmentOptions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.OptionalSubjects;

/// <summary>Cache KPI désactivé : ces tests exercent le Handler directement, hors DI (comme EnrollmentTests).</summary>
file sealed class NoOpKpiCacheService : IKpiCacheService
{
    public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken) =>
        factory(cancellationToken);

    public void Invalidate(string key) { }
}

/// <summary>
/// Matières optionnelles — le choix d'options d'une inscription : validation (un choix par groupe),
/// remplacement idempotent des dispenses, lecture, et écriture dans la même transaction que l'inscription.
/// </summary>
[Trait("Category", "MultiTenant")]
public class EnrollmentOptionsTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("98888888-8888-8888-8888-888888888888");
    private static readonly Guid Autre = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid Annee = Guid.Parse("98888888-0000-0000-0000-000000000001");
    private static readonly Guid AnneePassee = Guid.Parse("98888888-0000-0000-0000-000000000002");
    private static readonly Guid Trimestre = Guid.Parse("98888888-0000-0000-0000-0000000000d1");
    private static readonly Guid Classe = Guid.Parse("98888888-0000-0000-0000-0000000000c1");
    private static readonly Guid Eleve = Guid.Parse("98888888-0000-0000-0000-0000000000e1");
    private static readonly Guid Inscription = Guid.Parse("98888888-0000-0000-0000-0000000000f1");
    private static readonly Guid InscriptionPassee = Guid.Parse("98888888-0000-0000-0000-0000000000f2");
    private static readonly Guid CatInscription = Guid.Parse("98888888-0000-0000-0000-0000000000b1");
    private static readonly Guid Directeur = Guid.Parse("98888888-0000-0000-0000-0000000000d0");

    private static readonly Guid Maths = Guid.Parse("98888888-0000-0000-0000-0000000000a0");
    private static readonly Guid Espagnol = Guid.Parse("98888888-0000-0000-0000-0000000000a1");
    private static readonly Guid Arabe = Guid.Parse("98888888-0000-0000-0000-0000000000a2");
    private static readonly Guid Allemand = Guid.Parse("98888888-0000-0000-0000-0000000000a3");
    private static readonly Guid Pc = Guid.Parse("98888888-0000-0000-0000-0000000000a4");
    private static readonly Guid Svt = Guid.Parse("98888888-0000-0000-0000-0000000000a5");
    private static readonly Guid Dessin = Guid.Parse("98888888-0000-0000-0000-0000000000a6");

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(new School { Id = Ecole, Name = "Collège A" }, new School { Id = Autre, Name = "Collège B" });
        owner.SchoolYears.AddRange(
            new SchoolYear { Id = Annee, SchoolId = Ecole, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true },
            new SchoolYear { Id = AnneePassee, SchoolId = Ecole, Label = "2025-2026", StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) });
        owner.Terms.Add(new Term
        {
            Id = Trimestre, SchoolId = Ecole, SchoolYearId = Annee, Label = "1er trimestre", Order = 1,
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 12, 20)
        });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 });

        owner.FeeCategories.Add(new FeeCategory { Id = CatInscription, SchoolId = Ecole, Name = "Inscription", IsRecurring = false });
        owner.ClassFees.Add(new ClassFee { SchoolId = Ecole, FeeCategoryId = CatInscription, ClassroomId = Classe, Amount = 10_000m });

        owner.Subjects.AddRange(
            new Subject { Id = Maths, SchoolId = Ecole, Name = "Mathématiques", Level = "Collège", Coefficient = 4 },
            Option(Espagnol, "Espagnol", "LV2"), Option(Arabe, "Arabe", "LV2"), Option(Allemand, "Allemand", "LV2"),
            Option(Pc, "PC", "Option scientifique"), Option(Svt, "SVT", "Option scientifique"),
            Option(Dessin, "Dessin", null));

        owner.Students.Add(new Student
        {
            Id = Eleve, SchoolId = Ecole, Matricule = "ELEV-0001", FullName = "Awa Fall",
            BirthDate = new DateOnly(2011, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe
        });
        owner.Enrollments.AddRange(
            NewEnrollment(Inscription, Annee, "R-1"), NewEnrollment(InscriptionPassee, AnneePassee, "R-0"));
        owner.Grades.Add(new Grade
        {
            SchoolId = Ecole, StudentId = Eleve, SubjectId = Arabe, TermId = Trimestre,
            EvaluationType = EvaluationType.Composition, Value = 10
        });

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static Subject Option(Guid id, string name, string? group) => new()
    {
        Id = id, SchoolId = Ecole, Name = name, Level = "Collège", Coefficient = 2, IsOptional = true, OptionGroup = group
    };

    private static Enrollment NewEnrollment(Guid id, Guid yearId, string receipt) => new()
    {
        Id = id, SchoolId = Ecole, StudentId = Eleve, SchoolYearId = yearId, ClassroomId = Classe,
        Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, ReceiptNumber = receipt
    };

    private async Task SetForAsync(Guid enrollmentId, params Guid[] subjectIds)
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var handler = new SetEnrollmentOptionsCommandHandler(
            ctx, new StubTenantProvider(Ecole), new TestCurrentUser(Directeur, Role.Directeur));

        await handler.Handle(new SetEnrollmentOptionsCommand(enrollmentId, subjectIds), default);
    }

    private Task SetAsync(params Guid[] subjectIds) => SetForAsync(Inscription, subjectIds);

    private async Task<List<Guid>> ExemptedAsync(Guid? enrollmentId = null)
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var id = enrollmentId ?? Inscription;
        return await ctx.EnrollmentSubjectExemptions.AsNoTracking()
            .Where(x => x.EnrollmentId == id)
            .Select(x => x.SubjectId)
            .ToListAsync();
    }

    private async Task<EnrollmentOptionsDto> QueryAsync()
    {
        await using var ctx = _db.NewAppContext(Ecole);
        return await new GetEnrollmentOptionsQueryHandler(ctx)
            .Handle(new GetEnrollmentOptionsQuery(Inscription), default);
    }

    [Fact]
    public async Task Choosing_One_Subject_Per_Group_Exempts_The_Others_And_Is_Idempotent()
    {
        await SetAsync(Espagnol, Pc, Dessin);
        await SetAsync(Espagnol, Pc, Dessin); // 2e appel identique : aucun doublon, aucune erreur

        (await ExemptedAsync()).Should().BeEquivalentTo([Arabe, Allemand, Svt]);
    }

    [Fact]
    public async Task Changing_The_Choice_Soft_Deletes_The_Old_Exemptions_And_Adds_The_New_Ones()
    {
        await SetAsync(Espagnol, Pc, Dessin);
        await SetAsync(Arabe, Svt, Dessin);

        (await ExemptedAsync()).Should().BeEquivalentTo([Espagnol, Allemand, Pc]);

        await using var owner = _db.NewOwnerContext();
        (await owner.EnrollmentSubjectExemptions.IgnoreQueryFilters()
            .CountAsync(x => x.EnrollmentId == Inscription && x.IsDeleted))
            .Should().BeGreaterThan(0, "les lignes retirées sont supprimées logiquement, jamais physiquement (règle #6)");
    }

    [Fact]
    public async Task An_Empty_Choice_Exempts_Every_Option_Of_The_Level()
    {
        await SetAsync();

        (await ExemptedAsync()).Should().BeEquivalentTo([Espagnol, Arabe, Allemand, Pc, Svt, Dessin]);
    }

    [Fact]
    public async Task Two_Subjects_Of_The_Same_Group_Are_Refused()
    {
        var act = () => SetAsync(Espagnol, Arabe);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().Contain(e => e.ErrorMessage.Contains("LV2"));
        (await ExemptedAsync()).Should().BeEmpty("un choix refusé n'écrit rien");
    }

    [Fact]
    public async Task A_Mandatory_Subject_Is_Not_An_Option_And_Is_Refused()
    {
        var act = () => SetAsync(Maths);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task A_Cancelled_Enrollment_Cannot_Have_Its_Options_Changed()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            (await owner.Enrollments.FindAsync(Inscription))!.Status = EnrollmentStatus.Cancelled;
            await owner.SaveChangesAsync();
        }

        var act = () => SetAsync(Espagnol);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Only_The_Enrollment_Of_The_Active_Year_Can_Have_Its_Options_Changed()
    {
        var act = () => SetForAsync(InscriptionPassee, Espagnol);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Another_School_Cannot_Reach_The_Enrollment()
    {
        await using var ctx = _db.NewAppContext(Autre);
        var handler = new SetEnrollmentOptionsCommandHandler(
            ctx, new StubTenantProvider(Autre), new TestCurrentUser(Guid.NewGuid(), Role.Directeur));

        var act = () => handler.Handle(new SetEnrollmentOptionsCommand(Inscription, [Espagnol]), default);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task The_Options_Query_Reports_Groups_State_And_The_Grades_A_Dispense_Would_Hide()
    {
        var before = await QueryAsync();
        before.HasExplicitChoice.Should().BeFalse("aucune ligne : l'élève suit tout");
        before.Groups.SelectMany(g => g.Subjects).Should().OnlyContain(s => s.IsFollowed);

        await SetAsync(Espagnol, Pc, Dessin);
        var after = await QueryAsync();

        after.HasExplicitChoice.Should().BeTrue();
        after.Groups.Select(g => g.Group).Should().Equal(new string?[] { "LV2", "Option scientifique", null });

        var lv2 = after.Groups.Single(g => g.Group == "LV2").Subjects;
        lv2.Single(s => s.SubjectId == Arabe).Should().Match<OptionSubjectDto>(s => !s.IsFollowed && s.GradeCount == 1);
        lv2.Single(s => s.SubjectId == Espagnol).IsFollowed.Should().BeTrue();
    }

    private CreateEnrollmentCommandHandler NewCreateHandler(ApplicationDbContext db) =>
        new(db, new StubTenantProvider(Ecole), _db.NewGenerator(db), TimeProvider.System, new NoOpKpiCacheService());

    private static CreateEnrollmentCommand NewStudentCommand(IReadOnlyList<Guid>? options) => new()
    {
        Type = EnrollmentType.NewEnrollment,
        ClassroomId = Classe,
        FullName = "Modou Ndiaye",
        BirthDate = new DateOnly(2011, 5, 20),
        BirthPlace = "Dakar",
        Gender = "M",
        OptionSubjectIds = options
    };

    [Fact]
    public async Task Options_Chosen_At_Registration_Are_Recorded_With_The_Enrollment()
    {
        await using var db = _db.NewAppContext(Ecole);

        var receipt = await NewCreateHandler(db).Handle(NewStudentCommand([Espagnol]), default);

        (await ExemptedAsync(receipt.EnrollmentId)).Should().BeEquivalentTo([Arabe, Allemand, Pc, Svt, Dessin]);
    }

    [Fact]
    public async Task Without_A_Choice_At_Registration_No_Exemption_Is_Recorded()
    {
        await using var db = _db.NewAppContext(Ecole);

        var receipt = await NewCreateHandler(db).Handle(NewStudentCommand(null), default);

        (await ExemptedAsync(receipt.EnrollmentId)).Should().BeEmpty("aucun choix : l'élève suit toutes les options");
    }

    [Fact]
    public async Task An_Invalid_Choice_Creates_Neither_The_Student_Nor_The_Enrollment()
    {
        int enrollmentsBefore, studentsBefore;
        await using (var count = _db.NewAppContext(Ecole))
        {
            enrollmentsBefore = await count.Enrollments.CountAsync();
            studentsBefore = await count.Students.CountAsync();
        }

        await using var db = _db.NewAppContext(Ecole);
        var act = () => NewCreateHandler(db).Handle(NewStudentCommand([Espagnol, Arabe]), default);

        await act.Should().ThrowAsync<ValidationException>();

        await using var after = _db.NewAppContext(Ecole);
        (await after.Enrollments.CountAsync()).Should().Be(enrollmentsBefore);
        (await after.Students.CountAsync()).Should().Be(studentsBefore,
            "le choix est validé avant toute écriture : ni élève ni numéro de reçu consommés");
    }
}
```
Notes pour l'exécutant : `receipt.EnrollmentId` est le premier champ d'`EnrollmentReceiptDto` (le contrôleur s'en sert pour `CreatedAtAction`). Si `ValidationException.Errors` porte un autre nom, lire `Common/Exceptions/ValidationException.cs`. La première inscription (`Inscription`) est confirmée et l'élève y est déjà inscrit : les tests de création utilisent donc `NewEnrollment` (un **nouvel** élève), jamais `ReEnrollment` de `Eleve`.

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet build tests/SamaEcole.IntegrationTests`
Expected: FAIL — `SetEnrollmentOptionsCommand` et `GetEnrollmentOptionsQuery` n'existent pas.

- [ ] **Step 3: La commande d'écriture**

```csharp
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.OptionalSubjects;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.Enrollments.Commands.SetEnrollmentOptions;

/// <summary>
/// PUT /api/v1/enrollments/{id}/options — enregistre les matières optionnelles que l'élève SUIT. Ses
/// dispenses deviennent toutes les autres options du niveau de sa classe COURANTE (spec §5.2, écart E4) ;
/// l'ancien ensemble est remplacé dans une transaction (retrait par suppression logique, règle #6).
/// Idempotent. Refusé (422) sur une inscription annulée ou qui n'est pas celle de l'année active.
/// </summary>
public record SetEnrollmentOptionsCommand(Guid EnrollmentId, IReadOnlyList<Guid> SubjectIds)
    : IRequest<Unit>, IAuditableRequest;

public class SetEnrollmentOptionsCommandValidator : AbstractValidator<SetEnrollmentOptionsCommand>
{
    public SetEnrollmentOptionsCommandValidator()
    {
        RuleFor(x => x.EnrollmentId).NotEmpty();
        RuleFor(x => x.SubjectIds).NotNull();
    }
}

public class SetEnrollmentOptionsCommandHandler(
    IApplicationDbContext dbContext, ITenantProvider tenantProvider, ICurrentUserService currentUser)
    : IRequestHandler<SetEnrollmentOptionsCommand, Unit>
{
    public async Task<Unit> Handle(SetEnrollmentOptionsCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Le Global Query Filter + la RLS bornent la recherche à l'école courante : une inscription d'une
        // autre école renvoie 404, jamais une modification silencieuse.
        var enrollment = await dbContext.Enrollments.AsNoTracking()
            .Where(e => e.Id == request.EnrollmentId)
            .Select(e => new { e.Id, e.StudentId, e.SchoolYearId, e.Status })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Inscription {request.EnrollmentId} introuvable.");

        if (enrollment.Status == EnrollmentStatus.Cancelled)
        {
            throw Invalid(nameof(request.EnrollmentId),
                "Cette inscription est annulée : ses options ne peuvent plus être modifiées.");
        }

        if (!await dbContext.SchoolYears.AnyAsync(y => y.Id == enrollment.SchoolYearId && y.IsActive, cancellationToken))
        {
            throw Invalid(nameof(request.EnrollmentId),
                "Seules les options de l'inscription de l'année scolaire active peuvent être modifiées.");
        }

        var level = await CurrentLevelAsync(enrollment.StudentId, cancellationToken);

        var exempted = await EnrollmentOptionsPlanner.PlanExemptionsAsync(
            dbContext, level, request.SubjectIds, nameof(request.SubjectIds), cancellationToken);

        var existing = await dbContext.EnrollmentSubjectExemptions
            .Where(x => x.EnrollmentId == enrollment.Id)
            .ToListAsync(cancellationToken);

        // Lignes en trop (choix changé, matière d'un ancien niveau, matière devenue obligatoire) : retirées
        // logiquement. Lignes manquantes : ajoutées. Le reste ne bouge pas — d'où l'idempotence.
        foreach (var stale in existing.Where(x => !exempted.Contains(x.SubjectId)))
        {
            stale.SoftDelete(actorId.ToString());
        }

        var present = existing.Select(x => x.SubjectId).ToHashSet();
        foreach (var subjectId in exempted.Where(id => !present.Contains(id)))
        {
            dbContext.EnrollmentSubjectExemptions.Add(new EnrollmentSubjectExemption
            {
                SchoolId = schoolId, EnrollmentId = enrollment.Id, SubjectId = subjectId
            });
        }

        // Une violation de l'index unique (deux secrétaires en même temps) est traduite par
        // SaveChangesAsync en ConcurrencyConflictException → 409, jamais un doublon silencieux.
        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }

    /// <summary>Niveau de la classe COURANTE de l'élève (Student.ClassroomId) : c'est elle que lisent les feuilles de notes.</summary>
    private async Task<string> CurrentLevelAsync(Guid studentId, CancellationToken cancellationToken) =>
        await (from s in dbContext.Students.AsNoTracking()
               join c in dbContext.Classrooms.AsNoTracking() on s.ClassroomId equals c.Id
               where s.Id == studentId
               select c.Level).FirstOrDefaultAsync(cancellationToken)
        ?? string.Empty;

    private static ValidationException Invalid(string field, string message) =>
        new([new ValidationFailure(field, message)]);
}
```
Vérifier les `using` en compilant : `ValidationException` maison (`SamaEcole.Application.Common.Exceptions`) est en conflit de nom avec celle de FluentValidation — l'alias ci-dessus tranche, comme les autres fichiers de ce dossier le font en n'important pas `FluentValidation` en entier (si le conflit gêne, ne garder de FluentValidation que `AbstractValidator` via `using FluentValidation;` et supprimer l'alias en qualifiant `FluentValidation.Results.ValidationFailure`). `IAuditableRequest` se trouve dans `SamaEcole.Application.Common.Interfaces` (comme dans `DeleteCoefficientOverrideCommand`).

- [ ] **Step 4: La requête de lecture**

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.OptionalSubjects;

namespace SamaEcole.Application.Enrollments.Queries.GetEnrollmentOptions;

/// <summary>
/// GET /api/v1/enrollments/{id}/options — les matières optionnelles du niveau de la classe courante de
/// l'élève, groupées, avec l'état de chacune (suivie / dispensée) et le nombre de notes de l'année qu'une
/// dispense masquerait. <see cref="EnrollmentOptionsDto.HasExplicitChoice"/> est faux tant qu'aucune
/// dispense n'existe : l'élève suit alors TOUTES les options (comportement d'avant).
/// </summary>
public record GetEnrollmentOptionsQuery(Guid EnrollmentId) : IRequest<EnrollmentOptionsDto>;

public record EnrollmentOptionsDto(Guid EnrollmentId, bool HasExplicitChoice, IReadOnlyList<OptionGroupDto> Groups);

/// <summary><see cref="Group"/> est null pour une option sans groupe (cumulable) : une entrée par matière.</summary>
public record OptionGroupDto(string? Group, IReadOnlyList<OptionSubjectDto> Subjects);

public record OptionSubjectDto(Guid SubjectId, string Name, bool IsFollowed, int GradeCount);

public class GetEnrollmentOptionsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetEnrollmentOptionsQuery, EnrollmentOptionsDto>
{
    public async Task<EnrollmentOptionsDto> Handle(GetEnrollmentOptionsQuery request, CancellationToken cancellationToken)
    {
        var enrollment = await dbContext.Enrollments.AsNoTracking()
            .Where(e => e.Id == request.EnrollmentId)
            .Select(e => new { e.Id, e.StudentId, e.SchoolYearId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Inscription {request.EnrollmentId} introuvable.");

        var level = await (from s in dbContext.Students.AsNoTracking()
                           join c in dbContext.Classrooms.AsNoTracking() on s.ClassroomId equals c.Id
                           where s.Id == enrollment.StudentId
                           select c.Level).FirstOrDefaultAsync(cancellationToken)
                    ?? string.Empty;

        var options = await EnrollmentOptionsPlanner.LoadLevelOptionsAsync(dbContext, level, cancellationToken);

        var exemptedIds = (await dbContext.EnrollmentSubjectExemptions.AsNoTracking()
                .Where(x => x.EnrollmentId == enrollment.Id)
                .Select(x => x.SubjectId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var optionIds = options.Select(o => o.Id).ToList();
        var gradeCounts = await (
            from g in dbContext.Grades.AsNoTracking()
            join t in dbContext.Terms.AsNoTracking() on g.TermId equals t.Id
            where g.StudentId == enrollment.StudentId
                  && t.SchoolYearId == enrollment.SchoolYearId
                  && optionIds.Contains(g.SubjectId)
            group g by g.SubjectId into grouped
            select new { SubjectId = grouped.Key, Count = grouped.Count() })
            .ToDictionaryAsync(x => x.SubjectId, x => x.Count, cancellationToken);

        OptionSubjectDto ToDto(OptionSubject o) =>
            new(o.Id, o.Name, !exemptedIds.Contains(o.Id), gradeCounts.GetValueOrDefault(o.Id));

        // Groupes d'abord (déjà triés par LoadLevelOptionsAsync), puis les options sans groupe, une par entrée.
        var groups = options
            .Where(o => o.Group is not null)
            .GroupBy(o => o.Group!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new OptionGroupDto(g.Key, g.Select(ToDto).ToList()))
            .Concat(options.Where(o => o.Group is null)
                .Select(o => new OptionGroupDto(null, [ToDto(o)])))
            .ToList();

        return new EnrollmentOptionsDto(enrollment.Id, exemptedIds.Count > 0, groups);
    }
}
```
Limite connue, à consigner dans la spec (Tâche 9) : un choix qui ne dispense de rien (toutes les options sans groupe cochées et une seule option par groupe *quand il n'y en a qu'une*) est indiscernable de « aucun choix » — `HasExplicitChoice` reste faux ; sans conséquence sur le calcul.

- [ ] **Step 5: Le choix à l'inscription**

`CreateEnrollmentCommand` — ajouter, à la suite du bloc Internat :

```csharp

    // --- Matières optionnelles (Options d'un niveau : LV2, option scientifique) ---

    /// <summary>
    /// Les options que l'élève SUIT. <c>null</c> (défaut) : aucun choix enregistré, l'élève suit toutes les
    /// options. Liste vide : aucune option suivie. Les dispenses sont écrites dans la même transaction que
    /// l'inscription (règle #3, comme le matricule).
    /// </summary>
    public IReadOnlyList<Guid>? OptionSubjectIds { get; init; }
```

`CreateEnrollmentCommandHandler` — `using SamaEcole.Application.OptionalSubjects;` et `using System.Collections.Frozen;`. Après le contrôle Internat (avant `var receipt = await dbContext.ExecuteInTransactionAsync(`) :

```csharp
        // Matières optionnelles : le choix est validé AVANT toute écriture — un choix invalide ne doit ni
        // créer l'élève ni consommer un numéro de reçu. null = aucun choix (l'élève suit tout).
        var optionExemptions = request.OptionSubjectIds is null
            ? FrozenSet<Guid>.Empty
            : await EnrollmentOptionsPlanner.PlanExemptionsAsync(
                dbContext, classroom.Level, request.OptionSubjectIds, nameof(request.OptionSubjectIds), cancellationToken);
```
puis, juste après `dbContext.Enrollments.Add(enrollment);` :

```csharp

            foreach (var subjectId in optionExemptions)
            {
                dbContext.EnrollmentSubjectExemptions.Add(new EnrollmentSubjectExemption
                {
                    SchoolId = schoolId, EnrollmentId = enrollment.Id, SubjectId = subjectId
                });
            }
```
(`FrozenSet<Guid>.Empty` et `IReadOnlySet<Guid>` : les deux branches du ternaire doivent partager un type — si le compilateur proteste, typer explicitement `IReadOnlySet<Guid> optionExemptions`.)

- [ ] **Step 6: Les routes**

`EnrollmentsController.cs` — `using SamaEcole.Application.Enrollments.Commands.SetEnrollmentOptions;`, `using SamaEcole.Application.Enrollments.Queries.GetEnrollmentOptions;`, `using SamaEcole.Web.Authorization;`. Ajouter à côté de `ChangeEnrollmentStatusRequest` :

```csharp
    public record SetEnrollmentOptionsRequest(IReadOnlyList<Guid> SubjectIds);
```
et deux actions à la suite de `Receipt` :

```csharp
    /// <summary>
    /// Options (LV2, option scientifique) du niveau de l'élève et état de chacune. Lecture ouverte à tout
    /// utilisateur de l'école, comme le reçu ; module Pédagogie requis, comme les matières elles-mêmes.
    /// </summary>
    [HttpGet("{id:guid}/options")]
    [RequireModule(SchoolModule.Pedagogy)]
    [ProducesResponseType<EnrollmentOptionsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Options(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetEnrollmentOptionsQuery(id), cancellationToken));

    /// <summary>Enregistre les options que l'élève suit ; les autres options du niveau deviennent ses dispenses.</summary>
    [HttpPut("{id:guid}/options")]
    [Authorize(Roles = EnrollmentWriters)]
    [RequireModule(SchoolModule.Pedagogy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetOptions(
        Guid id, [FromBody] SetEnrollmentOptionsRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new SetEnrollmentOptionsCommand(id, request.SubjectIds ?? []), cancellationToken);
        return NoContent();
    }
```
`SchoolModule` est dans `SamaEcole.Domain.Enums` (déjà importé par ce contrôleur). Vérifier le namespace exact de `RequireModuleAttribute` : `grep -n "^namespace" src/SamaEcole.Web/Authorization/RequireModuleAttribute.cs`.

- [ ] **Step 7: Lancer les tests ciblés**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~EnrollmentOptionsTests|FullyQualifiedName~EnrollmentTests|FullyQualifiedName~EnrollmentBoardingTests"`
Expected: PASS (les tests d'inscription existants restent verts : `OptionSubjectIds` est facultatif).

- [ ] **Step 8: Commit**

```bash
git add src/SamaEcole.Application/Enrollments src/SamaEcole.Web/Controllers/EnrollmentsController.cs \
  tests/SamaEcole.IntegrationTests/OptionalSubjects/EnrollmentOptionsTests.cs
git commit -m "feat(options): choix d'options d'une inscription (API et transaction d'inscription)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 7: Front — logique partagée et écran Matières

**Files:**
- Create: `src/SamaEcole.Web/wwwroot/js/subject-options.js`
- Modify: `src/SamaEcole.Web/wwwroot/js/subjects.js` (`newSubject` l. 160, `openCreate` l. ~463-487 et ~659-661, `openEdit` l. ~704-716, `saveSubject` l. ~606-620)
- Modify: `src/SamaEcole.Web/Views/Subjects/Index.cshtml` (formulaires création l. ~576-580 et édition l. ~671-674, ligne de liste l. ~246)
- Test: `src/SamaEcole.Web/tests/js/subject-options.test.mjs`, `src/SamaEcole.Web/tests/js/subjects-optional.test.mjs`

**Interfaces:**
- Produces (`window.subjectOptions`) :
  - `groupsForLevel(subjects, level) → [{ key, label, exclusive, subjects: [{ id, name }] }]` — groupes triés par libellé, puis options sans groupe (une entrée chacune, `exclusive: false`, `label: null`)
  - `choose(group, selected, subjectId) → string[]` — `selected` est le tableau des ids suivis ; groupe exclusif : remplace l'éventuel autre choix du groupe ; option sans groupe : bascule
  - `clearGroup(group, selected) → string[]` — retire les ids du groupe
  - `unchosenGroupLabels(groups, selected) → string[]` — libellés des groupes exclusifs sans choix
  - `hiddenGradeCount(groups, selected) → number` — somme des `gradeCount` des matières non suivies (pour l'alerte de la fiche élève)

- [ ] **Step 1: Écrire le test de la logique pure**

`subject-options.test.mjs` :

```js
/**
 * Choix d'options d'un élève (`window.subjectOptions`, wwwroot/js/subject-options.js) — logique PURE,
 * partagée par l'écran d'inscription et l'onglet « Options » de la fiche élève : groupes d'un niveau,
 * exclusivité au sein d'un groupe, options cumulables, alerte sur les notes qu'un choix masquerait.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, plain } from './harness.mjs';

const so = () => loadScripts(['subject-options.js']).window.subjectOptions;

const SUBJECTS = [
    { id: 'maths', name: 'Maths', level: 'Collège', isOptional: false, optionGroup: null },
    { id: 'esp', name: 'Espagnol', level: 'Collège', isOptional: true, optionGroup: 'LV2' },
    { id: 'ara', name: 'Arabe', level: ' collège ', isOptional: true, optionGroup: ' lv2 ' },
    { id: 'pc', name: 'PC', level: 'Collège', isOptional: true, optionGroup: 'Option scientifique' },
    { id: 'des', name: 'Dessin', level: 'Collège', isOptional: true, optionGroup: null },
    { id: 'lat', name: 'Latin', level: 'Lycée', isOptional: true, optionGroup: 'LV2' }
];

test('les groupes d\'un niveau ignorent la casse, écartent les autres niveaux et les matières obligatoires', () => {
    const groups = plain(so().groupsForLevel(SUBJECTS, 'Collège'));

    assert.deepEqual(groups.map((g) => [g.label, g.exclusive, g.subjects.map((s) => s.id)]), [
        ['LV2', true, ['ara', 'esp']],
        ['Option scientifique', true, ['pc']],
        [null, false, ['des']]
    ]);
});

test('choisir dans un groupe exclusif remplace l\'autre choix du même groupe', () => {
    const lib = so();
    const groups = lib.groupsForLevel(SUBJECTS, 'Collège');
    const lv2 = groups[0];

    const first = lib.choose(lv2, [], 'esp');
    assert.deepEqual(plain(first), ['esp']);
    assert.deepEqual(plain(lib.choose(lv2, first, 'ara')), ['ara']);
    assert.deepEqual(plain(lib.choose(lv2, ['esp', 'pc'], 'ara')), ['pc', 'ara']);
});

test('une option sans groupe se coche et se décoche librement', () => {
    const lib = so();
    const dessin = lib.groupsForLevel(SUBJECTS, 'Collège')[2];

    const on = lib.choose(dessin, ['esp'], 'des');
    assert.deepEqual(plain(on), ['esp', 'des']);
    assert.deepEqual(plain(lib.choose(dessin, on, 'des')), ['esp']);
});

test('clearGroup retire le choix d\'un groupe sans toucher aux autres', () => {
    const lib = so();
    const lv2 = lib.groupsForLevel(SUBJECTS, 'Collège')[0];

    assert.deepEqual(plain(lib.clearGroup(lv2, ['esp', 'pc'])), ['pc']);
});

test('les groupes exclusifs sans choix sont signalés, pas les options libres', () => {
    const lib = so();
    const groups = lib.groupsForLevel(SUBJECTS, 'Collège');

    assert.deepEqual(plain(lib.unchosenGroupLabels(groups, ['esp'])), ['Option scientifique']);
    assert.deepEqual(plain(lib.unchosenGroupLabels(groups, ['esp', 'pc'])), []);
});

test('le nombre de notes masquées additionne les matières que le choix ne suit pas', () => {
    const lib = so();
    const groups = [
        { key: 'lv2', label: 'LV2', exclusive: true, subjects: [{ id: 'esp', name: 'Espagnol', gradeCount: 0 }, { id: 'ara', name: 'Arabe', gradeCount: 4 }] },
        { key: 'des', label: null, exclusive: false, subjects: [{ id: 'des', name: 'Dessin', gradeCount: 2 }] }
    ];

    assert.equal(lib.hiddenGradeCount(groups, ['esp', 'des']), 4);
    assert.equal(lib.hiddenGradeCount(groups, []), 6);
    assert.equal(lib.hiddenGradeCount(groups, ['ara', 'des']), 0);
});
```

- [ ] **Step 2: Vérifier l'échec**

Run: `npm test --prefix src/SamaEcole.Web -- --test-name-pattern="groupes|choisir|option sans groupe|clearGroup|masquées"` (ou `node --test src/SamaEcole.Web/tests/js/subject-options.test.mjs`)
Expected: FAIL — `subject-options.js` est introuvable (ENOENT).

- [ ] **Step 3: Implémenter `subject-options.js`**

```js
/**
 * Choix d'options d'un élève (LV2, option scientifique) — logique PURE, sans DOM ni réseau.
 *
 * Partagée par l'écran d'inscription (enrollments.js) et l'onglet « Options » de la fiche élève
 * (students.js) : une seule définition de ce qu'est un groupe, de l'exclusivité et du décompte des notes
 * qu'un choix masquerait. AUCUNE règle métier n'est décidée ici : le serveur (OptionSelectionRules) reste
 * seul juge d'un choix, ce module ne sert qu'à ne pas laisser l'utilisateur composer un choix qu'il
 * refuserait. Le niveau et le groupe sont des textes libres, comparés sans casse ni espaces de bord —
 * exactement comme côté serveur.
 */
(function () {
    'use strict';

    const norm = (value) => (value ?? '').trim().toLowerCase();

    /**
     * Groupes d'options d'un niveau : `[{ key, label, exclusive, subjects: [{ id, name, … }] }]`.
     * Les groupes nommés (exclusifs) d'abord, par libellé ; puis chaque option sans groupe, seule dans son
     * entrée (`exclusive: false`, `label: null`). Une matière obligatoire ou d'un autre niveau n'en fait pas partie.
     */
    function groupsForLevel(subjects, level) {
        const options = (subjects || [])
            .filter((s) => s.isOptional && !s.parentSubjectId && norm(s.level) === norm(level));

        const byGroup = new Map();
        options.filter((s) => norm(s.optionGroup) !== '').forEach((s) => {
            const key = norm(s.optionGroup);
            if (!byGroup.has(key)) byGroup.set(key, { key, label: s.optionGroup.trim(), exclusive: true, subjects: [] });
            byGroup.get(key).subjects.push({ id: s.id, name: s.name });
        });

        const named = [...byGroup.values()]
            .map((g) => ({ ...g, subjects: g.subjects.sort((a, b) => a.name.localeCompare(b.name, 'fr')) }))
            .sort((a, b) => a.label.localeCompare(b.label, 'fr'));

        const free = options
            .filter((s) => norm(s.optionGroup) === '')
            .sort((a, b) => a.name.localeCompare(b.name, 'fr'))
            .map((s) => ({ key: `free:${s.id}`, label: null, exclusive: false, subjects: [{ id: s.id, name: s.name }] }));

        return [...named, ...free];
    }

    const idsOf = (group) => group.subjects.map((s) => s.id);

    /** Ajoute `subjectId` au choix : remplace l'autre choix d'un groupe exclusif, bascule une option libre. */
    function choose(group, selected, subjectId) {
        const current = selected || [];
        if (group.exclusive) {
            return [...current.filter((id) => !idsOf(group).includes(id)), subjectId];
        }
        return current.includes(subjectId) ? current.filter((id) => id !== subjectId) : [...current, subjectId];
    }

    /** Retire de `selected` toutes les matières du groupe. */
    function clearGroup(group, selected) {
        return (selected || []).filter((id) => !idsOf(group).includes(id));
    }

    /** Libellés des groupes exclusifs où rien n'est choisi : l'élève n'y suivra aucune matière. */
    function unchosenGroupLabels(groups, selected) {
        const current = selected || [];
        return groups
            .filter((g) => g.exclusive && !idsOf(g).some((id) => current.includes(id)))
            .map((g) => g.label);
    }

    /** Notes de l'année que ce choix masquerait : somme de `gradeCount` des matières non suivies. */
    function hiddenGradeCount(groups, selected) {
        const current = selected || [];
        return groups
            .flatMap((g) => g.subjects)
            .filter((s) => !current.includes(s.id))
            .reduce((sum, s) => sum + (s.gradeCount || 0), 0);
    }

    window.subjectOptions = { groupsForLevel, choose, clearGroup, unchosenGroupLabels, hiddenGradeCount };
})();
```

- [ ] **Step 4: Vérifier que la logique passe**

Run: `node --test src/SamaEcole.Web/tests/js/subject-options.test.mjs`
Expected: PASS (6 tests).

- [ ] **Step 5: Écrire le test de l'écran Matières**

`subjects-optional.test.mjs` — reprendre le `subjectsView(rows)` de `subjects-cycle-filter.test.mjs` (même `loadScripts(['subjects.js'], { preload: … })`, avec un `api.put` espion) :

```js
/**
 * Écran Matières — matières optionnelles. Le PUT d'une matière est un REMPLACEMENT COMPLET : un champ omis
 * retombe à sa valeur par défaut. Ce fichier verrouille que le drapeau et le groupe repartent à chaque
 * enregistrement, y compris ceux qui ne les concernent pas (réordonner, entêtes).
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

const ARABE = {
    id: 's-ara', name: 'Arabe', level: 'Collège', coefficient: 2, rowVersion: '1',
    parentSubjectId: null, maxScore: null, displayOrder: 0, isOptional: true, optionGroup: 'LV2'
};

async function view(rows = [ARABE]) {
    const puts = [];
    const posts = [];
    const ctx = loadScripts(['subjects.js'], {
        preload: {
            auth: { role: 'Directeur' },
            api: {
                get: async () => rows,
                put: async (url, body) => { puts.push({ url, body }); return {}; },
                post: async (url, body) => { posts.push({ url, body }); return {}; },
                toMessage: (_e, fallback) => fallback,
                toFieldErrors: (_e, fallback) => ({ global: fallback })
            },
            location: { href: 'https://localhost/matieres', search: '' },
            history: { replaceState() {} }
        }
    });
    const component = ctx.component('subjectsView');
    await flush();
    return { component, puts, posts };
}

test('un enregistrement qui ne concerne pas les options renvoie quand même le drapeau et le groupe', async () => {
    const { component, puts } = await view();

    await component.saveSubject(ARABE, { displayOrder: 3 });

    assert.equal(puts[0].url, '/subjects/s-ara');
    assert.equal(puts[0].body.isOptional, true);
    assert.equal(puts[0].body.optionGroup, 'LV2');
    assert.equal(puts[0].body.displayOrder, 3);
});

test('l\'édition recopie le drapeau et le groupe de la matière', async () => {
    const { component } = await view();

    component.openEdit(ARABE);

    assert.equal(component.editing.isOptional, true);
    assert.equal(component.editing.optionGroup, 'LV2');
});

test('la création part matière obligatoire, sans groupe', async () => {
    const { component, posts } = await view();

    component.openCreate();
    component.newSubject.name = 'Espagnol';
    component.newSubject.level = 'Collège';
    await component.submitCreate();

    assert.equal(posts[0].body.isOptional, false);
    assert.equal(posts[0].body.optionGroup, null, 'un groupe vide part en null, jamais en chaîne vide');
});

test('décocher « optionnelle » vide le groupe avant l\'envoi', async () => {
    const { component, puts } = await view();

    component.openEdit(ARABE);
    component.editing.isOptional = false;
    await component.submitEdit();

    assert.equal(puts[0].body.isOptional, false);
    assert.equal(puts[0].body.optionGroup, null);
});
```

- [ ] **Step 6: Vérifier l'échec**

Run: `node --test src/SamaEcole.Web/tests/js/subjects-optional.test.mjs`
Expected: FAIL — `isOptional` absent du corps du PUT et de `editing`.

- [ ] **Step 7: Adapter `subjects.js`**

1. `newSubject` (l. 160) : ajouter `isOptional: false, optionGroup: ''` ; idem dans les deux réinitialisations de `openCreate` (l. ~463-487 : `openCreateActivity`/équivalents — les deux littéraux `this.newSubject = {` qui listent `nameAr: ''` reçoivent `isOptional: false, optionGroup: ''`) et dans celui de l. ~659-661.
2. `openEdit` (l. ~704-716) : ajouter `isOptional: subject.isOptional === true, optionGroup: subject.optionGroup ?? ''`.
3. `saveSubject` (l. ~606-620) : ajouter au `payload`, **avant** `...patch` :
```js
                isOptional: subject.isOptional === true,
                // Le groupe n'a de sens que pour une option : décocher « optionnelle » le vide.
                optionGroup: subject.isOptional === true ? subject.optionGroup : null,
```
4. `submitCreate` (l. ~687) : envoyer `blankToNull({ ...this.newSubject, optionGroup: this.newSubject.isOptional ? this.newSubject.optionGroup : null })`. `blankToNull` transforme déjà `''` en `null`.
5. Suggestions de groupes existants : ajouter au composant

```js
        /** Groupes d'options déjà utilisés (suggestions du champ « Groupe d'options »), sans doublon de casse. */
        get existingOptionGroups() {
            const seen = new Map();
            this.subjects.forEach((s) => {
                const group = (s.optionGroup ?? '').trim();
                if (s.isOptional && group && !seen.has(group.toLowerCase())) seen.set(group.toLowerCase(), group);
            });
            return [...seen.values()].sort((a, b) => a.localeCompare(b, 'fr'));
        },
```

- [ ] **Step 8: Vues**

Dans `Views/Subjects/Index.cshtml`, après le bloc « Coefficient » de la **création** (l. ~580) puis de l'**édition** (l. ~674), insérer (adapter `newSubject`/`createErrors`, `editing`/`editErrors` et les `id`) :

```html
   @* Matière optionnelle (LV2, option scientifique) : un élève peut en être dispensé à l'inscription.
      Réservée aux matières de premier niveau — le serveur refuse une activité de domaine. *@
   <div x-show="!newSubject.parentSubjectId" x-cloak class="space-y-3">
    <label class="flex items-start gap-2.5 cursor-pointer select-none">
     <input type="checkbox" x-model="newSubject.isOptional" class="checkbox-field mt-0.5">
     <span class="text-sm text-slate-700">
      Matière optionnelle / au choix
      <span class="block text-xs text-slate-400">Chaque élève choisit ses options à l'inscription ; une matière non suivie n'apparaît ni à la saisie des notes ni sur le bulletin.</span>
     </span>
    </label>

    <div x-show="newSubject.isOptional" x-cloak>
     <label for="subject-option-group" class="form-label">Groupe d'options</label>
     <input id="subject-option-group" type="text" x-model="newSubject.optionGroup" maxlength="50" list="subject-option-groups"
      placeholder="ex. LV2, Option scientifique — un seul choix par groupe"
      class="input-field" :class="createErrors.optiongroup && 'input-field-error'">
     <datalist id="subject-option-groups">
      <template x-for="group in existingOptionGroups" :key="group"><option :value="group"></option></template>
     </datalist>
     <p class="text-xs text-slate-400 mt-1">Laissez vide pour une option cumulable (l'élève peut la suivre en plus d'une autre).</p>
     <p x-show="createErrors.optiongroup" x-cloak x-text="createErrors.optiongroup" class="field-error"></p>
    </div>
   </div>
```
et son pendant `editing.isOptional` / `editing.optionGroup` / `editErrors.optiongroup` (ids `edit-subject-option-group`, `datalist` partagée). Les clés d'erreur sont en minuscules (`toFieldErrors`) : c'est la convention du fichier (`formErrors.boardingstatus` dans Enrollments).

Dans la ligne de liste (l. ~246, à côté de `<badge … x-text="subject.level">`) et son pendant l. ~312 :

```html
         <badge x-show="subject.isOptional" x-cloak variant="info" class="shrink-0 text-[10px]"
          x-text="'Option' + (subject.optionGroup ? ' · ' + subject.optionGroup : '')"></badge>
```
(vérifier que `variant="info"` existe : `grep -rn "variant" src/SamaEcole.Web/TagHelpers/BadgeTagHelper.cs` ; sinon `neutral`.)

Ajouter la balise de script dans `Views/Subjects/Index.cshtml` après `subjects.js` : **inutile** — l'écran Matières n'utilise pas `subject-options.js`. Ne charger `subject-options.js` que dans Enrollments et Students (Tâche 8).

- [ ] **Step 9: Lancer les tests JS**

Run: `npm test --prefix src/SamaEcole.Web`
Expected: PASS, y compris les tests existants `subjects-cycle-filter.test.mjs` et `regression-guards.test.mjs`.

- [ ] **Step 10: Commit**

```bash
git add src/SamaEcole.Web/wwwroot/js/subject-options.js src/SamaEcole.Web/wwwroot/js/subjects.js \
  src/SamaEcole.Web/Views/Subjects/Index.cshtml src/SamaEcole.Web/tests/js/subject-options.test.mjs \
  src/SamaEcole.Web/tests/js/subjects-optional.test.mjs
git commit -m "feat(options): case « matière optionnelle » et groupe d'options sur l'écran Matières

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 8: Front — choix à l'inscription et onglet « Options » de la fiche élève

**Files:**
- Modify: `src/SamaEcole.Web/wwwroot/js/enrollments.js` (état l. ~62, `loadReferenceData` l. 157-193, `submit` l. 320-350, réinitialisation l. ~392-415)
- Modify: `src/SamaEcole.Web/Views/Enrollments/Index.cshtml` (script l. 8 ; bloc après « Classe redoublée », l. ~197)
- Modify: `src/SamaEcole.Web/wwwroot/js/students.js` (état l. ~70, `openDetail` l. 395-415)
- Modify: `src/SamaEcole.Web/Views/Students/Index.cshtml` (script l. 27-28, onglets l. ~414-436, panneau après l'onglet paiements)
- Test: `src/SamaEcole.Web/tests/js/enrollments-options.test.mjs`, `src/SamaEcole.Web/tests/js/students-options.test.mjs`

**Interfaces:**
- Consumes: `window.subjectOptions` (Tâche 7), `GET /subjects`, `GET/PUT /enrollments/{id}/options`, `POST /enrollments` avec `optionSubjectIds`.
- Produces (composant `enrollmentsView`) : `optionSubjects`, `optionSelection` + `optionSelectionFor` (`null` = non renseigné ; la sélection n'est valable que pour la classe où elle a été composée), `optionGroups`, `effectiveOptionSelection`, `unchosenOptionGroups` (getters), `chooseOption(group, subjectId)`, `clearOptionGroup(group)`, `optionsPayload() → {} | { optionSubjectIds: string[] }`.
- Produces (composant `studentsView`) : `optionsPanel = { loading, error, enrollmentId, groups, selected, hasExplicitChoice, dirty, saving, saved }`, `canManageOptions`, `activeEnrollmentEntry` (getter), `openOptionsTab()`, `chooseStudentOption(group, id)`, `clearStudentOptionGroup(group)`, `hiddenOptionGrades` (getter), `unchosenOptionGroups` (getter), `saveOptions()`.

- [ ] **Step 1: Écrire le test de l'inscription**

`enrollments-options.test.mjs`. Le composant n'est **pas** initialisé par `component()` : son `init()` pose un `$watch` Alpine (absent du harnais) et lit l'URL, sans rapport avec ce qu'on teste. On instancie la fabrique, puis on charge les données de référence par `loadReferenceData()` — exactement ce que fait `init()` :

```js
/**
 * Écran d'inscription — bloc « Langues & options ». Le choix n'est transmis que si le secrétaire l'a
 * réellement composé POUR LA CLASSE CHOISIE : tant qu'il n'y touche pas — ou s'il change de classe après —
 * `optionSubjectIds` est OMIS et l'élève suit toutes les options (rien ne change pour une école sans option).
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, plain } from './harness.mjs';

const CLASSROOMS = [
    { id: 'c-4a', name: '4ème A', level: 'Collège', cycle: 'College' },
    { id: 'c-ts2', name: 'Terminale S2', level: 'Lycée', cycle: 'Lycee' }
];
const SUBJECTS = [
    { id: 'esp', name: 'Espagnol', level: 'Collège', isOptional: true, optionGroup: 'LV2' },
    { id: 'ara', name: 'Arabe', level: 'Collège', isOptional: true, optionGroup: 'LV2' },
    { id: 'lat', name: 'Latin', level: 'Lycée', isOptional: true, optionGroup: 'LV2' },
    { id: 'maths', name: 'Maths', level: 'Collège', isOptional: false, optionGroup: null }
];

async function enrollments({ subjects = SUBJECTS, subjectsFail = false } = {}) {
    const posts = [];
    const ctx = loadScripts(['subject-options.js', 'enrollments.js'], {
        preload: {
            auth: { role: 'Secretariat' },
            api: {
                get: async (url) => {
                    if (url === '/subjects') {
                        if (subjectsFail) throw new Error('403');
                        return subjects;
                    }
                    return {
                        '/classrooms': CLASSROOMS,
                        '/school-years': [{ id: 'y1', label: '2026-2027', isActive: true }],
                        '/schools/current/settings': { tuitionMonthsPerYear: 9, isInternatEnabled: false }
                    }[url] ?? [];
                },
                post: async (url, body) => { posts.push({ url, body }); return { enrollmentId: 'e1' }; },
                toMessage: (_e, fallback) => fallback,
                toFieldErrors: (_e, fallback) => ({ global: fallback })
            }
        }
    });
    const view = ctx.initAlpine().get('enrollmentsView')();
    await view.loadReferenceData();
    return { view, posts };
}

test('les groupes proposés sont ceux du niveau de la classe choisie', async () => {
    const { view } = await enrollments();

    view.form.classroomId = 'c-4a';
    assert.deepEqual(plain(view.optionGroups).map((g) => [g.label, g.subjects.map((s) => s.id)]),
        [['LV2', ['ara', 'esp']]]);

    view.form.classroomId = 'c-ts2';
    assert.deepEqual(plain(view.optionGroups).map((g) => g.subjects.map((s) => s.id)), [['lat']]);
});

test('sans toucher au bloc, le choix est omis du payload', async () => {
    const { view } = await enrollments();
    view.form.classroomId = 'c-4a';

    assert.deepEqual(plain(view.optionsPayload()), {});
});

test('un choix composé est transmis, exclusivité comprise', async () => {
    const { view } = await enrollments();
    view.form.classroomId = 'c-4a';
    const lv2 = view.optionGroups[0];

    view.chooseOption(lv2, 'esp');
    view.chooseOption(lv2, 'ara');

    assert.deepEqual(plain(view.optionsPayload()), { optionSubjectIds: ['ara'] });
    assert.deepEqual(plain(view.unchosenOptionGroups), []);
});

test('changer de classe abandonne le choix : retour à « non renseigné », rien n\'est transmis', async () => {
    const { view } = await enrollments();
    view.form.classroomId = 'c-4a';
    view.chooseOption(view.optionGroups[0], 'esp');

    view.form.classroomId = 'c-ts2';

    assert.equal(view.effectiveOptionSelection, null);
    assert.deepEqual(plain(view.optionsPayload()), {},
        'un choix composé pour une autre classe ne doit ni être envoyé ni dispenser des options du nouveau niveau');
});

test('vider un groupe rend explicite « aucune matière » et le signale', async () => {
    const { view } = await enrollments();
    view.form.classroomId = 'c-4a';
    const lv2 = view.optionGroups[0];

    view.clearOptionGroup(lv2);

    assert.deepEqual(plain(view.optionsPayload()), { optionSubjectIds: [] });
    assert.deepEqual(plain(view.unchosenOptionGroups), ['LV2']);
});

test('l\'inscription envoie le choix avec la commande', async () => {
    const { view, posts } = await enrollments();
    view.mode = 'NewEnrollment';
    view.form.classroomId = 'c-4a';
    view.form.fullName = 'Awa Ndiaye';
    view.chooseOption(view.optionGroups[0], 'esp');

    await view.submit();

    assert.equal(posts[0].url, '/enrollments');
    assert.deepEqual(plain(posts[0].body.optionSubjectIds), ['esp']);
});

test('une école sans option ne voit aucun bloc et n\'envoie rien', async () => {
    const { view, posts } = await enrollments({ subjects: [] });
    view.form.classroomId = 'c-4a';
    view.form.fullName = 'Awa Ndiaye';

    assert.equal(view.optionGroups.length, 0);
    await view.submit();

    assert.equal('optionSubjectIds' in posts[0].body, false);
});

test('sans le module Pédagogie (/subjects refusé), l\'écran reste utilisable et sans bloc', async () => {
    const { view } = await enrollments({ subjectsFail: true });

    assert.equal(view.error, null, 'le refus de /subjects ne doit pas bloquer l\'inscription');
    view.form.classroomId = 'c-4a';
    assert.equal(view.optionGroups.length, 0);
    assert.ok(view.classrooms.length > 0, 'les données de référence de l\'inscription sont chargées quand même');
});
```

- [ ] **Step 2: Vérifier l'échec**

Run: `node --test src/SamaEcole.Web/tests/js/enrollments-options.test.mjs`
Expected: FAIL — `view.optionGroups` est `undefined` (getter absent). Si l'échec vient plutôt de la fabrique (`window.api`, `window.formDraft` manquants à la création), ajouter au `preload` ce que le message d'erreur réclame : ne pas modifier `enrollments.js` pour le test.

- [ ] **Step 3: Adapter `enrollments.js`**

1. État (après `availableRooms`) :

```js
        // Matières optionnelles (LV2, option scientifique). `optionSubjects` : toutes les matières lues sur
        // /subjects — vide si l'école n'a pas le module Pédagogie (403) ou aucune option. `optionSelection` :
        // les options SUIVIES, composées pour la classe `optionSelectionFor` ; null = « non renseigné »
        // (l'élève suit toutes les options, rien n'est transmis).
        optionSubjects: [],
        optionSelection: null,
        optionSelectionFor: null,
```
2. `loadReferenceData` : lire les matières **sans jamais bloquer l'écran** — juste après l'affectation de `this.classrooms`/`activeYear` (dans le `try`, après le `Promise.all`) :

```js
                // Les matières sont facultatives ici : sans le module Pédagogie l'API répond 403, et
                // l'inscription doit rester possible (aucun bloc « options » ne s'affiche alors).
                this.optionSubjects = await window.api.get('/subjects').catch(() => []);
```
3. Méthodes (à côté de `boardingPayload`) :

```js
        /** Groupes d'options du niveau de la classe choisie (vide si aucune option). */
        get optionGroups() {
            const classroom = this.classrooms.find((c) => c.id === this.form.classroomId);
            return classroom ? window.subjectOptions.groupsForLevel(this.optionSubjects, classroom.level) : [];
        },

        /**
         * La sélection valable pour la classe ACTUELLEMENT choisie : null (« non renseigné ») si elle a été
         * composée pour une autre classe. Changer de classe abandonne donc le choix — un choix composé
         * pour un autre niveau ne doit ni être envoyé, ni dispenser des options du nouveau.
         */
        get effectiveOptionSelection() {
            return this.optionSelectionFor === this.form.classroomId ? this.optionSelection : null;
        },

        chooseOption(group, subjectId) {
            this.optionSelection = window.subjectOptions.choose(group, this.effectiveOptionSelection ?? [], subjectId);
            this.optionSelectionFor = this.form.classroomId;
        },

        clearOptionGroup(group) {
            this.optionSelection = window.subjectOptions.clearGroup(group, this.effectiveOptionSelection ?? []);
            this.optionSelectionFor = this.form.classroomId;
        },

        /** Groupes exclusifs où rien n'est choisi, pour l'avertissement « aucune matière suivie dans… ». */
        get unchosenOptionGroups() {
            const selection = this.effectiveOptionSelection;
            return selection === null ? [] : window.subjectOptions.unchosenGroupLabels(this.optionGroups, selection);
        },

        /** Le choix à joindre à la commande — VIDE tant que le secrétaire n'a pas touché au bloc de cette classe. */
        optionsPayload() {
            const selection = this.effectiveOptionSelection;
            return selection === null || this.optionGroups.length === 0 ? {} : { optionSubjectIds: selection };
        },
```
4. `submit()` : ajouter `...this.optionsPayload()` après `...this.boardingPayload()` dans **les deux** commandes (`ReEnrollment` et `NewEnrollment`).
5. Réinitialisation (l. ~392-415, à côté de `this.form = {...}`) : `this.optionSelection = null; this.optionSelectionFor = null;`.

- [ ] **Step 4: La vue d'inscription**

`Views/Enrollments/Index.cshtml` : ajouter `<script src="~/js/subject-options.js" asp-append-version="true"></script>` **avant** la ligne 8 (`enrollments.js`). Après le `</label>` de « Classe redoublée » (l. ~197), avant le bloc « Régime & Hébergement » :

```html
 @* Langues & options (matières optionnelles) : visible seulement si le niveau de la classe choisie compte des
    options. Tant que le secrétaire n'y touche pas, rien n'est transmis et l'élève suit toutes les options
    (comportement d'avant). Le serveur reste seul juge du choix (OptionSelectionRules). *@
 <div x-show="optionGroups.length > 0" x-cloak class="border-t border-slate-100 pt-4 mt-4">
  <h3 class="text-xs font-semibold text-slate-500 uppercase tracking-wider mb-1">Langues &amp; options</h3>
  <p class="text-xs text-slate-400 mb-3" x-show="effectiveOptionSelection === null">
   Non renseigné : l'élève suivra toutes les options. Faites un choix pour le limiter.
  </p>

  <div class="space-y-3">
   <template x-for="group in optionGroups" :key="group.key">
    <fieldset class="rounded-xl border border-slate-200 p-3">
     <legend class="px-1 text-sm font-semibold text-slate-700" x-text="group.exclusive ? group.label + ' — une seule matière' : 'Option'"></legend>
     <div class="flex flex-wrap gap-x-5 gap-y-2">
      <template x-for="subject in group.subjects" :key="subject.id">
       <label class="flex items-center gap-2 cursor-pointer select-none text-sm text-slate-700">
        <input :type="group.exclusive ? 'radio' : 'checkbox'" :name="'opt-' + group.key"
         class="checkbox-field" :checked="effectiveOptionSelection !== null && effectiveOptionSelection.includes(subject.id)"
         x-on:change="chooseOption(group, subject.id)">
        <span x-text="subject.name"></span>
       </label>
      </template>
     </div>
     <button type="button" x-show="group.exclusive && effectiveOptionSelection !== null" x-cloak
      x-on:click="clearOptionGroup(group)" class="mt-2 text-xs text-slate-400 underline">Aucune matière de ce groupe</button>
    </fieldset>
   </template>
  </div>

  <p x-show="unchosenOptionGroups.length > 0" x-cloak class="mt-2 text-xs text-warning">
   Aucun choix dans : <span x-text="unchosenOptionGroups.join(', ')"></span> — l'élève ne suivra aucune matière de ce groupe.
  </p>
  <p x-show="formErrors.optionsubjectids" x-cloak x-text="formErrors.optionsubjectids" class="field-error"></p>
 </div>
```
Le champ d'erreur est en minuscules (`optionsubjectids`) : c'est la clé que produit `toFieldErrors` pour `OptionSubjectIds` (à vérifier avec un 422 réel, cf. `formErrors.boardingstatus`).

- [ ] **Step 5: Vérifier l'inscription**

Run: `node --test src/SamaEcole.Web/tests/js/enrollments-options.test.mjs`
Expected: PASS (8 tests).

- [ ] **Step 6: Écrire le test de la fiche élève**

`students-options.test.mjs`. Même méthode : fabrique instanciée directement, `init()` non exécuté, la fiche chargée est posée à la main. `preload` porte `auth.canView` en plus du rôle (la fabrique de `studentsView` peut le lire à la construction ; retirer ce qui ne sert pas si le composant s'en passe).

```js
/**
 * Fiche élève › onglet « Options » (matières optionnelles) : cible l'inscription de l'année active,
 * n'invente pas de choix quand aucun n'est enregistré, avertit des notes qu'un choix masquerait, et n'envoie
 * au serveur que les matières réellement suivies.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, plain } from './harness.mjs';

const HISTORY = [
    { enrollmentId: 'e-old', schoolYearId: 'y0', schoolYearLabel: '2025-2026', status: 'Confirmed', isActiveYear: false },
    { enrollmentId: 'e-now', schoolYearId: 'y1', schoolYearLabel: '2026-2027', status: 'Confirmed', isActiveYear: true }
];

const options = (hasExplicitChoice, arabeFollowed = true) => ({
    enrollmentId: 'e-now',
    hasExplicitChoice,
    groups: [{
        group: 'LV2',
        subjects: [
            { subjectId: 'esp', name: 'Espagnol', isFollowed: true, gradeCount: 0 },
            { subjectId: 'ara', name: 'Arabe', isFollowed: arabeFollowed, gradeCount: 3 }
        ]
    }]
});

function students({ role = 'Secretariat', dto = options(false), history = HISTORY } = {}) {
    const calls = { get: [], put: [] };
    const ctx = loadScripts(['subject-options.js', 'students.js'], {
        preload: {
            auth: { role, canView: () => true },
            api: {
                get: async (url) => { calls.get.push(url); return dto; },
                put: async (url, body) => { calls.put.push({ url, body }); return {}; },
                toMessage: (_e, fallback) => fallback,
                toFieldErrors: (_e, fallback) => ({ global: fallback })
            }
        }
    });
    const view = ctx.initAlpine().get('studentsView')();
    view.studentDetail = { academicHistory: history };
    return { view, calls };
}

test('l\'onglet cible l\'inscription de l\'année active, jamais une ancienne ni une annulée', () => {
    assert.equal(students().view.activeEnrollmentEntry.enrollmentId, 'e-now');

    const cancelled = HISTORY.map((e) => (e.isActiveYear ? { ...e, status: 'Cancelled' } : e));
    assert.equal(students({ history: cancelled }).view.activeEnrollmentEntry, undefined);
});

test('sans choix enregistré, la sélection démarre vide et le formulaire n\'est pas modifié', async () => {
    const { view, calls } = students({ dto: options(false) });

    await view.openOptionsTab();

    assert.deepEqual(calls.get, ['/enrollments/e-now/options']);
    assert.equal(view.detailTab, 'options');
    assert.equal(view.optionsPanel.hasExplicitChoice, false);
    assert.deepEqual(plain(view.optionsPanel.selected), []);
    assert.equal(view.optionsPanel.dirty, false);
});

test('avec un choix enregistré, la sélection reprend les matières suivies', async () => {
    const { view } = students({ dto: options(true, false) });

    await view.openOptionsTab();

    assert.deepEqual(plain(view.optionsPanel.selected), ['esp']);
});

test('choisir marque le formulaire modifié et compte les notes qui seraient masquées', async () => {
    const { view } = students({ dto: options(false) });
    await view.openOptionsTab();

    view.chooseStudentOption(view.optionsPanel.groups[0], 'esp');

    assert.deepEqual(plain(view.optionsPanel.selected), ['esp']);
    assert.equal(view.optionsPanel.dirty, true);
    assert.equal(view.hiddenOptionGrades, 3, 'l\'Arabe (3 notes) n\'est plus suivi');
    assert.deepEqual(plain(view.unchosenOptionGroups), []);
});

test('l\'enregistrement envoie exactement les matières suivies puis recharge', async () => {
    const { view, calls } = students({ dto: options(false) });
    await view.openOptionsTab();
    view.chooseStudentOption(view.optionsPanel.groups[0], 'esp');

    await view.saveOptions();

    assert.deepEqual(plain(calls.put), [{ url: '/enrollments/e-now/options', body: { subjectIds: ['esp'] } }]);
    assert.equal(calls.get.length, 2, 'la fiche recharge l\'état serveur après l\'écriture');
    assert.equal(view.optionsPanel.dirty, false);
    assert.equal(view.optionsPanel.saved, true);
});

test('un rôle sans droit d\'écriture n\'a pas l\'onglet', () => {
    assert.equal(students({ role: 'Enseignant' }).view.canManageOptions, false);
    assert.equal(students({ role: 'Directeur' }).view.canManageOptions, true);
});
```

- [ ] **Step 7: Vérifier l'échec**

Run: `node --test src/SamaEcole.Web/tests/js/students-options.test.mjs`
Expected: FAIL — `openOptionsTab` n'existe pas.

- [ ] **Step 8: Adapter `students.js`**

À côté de `detailTab: 'history'` :

```js
        // Onglet « Options » de la fiche (matières optionnelles) : écriture réservée au Directeur et au
        // Secrétariat (confort d'affichage — la garde réelle est EnrollmentsController).
        canManageOptions: window.auth.role === 'Directeur' || window.auth.role === 'Secretariat',
        optionsPanel: {
            loading: false, error: null, enrollmentId: null, groups: [], selected: [],
            hasExplicitChoice: false, dirty: false, saving: false, saved: false
        },

        /** L'inscription dont on règle les options : celle de l'année ACTIVE, non annulée. */
        get activeEnrollmentEntry() {
            const history = this.studentDetail ? this.studentDetail.academicHistory : [];
            return history.find((e) => e.isActiveYear && e.status !== 'Cancelled');
        },

        get hiddenOptionGrades() {
            return window.subjectOptions.hiddenGradeCount(
                this.optionsPanel.groups.map((g) => this.optionsGroupView(g)), this.optionsPanel.selected);
        },

        get unchosenOptionGroups() {
            return window.subjectOptions.unchosenGroupLabels(
                this.optionsPanel.groups.map((g) => this.optionsGroupView(g)), this.optionsPanel.selected);
        },

        /** Forme attendue par subject-options.js à partir d'un groupe du DTO serveur. */
        optionsGroupView(group) {
            return {
                key: group.group ?? `free:${group.subjects[0].subjectId}`,
                label: group.group,
                exclusive: group.group !== null,
                subjects: group.subjects.map((s) => ({ id: s.subjectId, name: s.name, gradeCount: s.gradeCount }))
            };
        },

        async openOptionsTab() {
            this.detailTab = 'options';
            const entry = this.activeEnrollmentEntry;
            const panel = this.optionsPanel;
            panel.error = null;
            panel.saved = false;
            if (!entry) return;

            panel.loading = true;
            try {
                const dto = await window.api.get(`/enrollments/${entry.enrollmentId}/options`);
                panel.enrollmentId = dto.enrollmentId;
                panel.groups = dto.groups;
                panel.hasExplicitChoice = dto.hasExplicitChoice;
                // Sans choix enregistré l'élève suit tout : la sélection démarre VIDE (« non renseigné »), pour
                // ne pas présenter comme un choix ce qui n'en est pas un.
                panel.selected = dto.hasExplicitChoice
                    ? dto.groups.flatMap((g) => g.subjects.filter((s) => s.isFollowed).map((s) => s.subjectId))
                    : [];
                panel.dirty = false;
            } catch (err) {
                panel.error = window.api.toMessage(err, "Impossible de charger les options de l'élève.");
            } finally {
                panel.loading = false;
            }
        },

        chooseStudentOption(group, subjectId) {
            const panel = this.optionsPanel;
            panel.selected = window.subjectOptions.choose(this.optionsGroupView(group), panel.selected, subjectId);
            panel.dirty = true;
            panel.saved = false;
        },

        clearStudentOptionGroup(group) {
            const panel = this.optionsPanel;
            panel.selected = window.subjectOptions.clearGroup(this.optionsGroupView(group), panel.selected);
            panel.dirty = true;
            panel.saved = false;
        },

        async saveOptions() {
            const panel = this.optionsPanel;
            if (!panel.enrollmentId || panel.saving) return;

            panel.saving = true;
            panel.error = null;
            try {
                await window.api.put(`/enrollments/${panel.enrollmentId}/options`, { subjectIds: panel.selected });
                await this.openOptionsTab();
                this.optionsPanel.saved = true;
            } catch (err) {
                panel.error = window.api.toMessage(err, "Erreur lors de l'enregistrement des options.");
            } finally {
                panel.saving = false;
            }
        },
```
`openDetail` : après `this.detailTab = 'history';` ajouter la remise à zéro de l'onglet Options : `this.optionsPanel = { loading: false, error: null, enrollmentId: null, groups: [], selected: [], hasExplicitChoice: false, dirty: false, saving: false, saved: false };` (ne pas laisser l'état d'un élève sur le suivant, comme pour `reportCardError`).

- [ ] **Step 9: La vue de la fiche élève**

`Views/Students/Index.cshtml` : `<script src="~/js/subject-options.js" asp-append-version="true"></script>` avant `students.js` (l. 28). Dans la barre d'onglets (après le bouton « Paiements ») :

```html
        <button type="button" role="tab" x-show="canManageOptions && activeEnrollmentEntry" x-cloak @@click="openOptionsTab()"
                :aria-selected="detailTab === 'options'"
                class="tab-btn flex-1" :class="detailTab === 'options' ? 'tab-btn-active' : ''">
            <icon name="layers" class="w-4 h-4" />
            Options
        </button>
```
et, après le panneau `detailTab === 'payments'`, un panneau :

```html
 <!-- ONGLET 4 — Options (matières optionnelles) : réservé au Directeur et au Secrétariat. -->
 <div x-show="detailTab === 'options'" x-cloak role="tabpanel" class="py-5 space-y-4">
  <div x-show="optionsPanel.loading" x-cloak class="skeleton h-16 w-full"></div>
  <div x-show="optionsPanel.error" x-cloak class="rounded-xl border-l-4 border-danger bg-danger-bg p-3 text-sm text-danger" x-text="optionsPanel.error"></div>

  <template x-if="!optionsPanel.loading && optionsPanel.groups.length === 0 && !optionsPanel.error">
   <p class="text-sm text-slate-500">Le niveau de cette classe ne compte aucune matière optionnelle.</p>
  </template>

  <p x-show="optionsPanel.groups.length > 0 && !optionsPanel.hasExplicitChoice" x-cloak class="text-sm text-slate-500">
   Options non renseignées : l'élève suit actuellement toutes les options.
  </p>

  <template x-for="group in optionsPanel.groups" :key="optionsGroupView(group).key">
   <fieldset class="rounded-xl border border-slate-200 p-3">
    <legend class="px-1 text-sm font-semibold text-slate-700" x-text="group.group ? group.group + ' — une seule matière' : 'Option'"></legend>
    <div class="flex flex-wrap gap-x-5 gap-y-2">
     <template x-for="subject in group.subjects" :key="subject.subjectId">
      <label class="flex items-center gap-2 cursor-pointer select-none text-sm text-slate-700">
       <input :type="group.group ? 'radio' : 'checkbox'" :name="'stu-opt-' + optionsGroupView(group).key" class="checkbox-field"
        :checked="optionsPanel.selected.includes(subject.subjectId)"
        x-on:change="chooseStudentOption(group, subject.subjectId)">
       <span x-text="subject.name"></span>
       <span x-show="subject.gradeCount > 0" x-cloak class="text-xs text-slate-400" x-text="'(' + subject.gradeCount + ' note(s))'"></span>
      </label>
     </template>
    </div>
    <button type="button" x-show="group.group" x-cloak x-on:click="clearStudentOptionGroup(group)"
     class="mt-2 text-xs text-slate-400 underline">Aucune matière de ce groupe</button>
   </fieldset>
  </template>

  <p x-show="unchosenOptionGroups.length > 0" x-cloak class="text-xs text-warning">
   Aucun choix dans : <span x-text="unchosenOptionGroups.join(', ')"></span> — l'élève ne suivra aucune matière de ce groupe.
  </p>
  <p x-show="hiddenOptionGrades > 0" x-cloak class="rounded-xl border-l-4 border-warning bg-warning-bg p-3 text-sm text-warning">
   <span x-text="hiddenOptionGrades"></span> note(s) déjà saisie(s) seront masquées du bulletin et des moyennes (elles restent conservées).
  </p>

  <div class="flex items-center gap-3">
   <button type="button" x-on:click="saveOptions()" :disabled="!optionsPanel.dirty || optionsPanel.saving" class="btn-modal-primary disabled:opacity-50">
    <span x-text="optionsPanel.saving ? 'Enregistrement…' : 'Enregistrer les options'"></span>
   </button>
   <span x-show="optionsPanel.saved" x-cloak class="text-sm text-slate-500">Options enregistrées.</span>
  </div>
 </div>
```

- [ ] **Step 10: Lancer tous les tests JS et compiler le CSS**

Run:
```
npm test --prefix src/SamaEcole.Web
npm run build:css --prefix src/SamaEcole.Web
dotnet build
```
Expected: PASS ; CSS compilé sans erreur (les classes utilisées — `checkbox-field`, `btn-modal-primary`, `skeleton`, `text-warning`, `bg-warning-bg`, `border-warning` — existent déjà dans le produit).

- [ ] **Step 11: Vérification manuelle dans l'app** (obligatoire pour l'UI ; ne pas conclure sans l'avoir vue)

Run (appliquer d'abord les migrations sur la base de dev) : `dotnet run --project src/SamaEcole.Web`. Parcours : (1) Matières › modifier « Arabe » : cocher « Matière optionnelle », groupe « LV2 » ; idem « Espagnol » ; réordonner une matière et vérifier que la pastille « Option · LV2 » reste. (2) Inscriptions : choisir une classe du niveau → le bloc apparaît ; ne rien toucher → inscrire ; puis inscrire un second élève en choisissant « Espagnol ». (3) Fiche du second élève › Options : « Arabe » décoché. (4) Notes : la feuille d'Arabe ne liste pas le second élève. (5) Une école/classe sans option : aucun bloc, aucun onglet. Noter tout écart et le corriger avant de commit.

- [ ] **Step 12: Commit**

```bash
git add src/SamaEcole.Web/wwwroot/js/enrollments.js src/SamaEcole.Web/wwwroot/js/students.js \
  src/SamaEcole.Web/Views/Enrollments/Index.cshtml src/SamaEcole.Web/Views/Students/Index.cshtml \
  src/SamaEcole.Web/tests/js/enrollments-options.test.mjs src/SamaEcole.Web/tests/js/students-options.test.mjs
git commit -m "feat(options): bloc « Langues & options » à l'inscription et onglet Options de la fiche élève

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```
(le CSS compilé `wwwroot/css/site.css` : vérifier `git status` — s'il est suivi par git et modifié, l'ajouter aussi ; sinon il est généré et ignoré.)

---

### Task 9: Documentation, aide et contexte actif

**Files:**
- Modify: `docs/superpowers/specs/2026-09-25-optional-subjects-design.md` (corrections de la spec — voir « Écarts »)
- Modify: `docs/Volume_1_Cahier_des_Charges.md` (nouveau §8.8 après §8.7, avant le `---` qui précède §9)
- Modify: `docs/Volume_3_DDS.md` (nouvelle section à la suite de la dernière `### 5.N`), `docs/Volume_4_API_Design.md` (nouvelle section `## N.` à la suite de la dernière), `openapi.yaml`, `docs/Volume_7_Security.md`, `docs/design-references/README.md`
- Modify: `ACTIVE_CONTEXT.md` (nouvelle sous-section après « Coefficients par série… »), `src/SamaEcole.Web/wwwroot/js/help.js`
- Test: `src/SamaEcole.Web/tests/js/help.test.mjs` et `help-render.test.mjs` (existants, inchangés)

**Interfaces:** aucune (documentation).

- [ ] **Step 1: Corriger la spécification**

Dans `2026-09-25-optional-subjects-design.md` :
- **§1** : remplacer « Aujourd'hui, un élève qui ne suit pas l'Arabe voit donc sa ligne « Arabe » (vide) sur le bulletin, et apparaît dans la feuille de saisie de cette matière. » par : « Le bulletin secondaire n'imprime que les matières ayant une note ; la grille APC, elle, imprime toutes les lignes du niveau. Aujourd'hui, un élève qui ne suit pas l'Arabe apparaît dans la feuille de saisie de cette matière, sa note éventuelle entre dans sa moyenne, et (grille APC) sa ligne est imprimée vide. »
- **§4.1** : remplacer `SubjectExemptionLoader` par la classe statique `SubjectExemptions` (`ForStudentAsync`, `StudentsExemptFromAsync`) et retirer la phrase sur la variante par classe.
- **§4.2, ligne `ImportGradeSheet`** : « Une ligne d'élève dispensé est rejetée (erreur de ligne, message dédié) — jamais écrite. »
- **§3.2** : ajouter « Une dispense ne joue que si la matière est encore `IsOptional` à la lecture. »
- **§5.2** : « niveau de la classe **courante de l'élève** (`Student.ClassroomId`) » ; « `PUT` refusé (422) si l'inscription est annulée ou n'est pas celle de l'année active ».
- **§10.4** : remplacer par « Changement de classe : `UpdateStudentCommand` change `Student.ClassroomId` sans toucher l'inscription. Les dispenses restent rattachées à l'inscription ; les options proposées sont celles du niveau de la classe courante ; les dispenses d'un autre niveau sont retirées logiquement au prochain enregistrement. »
- **§10** : ajouter le point 5 : « Un choix qui ne dispense de rien (une seule option par groupe, toutes les options libres cochées) est indiscernable de « aucun choix » : `hasExplicitChoice` reste faux, sans effet sur le calcul. »
- Statut : « Validé, plan d'implémentation : `docs/superpowers/plans/2026-09-25-optional-subjects.md`. »

- [ ] **Step 2: Cahier des charges §8.8**

Insérer après §8.7 :

```markdown
### 8.8 Matières optionnelles et dispenses

Certaines matières sont **au choix** (LV2 : Espagnol, Arabe, Allemand ; option scientifique : PC ou SVT).
Dans Matières, la case « Matière optionnelle / au choix » les marque, et un **groupe d'options** (« LV2 »)
indique lesquelles s'excluent : un élève suit **au plus une matière par groupe** ; une option sans groupe est
cumulable.

- **Choix de l'élève.** À l'inscription (bloc « Langues & options ») ou sur la fiche élève (onglet Options),
  le Secrétariat ou le Directeur enregistre les options suivies. Les autres options du niveau deviennent des
  **dispenses** de l'inscription, donc de l'année.
- **Tant qu'aucun choix n'est enregistré**, l'élève suit toutes les options : rien ne change au déploiement ni
  à l'activation d'une option.
- **Saisie des notes.** L'élève dispensé ne figure ni dans la feuille de saisie, ni dans l'export/import Excel,
  ni sur la fiche imprimée de la matière ; une note ne peut pas lui être saisie.
- **Bulletin et moyennes.** La matière n'apparaît pas (ni ligne, ni note, ni coefficient) ; le total des
  coefficients s'adapte (ex. 22 au lieu de 25). Les notes déjà saisies sont **conservées** mais masquées.
- **Portée.** Réservé aux matières autonomes (ni domaine d'évaluation, ni activité). Repasser une matière en
  « obligatoire » la rend immédiatement à tous. Une réinscription repart sans dispense.
- **Rang.** Le classement compare les moyennes générales ; deux élèves aux options différentes sont comparés
  sur la moyenne, non sur le total de points.
```

- [ ] **Step 3: DDS, API, OpenAPI, sécurité, design**

Repérer les fins de fichier : `grep -n "^### 5\." docs/Volume_3_DDS.md | tail -1` et `grep -n "^## [0-9]*\." docs/Volume_4_API_Design.md | tail -1`.
- **Volume 3** : section « Matières optionnelles — `enrollment_subject_exemptions` + colonnes `subjects.IsOptional/OptionGroup` » : colonnes, index unique partiel `UX_enrollment_subject_exemptions_key`, FK composites `(SchoolId, EnrollmentId)` et `(SchoolId, SubjectId)` en RESTRICT, RLS `enrollment_subject_exemptions_tenant_isolation`, `GRANT SELECT, INSERT, UPDATE`, présence dans `reset_school_data`/`delete_school_year`, pas de `xmin`.
- **Volume 4** : section « API Matières optionnelles » : `GET /api/v1/enrollments/{id}/options` (200 + DTO `EnrollmentOptionsDto`, 404), `PUT /api/v1/enrollments/{id}/options` (corps `{subjectIds}`, 204, 403, 404, 409, 422), champ `optionSubjectIds` de `POST /api/v1/enrollments` (null = aucun choix), champs `isOptional`/`optionGroup` de `/api/v1/subjects` ; modules : `Pedagogy` requis sur les deux routes `options`.
- **`openapi.yaml`** : les deux routes, `EnrollmentOptionsDto`/`OptionGroupDto`/`OptionSubjectDto`, `optionSubjectIds` sur la requête d'inscription, `isOptional`/`optionGroup` sur `Subject`.
- **Volume 7** : ligne de matrice « Options d'une inscription » : écriture Directeur + Secrétariat, lecture tous rôles de l'école, Finance en lecture seule.
- **`design-references/README.md`** : une ligne — le bulletin et la fiche de saisie gardent leur mise en page (règle #12) ; seules les lignes d'une matière dispensée disparaissent.

- [ ] **Step 4: Fiche d'aide**

Dans `help.js`, à la suite de l'entrée `coefficients-par-serie` (même objet, mêmes champs — `id`, `title`, `location`, `href`, `roles`, `definition`, `objectif`, `probleme`, `procedure`, `impacts`, `recommandations`) :

```js
                {
                    id: 'matieres-optionnelles',
                    title: 'Matières optionnelles et dispenses',
                    location: 'Gestion Scolaire › Matières, Inscriptions, fiche élève › Options',
                    href: '/matieres',
                    roles: ['Directeur', 'Secrétariat'],
                    definition:
                        "Une matière optionnelle est une matière au choix — la seconde langue (Espagnol, Arabe, Allemand) ou " +
                        "l'option scientifique (PC ou SVT). Chaque élève ne suit que celles qu'il a choisies : l'autre n'apparaît " +
                        "ni dans la saisie des notes, ni sur son bulletin, ni dans sa moyenne.",
                    objectif:
                        "Éditer des bulletins et des moyennes exacts pour chaque élève, sans le coefficient ni la ligne d'une " +
                        "matière qu'il ne suit pas.",
                    probleme:
                        "Sans cela, une matière rattachée au niveau apparaissait pour toute la classe : il fallait laisser des " +
                        "lignes vides, et la moyenne pouvait être faussée par une note d'une option abandonnée.",
                    procedure: [
                        "Ouvrez Gestion Scolaire › Matières, modifiez la matière (ex. Espagnol) et cochez « Matière optionnelle / au choix ».",
                        "Renseignez le « Groupe d'options » (ex. LV2) : un élève ne pourra suivre qu'une seule matière de ce groupe. Laissez vide pour une option cumulable.",
                        "À l'inscription ou à la réinscription, le bloc « Langues & options » apparaît : choisissez la matière suivie dans chaque groupe.",
                        "Pour un élève déjà inscrit, ouvrez sa fiche › onglet Options, ajustez le choix et cliquez sur « Enregistrer les options ».",
                        "Vérifiez la saisie des notes : l'élève dispensé n'est plus listé pour la matière non suivie."
                    ],
                    impacts: [
                        "Tant qu'aucun choix n'est enregistré, l'élève suit toutes les options : rien ne change pour les élèves existants.",
                        "Bulletin et moyennes : la matière non suivie disparaît (ligne, note, coefficient) et le total des coefficients s'adapte.",
                        "Les notes déjà saisies dans une matière abandonnée sont conservées, mais masquées ; l'écran indique combien.",
                        "Le choix vaut pour l'année : à la réinscription, il faut le refaire.",
                        "Repasser une matière en « obligatoire » la rend immédiatement à tous les élèves."
                    ],
                    recommandations: [
                        "Créez les groupes d'options avant l'ouverture des inscriptions, pour que le choix soit fait dès l'inscription.",
                        "Renseignez le choix de chaque élève avant la première saisie de notes : sinon l'élève apparaît dans toutes les listes d'options.",
                        "Utilisez le même nom de groupe pour toutes les langues d'un même niveau (« LV2 ») : c'est ce nom qui les rend exclusives.",
                        "Ne marquez pas comme optionnelle une matière que toute la classe suit : elle n'y gagne rien."
                    ]
                },
```

- [ ] **Step 5: ACTIVE_CONTEXT**

Après la sous-section « Coefficients par série… », ajouter « ### Matières optionnelles et dispenses (25/09/2026) — livré » : branche `feature/optional-subjects` (issue de `main`), spec, plan, données (colonnes + table `enrollment_subject_exemptions`, RLS, purges), calcul (`SubjectExemptions`, six lecteurs), API (`/enrollments/{id}/options`), écrans, l'**invariant** « sans dispense, calcul strictement d'avant », et les points de vigilance : (1) nouvelle matière dans un groupe déjà réparti ; (2) le secrétariat doit renseigner les choix après avoir marqué une option ; (3) un choix qui ne dispense de rien ≡ « aucun choix » ; (4) `PUT /subjects/{id}` est un remplacement complet : tout client doit renvoyer `isOptional`/`optionGroup`.

- [ ] **Step 6: Lancer les tests JS de l'aide**

Run: `npm test --prefix src/SamaEcole.Web`
Expected: PASS (`help.test.mjs` détecte un identifiant dupliqué ou une entrée mal formée).

- [ ] **Step 7: Commit**

```bash
git add docs/superpowers/specs/2026-09-25-optional-subjects-design.md docs/Volume_1_Cahier_des_Charges.md \
  docs/Volume_3_DDS.md docs/Volume_4_API_Design.md docs/Volume_7_Security.md docs/design-references/README.md \
  openapi.yaml ACTIVE_CONTEXT.md src/SamaEcole.Web/wwwroot/js/help.js
git commit -m "docs(options): cahier des charges §8.8, API, DDS, aide et contexte actif

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Vérification finale (avant de proposer la fusion)

- [ ] `dotnet build` sans erreur ni avertissement nouveau.
- [ ] `npm test --prefix src/SamaEcole.Web` : tout vert.
- [ ] Tests ciblés : `--filter "FullyQualifiedName~OptionalSubjects|FullyQualifiedName~OptionSelectionRules|FullyQualifiedName~OptionalSubjectValidator"` verts.
- [ ] **À lancer par le propriétaire** (consigne du 17/09/2026) : `dotnet test` complet, dont `--filter Category=MultiTenant`, et les `FunctionalTests` (nettoyage `AuthApiFactory`).
- [ ] Parcours manuel de la Tâche 8, étape 11, revu de bout en bout.
- [ ] `git log --oneline main..HEAD` : 9 commits, aucun fichier étranger (`git diff --stat main..HEAD`) — en particulier aucun des fichiers modifiés dans l'autre répertoire de travail (`settings.js`, `help.js` du répertoire d'origine) : `help.js` est modifié ici **et** dans l'autre copie, la fusion peut demander une résolution manuelle (insertion d'une entrée, à un endroit différent).

## Self-review (spec ↔ plan)

| Exigence de la spécification | Tâche |
|---|---|
| §2 décisions 1-7 (groupes, sans choix = tout, dispenses, portée, notes conservées, matières autonomes, rôles) | 1, 2, 3, 4, 6 |
| §3.1 `IsOptional`/`OptionGroup` + validation | 2 |
| §3.2 table, RLS, unicité, purges | 1 |
| §3.3 migration | 1 |
| §4.1 résolveur unique | 3 (écart E1) |
| §4.2 six lecteurs | 4 (sommaire, fiche, structure APC), 5 (feuilles ×3, import, saisie) |
| §4.3 règles (invariant, rang, primaire) | 4 (test 1), 3 |
| §5.1 DTO matières | 2 |
| §5.2 `GET`/`PUT options`, `optionSubjectIds` | 6 |
| §6 écrans 1-4 | 7 (Matières), 8 (Inscription, fiche, saisie sans changement) |
| §6.4 ligne « N élève(s) dispensé(s) » sur la saisie | **Non planifié — décision à prendre** : l'écran de saisie (`grades.js`) n'a pas été lu ; cette ligne demande que `GetClassGrades` renvoie le nombre d'exemptés (changement de forme de la réponse). À trancher avec la validation du plan ; sinon retirée de la spécification. |
| §6.5 aide | 9 |
| §7 tests | chaque tâche |
| §8 documentation | 9 |

Cohérence des types : `OptionSubject`, `OptionSelectionRules.{NormalizeGroup, LevelMatches, Validate, ExemptedSubjectIds}`, `SubjectExemptions.{ForStudentAsync, StudentsExemptFromAsync}`, `EnrollmentOptionsPlanner.{LoadLevelOptionsAsync, PlanExemptionsAsync}` sont définis Tâche 2-3 et utilisés à l'identique Tâches 4-6 ; `window.subjectOptions.{groupsForLevel, choose, clearGroup, unchosenGroupLabels, hiddenGradeCount}` définis Tâche 7, utilisés Tâche 8.
