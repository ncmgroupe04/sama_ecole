# Dispense d'une matière obligatoire — Plan d'implémentation (v2, sur `main`)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal :** Le Directeur ou le Secrétariat peut dispenser un élève d'une matière obligatoire pour l'année active, avec un motif : la matière sort de ses moyennes et de sa saisie de notes, et le bulletin la marque « Dispensé(e) ». Sans dispense, tout est strictement comme sur `main`.

**Architecture :** Une table tenant `student_subject_exemptions` `(élève, matière, année, motif)`. La dispense est une **source d'exclusion supplémentaire de `SubjectFollowScope`** (Évolution N°6 de `main`) : `ExcludedSubjectsAsync`, `RestrictedStudentsAsync` et `EnsureFollowsAsync` l'intègrent, si bien que les lecteurs de `main` (résumé, fiche élève, grille, import, feuilles PDF/Excel, `CreateGrade`) en héritent sans changer de constructeur. Deux classes statiques (`ExemptionQueries`, `ExemptionRules`) portent les requêtes et les règles. Le résumé de notes expose les matières dispensées (`ExemptSubjects`, sans motif) pour que `ReportCardDocument` les imprime. L'API vit dans `ClassSubjectsController` (Directeur + Secrétariat, module `Pedagogy`), la section « Dispenses » dans la fiche élève.

**Tech Stack :** ASP.NET Core 9, EF Core/Npgsql (RLS, Global Query Filter), MediatR + FluentValidation, QuestPDF, Alpine.js, xUnit + FluentAssertions (Testcontainers Postgres), `node --test`.

**Spec :** `docs/superpowers/specs/2026-09-25-subject-exemptions-design.md` (v2). Elle remplace la v1 (`2026-09-25-optional-subjects-design.md`), dont le travail reste consultable sur la branche `backup/optional-subjects-v1`.

## Global Constraints

- PostgreSQL uniquement ; migration EF Core **nouvelle, générée sur la base de `main`**, jamais de migration appliquée modifiée. Lire `src/SamaEcole.Persistence/Migrations/README.md` et suivre la convention de `746672a` : scripts `docs/migrations/<id>.sql` et `<id>.rollback.sql` idempotents.
- Toute nouvelle table tenant : `SchoolId`, **Global Query Filter + policy RLS** (les deux), `GRANT SELECT, INSERT, UPDATE` (jamais `DELETE`), ajout à `reset_school_data` + `delete_school_year` par une migration de purges. Modèles : `20260924213235_AddSubjectCoefficientOverrides.cs`, `20260924213342_AddSubjectCoefficientOverridesToPurges.cs` et `20260925100215_AddClassSubjectsAndOptions.cs`.
- Aucune suppression physique (`SoftDelete(actor)`). CQRS MediatR ; aucune logique métier dans un contrôleur ni une entité. Erreurs au format normalisé (422 `ValidationException` maison, 404 `KeyNotFoundException`). `schoolId` du JWT ; l'année est l'année **active** résolue serveur (`CoefficientRules.ActiveSchoolYearIdAsync`).
- **Invariant :** sans dispense, résumé, bulletin, grilles et imports sont strictement ceux de `main`. **Aucun paramètre de constructeur ajouté** à un handler ou service existant (les handlers de `main` reçoivent déjà `SubjectFollowScope`) ; les DTO existants ne gagnent que des membres facultatifs, en dernier. Tout lecteur passe par `SubjectFollowScope`, jamais par un recalcul local.
- **Le motif d'une dispense est une donnée sensible** : jamais dans un DTO de bulletin (`ExemptSubjectDto` n'a pas de `Reason`), un document, un journal, un message d'erreur ni la console. Il n'est lu et écrit que par les deux routes réservées à `Directeur,Secretariat`.
- **Règle #12** : « Dispensé(e) » est le seul écart au bulletin de référence (validé par le propriétaire). **Aucune autre modification de `ReportCardDocument.cs`** — en particulier aucun changement d'espaces ou de commentaires sur des lignes non liées (deux avaient été relevés en v1).
- Le propriétaire lance lui-même la suite complète `dotnet test` (consigne du 17/09/2026) : ce plan n'exécute que des tests **ciblés** (`--filter`). `dotnet build` et `node --test` restent libres. Les tests d'intégration exigent Docker (Testcontainers).
- **Tests :** `RlsTestDatabase.NewOwnerContext()` a un tenant nul : le Global Query Filter y masque toute ligne pour toute **lecture ou mise à jour** (les ajouts passent) — utiliser `.IgnoreQueryFilters()` + `SingleAsync`. `ValidationException.Errors` (maison) est un `IDictionary<string, string[]>` : asserter avec `ContainKey("Champ")`. Sur la première exécution, un délai Testcontainers/Docker est possible : relancer une fois avant de diagnostiquer.
- `dotnet ef migrations add` exige `ConnectionStrings:Migrations`, dont la valeur est dans les user-secrets de l'autre copie de travail et **ne doit pas être lue** : pour **générer** (`migrations add`, `migrations script`), fournir une valeur factice par variable d'environnement (`ConnectionStrings__Migrations`) — la génération ne se connecte pas. Appliquer ensuite le script SQL généré à la base de dev par `docker exec sama-ecole-postgres psql -U sama_ecole -d sama_ecole_dev`, dans une transaction, avec l'insertion dans `__EFMigrationsHistory`.
- La base de dev `sama_ecole_dev` est **partagée** : elle porte des migrations d'autres branches (`AddCouncilRules`, `AddIefMapping`, `AddSyllabusTracking`, `AddWeeklyHourNorms`…). N'y toucher que pour retirer les objets de la v1 (Tâche 0) et ajouter les nôtres (Tâche 1).
- Conventional Commits, un commit par tâche, terminé par `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`. Staging **explicite** (jamais `git add .`). Branche : `feature/optional-subjects` (nom conservé à la demande du propriétaire), worktree `C:\Users\NCM\Documents\antigravity\sama_ecole-optional-subjects`.

## Ce que la v1 apporte encore (à réutiliser, pas à réécrire)

Le travail de la v1 est sur `backup/optional-subjects-v1`. Sont **portés tels quels ou presque** (`git show backup/optional-subjects-v1:<chemin>`) :

| Élément | Source v1 | Adaptation |
|---|---|---|
| Rendu « Dispensé(e) » des trois tableaux + `GradeRows()` + `ExemptLabel` | commit `d3e21ab` (`ReportCardDocument.cs`) | Copier les hunks **sans** les deux modifications d'espaces ; `ExemptSubjectDto` et `ReportCardDto.ExemptSubjects` identiques. |
| Tests du document | `tests/SamaEcole.UnitTests/ReportCards/ExemptSubjectReportCardTests.cs` | Inchangés. |
| Câblage `ReportCardDataService` | commit `d3e21ab` | Requête statique `ExemptionQueries.ForStudentAsync`. |
| Motif (règles) | `OptionSelectionRules.ValidateReason` (`e541e24`) | Repris dans `ExemptionRules`. |
| Logique pure des dispenses côté front | `subject-options.js` (`exemptionStateFrom`, `exemptionsPayload`, `missingReasons`, `hiddenExemptionGrades`) et ses tests | Nouveau fichier `subject-exemptions.js`. |
| Jeu de données du calcul | `OptionalSubjectsCalculationTests.cs` (`3d3b0fa`) | Réécrit sur `SubjectFollowScope`. |

## File Structure

| Fichier | Rôle |
|---|---|
| `src/SamaEcole.Domain/Entities/StudentSubjectExemption.cs` (créer) | Dispense : élève, matière, année, motif. |
| `src/SamaEcole.Persistence/Configurations/StudentSubjectExemptionConfiguration.cs`, `ApplicationDbContext.cs`, `Migrations/*`, `docs/migrations/*` | Table, RLS, purges, scripts SQL. |
| `src/SamaEcole.Application/Common/Interfaces/IApplicationDbContext.cs` | `DbSet<StudentSubjectExemption>`. |
| `src/SamaEcole.Application/Exemptions/ExemptionRules.cs`, `ExemptionQueries.cs` (créer) | Règles pures ; requêtes statiques. |
| `src/SamaEcole.Application/ClassSubjects/SubjectFollowScope.cs`, `Grades/*`, `ReportCards/*`, `StateIntegration/*` (modifier) | Dispense comme source d'exclusion ; résumé, structure APC, messages. |
| `src/SamaEcole.Infrastructure/Documents/ReportCardDocument.cs` (modifier) | Ligne « Dispensé(e) ». |
| `src/SamaEcole.Application/ClassSubjects/Queries/GetStudentExemptionsQuery.cs`, `Commands/SetStudentExemptionsCommand.cs` (créer), `Web/Controllers/ClassSubjectsController.cs` (modifier) | API. |
| `src/SamaEcole.Web/wwwroot/js/subject-exemptions.js` (créer), `students.js`, `help.js`, `Views/Students/Index.cshtml` (modifier) | Écran. |
| `tests/…/Exemptions/*`, `src/SamaEcole.Web/tests/js/*` | Tests. |

---

### Task 0: Reconstruire la branche sur `main`

Tâche de mise en place, sans code applicatif. **Prérequis : ce plan et la spec v2 sont validés par le propriétaire.**

**Files:** aucune modification de fichier applicatif.

- [ ] **Step 1: Sauvegarder l'ancienne pointe puis repartir de `main`**

Dans le worktree (`C:\Users\NCM\Documents\antigravity\sama_ecole-optional-subjects`), état propre exigé (`git status --short` vide) :

```bash
git fetch origin main
git branch backup/optional-subjects-v1            # l'ancienne pointe reste consultable
SPEC_PLAN=$(git log --format=%h -n 2 -- docs/superpowers/specs/2026-09-25-subject-exemptions-design.md docs/superpowers/plans/2026-09-25-subject-exemptions.md)
git reset --hard origin/main
git cherry-pick <les commits de la spec v2 et du plan v2, du plus ancien au plus récent>   # conserver leur historique
```
Vérifier : `git log --oneline -4` montre la spec et le plan v2 au-dessus de `origin/main`, et `git diff origin/main --stat` ne liste que ces deux fichiers.

- [ ] **Step 2: Ramener la base de dev à l'état sans objets de la v1**

La v1 a laissé dans `sama_ecole_dev` : la table `enrollment_subject_exemptions`, deux colonnes de `subjects` (`IsOptional`, `OptionGroup`), les étapes ajoutées aux fonctions de purge, et deux lignes de `__EFMigrationsHistory` (`20260925012001_AddOptionalSubjects`, `20260925012131_AddOptionalSubjectsToPurges`). Écrire dans le scratchpad un script SQL unique, exécuté **dans une transaction**, qui : (1) reprend le corps des deux `Down()` des migrations de la v1 (`git show backup/optional-subjects-v1:src/SamaEcole.Persistence/Migrations/20260925012131_AddOptionalSubjectsToPurges.cs` puis `…012001_AddOptionalSubjects.cs`), dans cet ordre ; (2) supprime les deux lignes de l'historique. Ne toucher **à aucun autre objet** de la base (elle porte les migrations d'autres branches). Exécuter par `docker exec -i sama-ecole-postgres psql -U sama_ecole -d sama_ecole_dev -v ON_ERROR_STOP=1 < script.sql`, puis vérifier :

```bash
docker exec sama-ecole-postgres psql -U sama_ecole -d sama_ecole_dev -Atc "select to_regclass('enrollment_subject_exemptions'), (select count(*) from information_schema.columns where table_name='subjects' and column_name in ('IsOptional','OptionGroup')), position('enrollment_subject_exemptions' in pg_get_functiondef('reset_school_data(uuid)'::regprocedure)), (select count(*) from \"__EFMigrationsHistory\" where \"MigrationId\" like '20260925012%')"
```
Attendu : `|0|0|0`.

- [ ] **Step 3: Vérifier la base de référence de `main`**

Run : `dotnet restore`, `npm install --prefix src/SamaEcole.Web`, `dotnet build`, puis `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~ClassSubjects"` et `npm test --prefix src/SamaEcole.Web`.
Expected : compilation sans erreur, tests verts (ce sont les tests de l'Évolution N°6 : ils prouvent que le point de départ est sain avant toute modification).

Aucun commit (les cherry-picks du Step 1 en sont l'historique).

---

### Task 1: Modèle de données, migrations et isolation

**Files:**
- Create: `src/SamaEcole.Domain/Entities/StudentSubjectExemption.cs`
- Create: `src/SamaEcole.Persistence/Configurations/StudentSubjectExemptionConfiguration.cs` (modèle : `StudentSubjectEnrollmentConfiguration.cs` de `main`)
- Modify: `src/SamaEcole.Persistence/ApplicationDbContext.cs`, `src/SamaEcole.Application/Common/Interfaces/IApplicationDbContext.cs`
- Create: `src/SamaEcole.Persistence/Migrations/<horodatage>_AddStudentSubjectExemptions.cs` (+ `.Designer.cs`), `<horodatage>_AddStudentSubjectExemptionsToPurges.cs` (+ `.Designer.cs`), `docs/migrations/<id>.sql` et `.rollback.sql` pour chacune
- Modify: `tests/SamaEcole.FunctionalTests/Common/AuthApiFactory.cs`
- Test: `tests/SamaEcole.IntegrationTests/Exemptions/StudentSubjectExemptionIsolationTests.cs`

**Interfaces:**
- Produces : `StudentSubjectExemption { Guid Id, Guid SchoolId, Guid StudentId, Guid SubjectId, Guid SchoolYearId, string Reason }` (+ `AuditableEntity`, `ITenantEntity`) ; `IApplicationDbContext.StudentSubjectExemptions : DbSet<StudentSubjectExemption>` ; table `student_subject_exemptions`.

- [ ] **Step 1: Écrire le test d'isolation (SQL brut, rôle applicatif)**

Créer `tests/SamaEcole.IntegrationTests/Exemptions/StudentSubjectExemptionIsolationTests.cs`. Jeu : deux écoles, chacune avec une année, une classe, une matière, un élève ; l'école A a une seconde année. SQL brut sous le rôle applicatif (`OpenRawAppConnectionAsync`), comme `CoefficientOverrideIsolationTests`.

```csharp
using FluentAssertions;
using Npgsql;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Exemptions;

/// <summary>
/// Dispense d'une matière obligatoire — la table <c>student_subject_exemptions</c> tient-elle son isolation, son
/// unicité et ses purges DANS LA BASE ? SQL BRUT sous le rôle applicatif, sans EF Core : seuls la policy RLS, les
/// index et les fonctions de purge font foi.
/// </summary>
[Trait("Category", "MultiTenant")]
public class StudentSubjectExemptionIsolationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("a1111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("a2222222-2222-2222-2222-222222222222");
    private static readonly Guid AnneeA1 = Guid.Parse("a1111111-0000-0000-0000-000000000001");
    private static readonly Guid AnneeA2 = Guid.Parse("a1111111-0000-0000-0000-000000000002");
    private static readonly Guid AnneeB = Guid.Parse("a2222222-0000-0000-0000-000000000001");
    private static readonly Guid ClasseA = Guid.Parse("a1111111-0000-0000-0000-0000000000c1");
    private static readonly Guid ClasseB = Guid.Parse("a2222222-0000-0000-0000-0000000000c1");
    private static readonly Guid EpsA = Guid.Parse("a1111111-0000-0000-0000-0000000000b1");
    private static readonly Guid EpsB = Guid.Parse("a2222222-0000-0000-0000-0000000000b1");
    private static readonly Guid EleveA = Guid.Parse("a1111111-0000-0000-0000-0000000000e1");
    private static readonly Guid EleveB = Guid.Parse("a2222222-0000-0000-0000-0000000000e1");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" });
        owner.SchoolYears.AddRange(
            new SchoolYear { Id = AnneeA1, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true },
            new SchoolYear { Id = AnneeA2, SchoolId = EcoleA, Label = "2025-2026", StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) },
            new SchoolYear { Id = AnneeB, SchoolId = EcoleB, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true });
        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 });
        owner.Subjects.AddRange(
            new Subject { Id = EpsA, SchoolId = EcoleA, Name = "EPS", Level = "Collège", Coefficient = 1 },
            new Subject { Id = EpsB, SchoolId = EcoleB, Name = "EPS", Level = "Collège", Coefficient = 1 });
        owner.Students.AddRange(
            new Student { Id = EleveA, SchoolId = EcoleA, Matricule = "ELEV-A1", FullName = "Awa A", BirthDate = new DateOnly(2011, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA },
            new Student { Id = EleveB, SchoolId = EcoleB, Matricule = "ELEV-B1", FullName = "Awa B", BirthDate = new DateOnly(2011, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseB });

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task RawSqlQuery_Should_Never_Return_Other_School_Exemptions()
    {
        await InsertAsync(EcoleA, EcoleA, EleveA, EpsA, AnneeA1);
        await InsertAsync(EcoleB, EcoleB, EleveB, EpsB, AnneeB);

        (await CountAsync(EcoleA)).Should().Be(1);
        (await CountAsync(EcoleB)).Should().Be(1);
    }

    [Fact]
    public async Task Session_Without_Tenant_Should_See_No_Exemption_At_All()
    {
        await InsertAsync(EcoleA, EcoleA, EleveA, EpsA, AnneeA1);

        (await CountAsync(schoolId: null)).Should().Be(0);
    }

    [Fact]
    public async Task Writing_An_Exemption_Into_Another_School_Should_Be_Rejected()
    {
        var act = async () => await InsertAsync(EcoleA, EcoleB, EleveB, EpsB, AnneeB);

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task An_Exemption_Is_Unique_Per_Student_Subject_And_Year_Until_It_Is_Soft_Deleted()
    {
        var first = await InsertAsync(EcoleA, EcoleA, EleveA, EpsA, AnneeA1);

        var duplicate = async () => await InsertAsync(EcoleA, EcoleA, EleveA, EpsA, AnneeA1);
        await duplicate.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.UniqueViolation);

        await InsertAsync(EcoleA, EcoleA, EleveA, EpsA, AnneeA2);   // autre année : aucun conflit

        await SoftDeleteAsync(EcoleA, first);
        await InsertAsync(EcoleA, EcoleA, EleveA, EpsA, AnneeA1);   // recréation permise
    }

    [Fact]
    public async Task The_Reason_Is_Mandatory_And_Bounded_By_The_Database()
    {
        await InsertAsync(EcoleA, EcoleA, EleveA, EpsA, AnneeA1, reason: new string('x', 200));

        var tooLong = async () => await InsertAsync(EcoleA, EcoleA, EleveA, EpsA, AnneeA2, reason: new string('x', 201));
        await tooLong.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.StringDataRightTruncation);

        var missing = async () => await InsertAsync(EcoleA, EcoleA, EleveA, EpsA, AnneeA2, reason: null);
        await missing.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.NotNullViolation);
    }

    [Fact]
    public async Task Deleting_A_School_Year_Removes_Its_Exemptions_And_Keeps_The_Other_Years()
    {
        await InsertAsync(EcoleA, EcoleA, EleveA, EpsA, AnneeA1);
        var oldYear = await InsertAsync(EcoleA, EcoleA, EleveA, EpsA, AnneeA2);

        await using (var connection = await _db.OpenRawAppConnectionAsync(EcoleA))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT count(*) FROM delete_school_year(@school, @year);";
            command.Parameters.AddWithValue("school", EcoleA);
            command.Parameters.AddWithValue("year", AnneeA2);
            await command.ExecuteScalarAsync();
        }

        (await CountAsync(EcoleA)).Should().Be(1);
        (await YearsOfSurvivorsAsync(EcoleA)).Should().Equal(AnneeA1);
        oldYear.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Resetting_The_School_Data_Removes_The_Exemptions_Without_A_Foreign_Key_Error()
    {
        await InsertAsync(EcoleA, EcoleA, EleveA, EpsA, AnneeA1);

        await using (var connection = await _db.OpenRawAppConnectionAsync(EcoleA))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT count(*) FROM reset_school_data(@school);";
            command.Parameters.AddWithValue("school", EcoleA);
            await command.ExecuteScalarAsync();
        }

        (await CountAsync(EcoleA)).Should().Be(0);
    }

    private async Task<Guid> InsertAsync(
        Guid sessionSchool, Guid schoolId, Guid studentId, Guid subjectId, Guid yearId, string? reason = "Inaptitude médicale")
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(sessionSchool);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO student_subject_exemptions
                ("Id", "SchoolId", "StudentId", "SubjectId", "SchoolYearId", "Reason", "CreatedAt", "IsDeleted")
            VALUES (@id, @schoolId, @studentId, @subjectId, @yearId, @reason, NOW(), FALSE);
            """;
        var id = Guid.NewGuid();
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("schoolId", schoolId);
        command.Parameters.AddWithValue("studentId", studentId);
        command.Parameters.AddWithValue("subjectId", subjectId);
        command.Parameters.AddWithValue("yearId", yearId);
        command.Parameters.AddWithValue("reason", (object?)reason ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private async Task SoftDeleteAsync(Guid schoolId, Guid id)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = """UPDATE student_subject_exemptions SET "IsDeleted" = TRUE, "DeletedAt" = NOW() WHERE "Id" = @id;""";
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> CountAsync(Guid? schoolId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT count(*) FROM student_subject_exemptions WHERE NOT "IsDeleted";""";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<List<Guid>> YearsOfSurvivorsAsync(Guid schoolId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "SchoolYearId" FROM student_subject_exemptions WHERE NOT "IsDeleted";""";
        var years = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            years.Add(reader.GetGuid(0));
        }

        return years;
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run : `dotnet build tests/SamaEcole.IntegrationTests` — Expected : le test compile (SQL brut), donc l'échec se voit à l'exécution : `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~StudentSubjectExemptionIsolationTests"` — FAIL (`relation "student_subject_exemptions" does not exist`).

- [ ] **Step 3: Domaine**

```csharp
using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Dispense d'un élève pour une matière OBLIGATOIRE, pour UNE année scolaire, avec un MOTIF. Calquée sur
/// <see cref="StudentSubjectEnrollment"/> (élève + année) : rattachée à l'élève et à l'année, pas à l'inscription.
/// « Aucune ligne » = « l'élève suit la matière » : rien ne change tant que personne n'a dispensé personne.
///
/// La matière dispensée sort des moyennes et de la saisie (SubjectFollowScope) ; le bulletin la garde, marquée
/// « Dispensé(e) ». Le <see cref="Reason"/> peut être médical : il n'est jamais imprimé ni journalisé.
///
/// Suppression logique uniquement (règle #6) : « refaire la liste » retire les lignes en trop et la clé redevient
/// libre grâce à l'index unique partiel. Pas de verrou xmin : l'écriture est un remplacement idempotent.
/// </summary>
public class StudentSubjectExemption : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid StudentId { get; set; }

    public Guid SubjectId { get; set; }

    public Guid SchoolYearId { get; set; }

    /// <summary>Motif obligatoire (200 caractères au plus). Donnée sensible.</summary>
    public string Reason { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Persistance**

Créer `StudentSubjectExemptionConfiguration.cs` en suivant **`StudentSubjectEnrollmentConfiguration.cs` de `main`** (mêmes conventions de FK composites et d'index) :

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par ApplicationDbContext.OnModelCreating
/// à toute entité ITenantEntity. La policy RLS équivalente vit dans la migration AddStudentSubjectExemptions
/// (AGENTS.md règle #2).
/// </summary>
public class StudentSubjectExemptionConfiguration : IEntityTypeConfiguration<StudentSubjectExemption>
{
    public void Configure(EntityTypeBuilder<StudentSubjectExemption> builder)
    {
        builder.ToTable("student_subject_exemptions");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.SchoolId).IsRequired();

        // Même borne que ExemptionRules.MaxReasonLength.
        builder.Property(e => e.Reason).IsRequired().HasMaxLength(200);

        // Une dispense par (élève, matière, année). Index PARTIEL : refaire la liste ne se heurte pas à la ligne archivée.
        builder.HasIndex(e => new { e.SchoolId, e.StudentId, e.SubjectId, e.SchoolYearId })
            .IsUnique()
            .HasDatabaseName("UX_student_subject_exemptions_key")
            .HasFilter("NOT \"IsDeleted\"");

        // « Quels élèves sont dispensés de cette matière cette année ? » (grilles, import, fiches).
        builder.HasIndex(e => new { e.SchoolId, e.SubjectId, e.SchoolYearId });

        builder.HasOne<School>().WithMany().HasForeignKey(e => e.SchoolId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Student>().WithMany()
            .HasForeignKey(e => new { e.SchoolId, e.StudentId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Subject>().WithMany()
            .HasForeignKey(e => new { e.SchoolId, e.SubjectId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SchoolYear>().WithMany()
            .HasForeignKey(e => new { e.SchoolId, e.SchoolYearId })
            .HasPrincipalKey(y => new { y.SchoolId, y.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```
`ApplicationDbContext.cs` : `public DbSet<StudentSubjectExemption> StudentSubjectExemptions => Set<StudentSubjectExemption>();` à côté de `StudentSubjectEnrollments`. `IApplicationDbContext.cs` : `DbSet<StudentSubjectExemption> StudentSubjectExemptions { get; }` avec son résumé XML.

- [ ] **Step 5: Migration de schéma + RLS**

Run (avec `ConnectionStrings__Migrations` factice en variable d'environnement, voir Global Constraints) :
`dotnet ef migrations add AddStudentSubjectExemptions -p src/SamaEcole.Persistence -s src/SamaEcole.Web`.
Attendu : `CreateTable student_subject_exemptions` seul (aucune autre modification : si le générateur en propose d'autres, la base de modèle n'est pas celle de `main` — s'arrêter). Éditer la migration : `TenantTables = ["student_subject_exemptions"]`, `AppRole = "sama_ecole_app"`, et à la fin de `Up` le bloc RLS **identique à `20260925100215_AddClassSubjectsAndOptions.cs`** (`ENABLE ROW LEVEL SECURITY`, policy `student_subject_exemptions_tenant_isolation` avec `USING` et `WITH CHECK`, `GRANT SELECT, INSERT, UPDATE` sans `DELETE`) ; `Down` retire la policy avant la table. Ajouter au résumé XML de la classe que les purges sont dans la migration suivante.

- [ ] **Step 6: Migration de purges**

`dotnet ef migrations add AddStudentSubjectExemptionsToPurges …` puis remplacer `Up`/`Down` par la technique de `20260924213342_AddSubjectCoefficientOverridesToPurges.cs` (lecture de `pg_get_functiondef`, insertion à une ancre, échec si l'ancre est absente ou ambiguë, idempotence). Ancres, **vérifiées uniques** sur la base de dev le 25/09/2026 (`string_to_array` → 2 segments) :
- `reset_school_data` : ancre `[''enrollments'',` (la dispense référence `students`, `subjects` et `school_years` en RESTRICT ; `enrollments` précède ces trois tables dans la liste) ; ligne insérée : `[''student_subject_exemptions'', ''Dispenses de matières''],`.
- `delete_school_year` : ancre `DELETE FROM enrollments`, étape insérée juste avant :

```sql
DELETE FROM student_subject_exemptions
WHERE "SchoolYearId" = p_school_year_id AND "SchoolId" = p_school_id;
GET DIAGNOSTICS v_deleted = ROW_COUNT;
label := 'Dispenses de matières'; rows_deleted := v_deleted; RETURN NEXT;
```
`Down` retire exactement ce que `Up` a inséré. Si `main` a redéfini ces fonctions depuis, relire la dernière migration qui les touche et adapter les ancres — le test `Every_Restrict_Foreign_Key_Into_A_Purged_Table_Must_Come_From_A_Purged_Table_Too` détecte un oubli.

- [ ] **Step 7: Scripts SQL et base de dev**

Générer, pour chacune des deux migrations, `docs/migrations/<id>.sql` (idempotent : `dotnet ef migrations script <précédente> <id> --idempotent`) et `<id>.rollback.sql` (`dotnet ef migrations script <id> <précédente>`), au format de `docs/migrations/20260925100215_AddClassSubjectsAndOptions*.sql`. Appliquer les deux scripts à `sama_ecole_dev` dans une transaction (Global Constraints), puis vérifier : `select to_regclass('student_subject_exemptions')` non nul et les deux lignes présentes dans `__EFMigrationsHistory`.

- [ ] **Step 8: Nettoyage des tests fonctionnels**

Dans `AuthApiFactory.cs`, à côté de `DELETE FROM student_subject_enrollments;` (l. ~466), **avant** les purges de `students`, `subjects` et `school_years` :
```csharp
        // Dispenses de matières : FK Restrict vers students, subjects ET school_years, donc AVANT les trois.
        await owner.Database.ExecuteSqlRawAsync("DELETE FROM student_subject_exemptions;");
```
Vérifier l'ordre : `grep -n "DELETE FROM student_subject_exemptions\|DELETE FROM students\|DELETE FROM subjects\|DELETE FROM school_years" tests/SamaEcole.FunctionalTests/Common/AuthApiFactory.cs`.

- [ ] **Step 9: Tests ciblés**

Run : `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~StudentSubjectExemptionIsolationTests|FullyQualifiedName~RlsCoverageTests|FullyQualifiedName~ResetSchoolDataTests|FullyQualifiedName~DeleteSchoolYearTests|FullyQualifiedName~ClassSubjects"`
Expected : PASS (7 tests d'isolation ; `RlsCoverageTests` et le contrôle des FK RESTRICT détectent seuls la nouvelle table ; les tests de l'Évolution N°6 restent verts).

- [ ] **Step 10: Commit**

```bash
git add src/SamaEcole.Domain/Entities/StudentSubjectExemption.cs src/SamaEcole.Persistence src/SamaEcole.Application/Common/Interfaces/IApplicationDbContext.cs \
  docs/migrations tests/SamaEcole.FunctionalTests/Common/AuthApiFactory.cs tests/SamaEcole.IntegrationTests/Exemptions
git commit -m "feat(dispenses): table des dispenses de matières obligatoires (RLS, purges)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 2: Règles pures et requêtes de dispenses

**Files:**
- Create: `src/SamaEcole.Application/Exemptions/ExemptionRules.cs`, `ExemptionQueries.cs`
- Test: `tests/SamaEcole.UnitTests/Exemptions/ExemptionRulesTests.cs`, `tests/SamaEcole.IntegrationTests/Exemptions/ExemptionQueriesTests.cs`

**Interfaces:**
- Produces (espace de noms `SamaEcole.Application.Exemptions`) :
  - `ExemptSubject(Guid SubjectId, string Name, decimal Coefficient)` ; `DispensableSubject(Guid SubjectId, string Name)` ; `SubjectExemptionInput(Guid SubjectId, string? Reason)`
  - `ExemptionRules.MaxReasonLength = 200`, `.LevelMatches(string, string) : bool`, `.ValidateReason(string?) : string?`, `.Validate(IReadOnlyList<DispensableSubject> dispensable, IReadOnlyCollection<SubjectExemptionInput> exemptions) : string?` (`null` si valide, sinon le message français à renvoyer en 422)
  - `ExemptionQueries.ForStudentAsync(IApplicationDbContext db, Guid studentId, Guid schoolYearId, CancellationToken ct) : Task<IReadOnlyList<ExemptSubject>>` (trié par nom), `.StudentsAsync(db, Guid subjectId, Guid schoolYearId, ct) : Task<IReadOnlySet<Guid>>`, `.DispensableAsync(db, Guid classroomId, ct) : Task<IReadOnlyList<DispensableSubject>>` (trié par nom)

- [ ] **Step 1: Tests des règles pures**

```csharp
using FluentAssertions;
using SamaEcole.Application.Exemptions;
using Xunit;

namespace SamaEcole.UnitTests.Exemptions;

public class ExemptionRulesTests
{
    private static readonly Guid Eps = Guid.NewGuid();
    private static readonly Guid Maths = Guid.NewGuid();
    private static readonly IReadOnlyList<DispensableSubject> Dispensable = [new(Eps, "EPS"), new(Maths, "Mathématiques")];

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_Blank_Reason_Is_Refused(string? reason)
        => ExemptionRules.ValidateReason(reason).Should().NotBeNull();

    [Fact]
    public void A_Reason_Is_Valid_Up_To_Two_Hundred_Characters_After_Trimming()
    {
        ExemptionRules.ValidateReason("  Inaptitude médicale  ").Should().BeNull();
        ExemptionRules.ValidateReason(new string('x', 200)).Should().BeNull();
        ExemptionRules.ValidateReason(new string('x', 201)).Should().NotBeNull();
    }

    [Fact]
    public void A_Dispensable_Subject_With_A_Reason_Is_Valid()
        => ExemptionRules.Validate(Dispensable, [new(Eps, "Inaptitude médicale")]).Should().BeNull();

    [Fact]
    public void No_Exemption_At_All_Is_Valid()
        => ExemptionRules.Validate(Dispensable, []).Should().BeNull();

    [Fact]
    public void A_Missing_Reason_Is_Refused_And_Names_The_Subject()
        => ExemptionRules.Validate(Dispensable, [new(Eps, null)]).Should().NotBeNull().And.Contain("EPS");

    [Fact]
    public void A_Subject_That_Is_Not_Dispensable_Is_Refused()
        => ExemptionRules.Validate(Dispensable, [new(Guid.NewGuid(), "Raison")]).Should().NotBeNull();

    [Fact]
    public void The_Same_Subject_Cannot_Be_Exempted_Twice()
        => ExemptionRules.Validate(Dispensable, [new(Eps, "A"), new(Eps, "B")]).Should().NotBeNull();

    [Theory]
    [InlineData("Collège", " collège ", true)]
    [InlineData("Terminale S2", "terminale s2", true)]
    [InlineData("Collège", "Lycée", false)]
    public void Levels_Match_Ignoring_Case_And_Edge_Spaces(string a, string b, bool expected)
        => ExemptionRules.LevelMatches(a, b).Should().Be(expected);
}
```

- [ ] **Step 2: Vérifier l'échec** — `dotnet build tests/SamaEcole.UnitTests` : FAIL (`ExemptionRules` absent).

- [ ] **Step 3: Implémenter `ExemptionRules.cs`**

```csharp
namespace SamaEcole.Application.Exemptions;

/// <summary>Une matière dispensée d'un élève, avec son coefficient de base (le coefficient effectif est calculé par le résumé).</summary>
public sealed record ExemptSubject(Guid SubjectId, string Name, decimal Coefficient);

/// <summary>Une matière que l'on peut dispenser : obligatoire et autonome, dans la classe de l'élève.</summary>
public sealed record DispensableSubject(Guid SubjectId, string Name);

/// <summary>Une dispense demandée : la matière et son motif (obligatoire, imposé par <see cref="ExemptionRules"/>).</summary>
public sealed record SubjectExemptionInput(Guid SubjectId, string? Reason);

/// <summary>
/// Règles PURES (aucun accès base) de la dispense d'une matière obligatoire : motif obligatoire et borné, matière
/// dispensable, pas de doublon. Le motif peut être médical : les messages nomment la matière, jamais le motif.
/// </summary>
public static class ExemptionRules
{
    /// <summary>Longueur maximale du motif (colonne <c>Reason</c>, varchar(200)).</summary>
    public const int MaxReasonLength = 200;

    /// <summary>
    /// Deux niveaux sont le même niveau s'ils ne diffèrent que par la casse ou les espaces de bord — la même
    /// tolérance qu'<c>EvaluationStructureBuilder</c> : le niveau est un texte libre par école.
    /// </summary>
    public static bool LevelMatches(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Null si le motif est valide (non vide, 200 caractères au plus après trim), sinon le message.</summary>
    public static string? ValidateReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "Le motif de la dispense est obligatoire.";
        }

        return reason.Trim().Length > MaxReasonLength
            ? $"Le motif ne peut pas dépasser {MaxReasonLength} caractères."
            : null;
    }

    /// <summary>
    /// Null si les dispenses sont valides : chaque matière est dispensable (jamais une matière hors classe, une
    /// option ou un domaine), n'est dispensée qu'une fois et porte un motif.
    /// </summary>
    public static string? Validate(
        IReadOnlyList<DispensableSubject> dispensable, IReadOnlyCollection<SubjectExemptionInput> exemptions)
    {
        var byId = dispensable.ToDictionary(s => s.SubjectId);

        if (exemptions.Any(e => !byId.ContainsKey(e.SubjectId)))
        {
            return "Une des matières indiquées ne peut pas être dispensée : ce n'est pas une matière obligatoire de la classe de l'élève.";
        }

        if (exemptions.GroupBy(e => e.SubjectId).Any(g => g.Count() > 1))
        {
            return "Une matière ne peut être dispensée qu'une seule fois.";
        }

        foreach (var exemption in exemptions)
        {
            if (ValidateReason(exemption.Reason) is { } message)
            {
                return $"{byId[exemption.SubjectId].Name} : {message}";
            }
        }

        return null;
    }
}
```

- [ ] **Step 4: Tests unitaires verts** — `dotnet test tests/SamaEcole.UnitTests --filter "FullyQualifiedName~ExemptionRulesTests"` → PASS (10 cas).

- [ ] **Step 5: Tests des requêtes (intégration)**

Jeu `ExemptionQueriesTests` (préfixe d'identifiants `b`) : école A (année active `Annee1`, autre année `Annee0`) et école B ; **classe A1 « 4ème A »** (Collège) **avec programme** (`ClassSubject` : Maths et EPS actifs sans groupe, Espagnol actif avec groupe « LV2 », Latin **inactif**, un domaine « Lang & Com. » avec une activité) ; **classe A2 « 5ème A »** (Collège) **sans programme** ; matières `Maths`, `EPS`, `Espagnol`, `Latin`, `LangCom` (domaine) + `Vocabulaire` (activité, `ParentSubjectId = LangCom`) au niveau « Collège » ; élèves `EleveDispense` (dispensé d'EPS pour `Annee1`, motif « Inaptitude médicale »), `EleveAnneePassee` (dispensé d'EPS pour `Annee0` seulement), `EleveLibre` ; école B : un élève dispensé de sa matière. Tests :

```csharp
    [Fact]
    public async Task Dispensable_Subjects_Of_A_Class_With_A_Program_Are_The_Active_Common_Autonomous_Ones()
    {
        await using var db = _db.NewAppContext(EcoleA);

        var subjects = await ExemptionQueries.DispensableAsync(db, ClasseAvecProgramme, default);

        subjects.Select(s => s.Name).Should().Equal("EPS", "Mathématiques");   // ni l'option Espagnol, ni Latin (inactif)
    }

    [Fact]
    public async Task Dispensable_Subjects_Of_A_Class_Without_A_Program_Are_The_Autonomous_Subjects_Of_Its_Level()
    {
        await using var db = _db.NewAppContext(EcoleA);

        var subjects = await ExemptionQueries.DispensableAsync(db, ClasseSansProgramme, default);

        subjects.Select(s => s.Name).Should().Equal("Espagnol", "EPS", "Latin", "Mathématiques");
        subjects.Select(s => s.Name).Should().NotContain(["Lang & Com.", "Vocabulaire"], "un domaine et une activité ne se dispensent pas");
    }

    [Fact]
    public async Task An_Exemption_Is_Reported_For_The_Year_It_Was_Recorded_Only()
    {
        await using var db = _db.NewAppContext(EcoleA);

        var current = await ExemptionQueries.ForStudentAsync(db, EleveDispense, Annee1, default);
        var otherYear = await ExemptionQueries.ForStudentAsync(db, EleveAnneePassee, Annee1, default);

        current.Should().ContainSingle().Which.Should().Be(new ExemptSubject(Eps, "EPS", 1m));
        otherYear.Should().BeEmpty();
    }

    [Fact]
    public async Task Students_Exempt_From_A_Subject_Are_Listed_For_That_Year()
    {
        await using var db = _db.NewAppContext(EcoleA);

        (await ExemptionQueries.StudentsAsync(db, Eps, Annee1, default)).Should().BeEquivalentTo([EleveDispense]);
        (await ExemptionQueries.StudentsAsync(db, Eps, Annee0, default)).Should().BeEquivalentTo([EleveAnneePassee]);
        (await ExemptionQueries.StudentsAsync(db, Maths, Annee1, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_Soft_Deleted_Exemption_Is_Ignored()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            var row = await owner.StudentSubjectExemptions.IgnoreQueryFilters()
                .SingleAsync(x => x.StudentId == EleveDispense && x.SchoolYearId == Annee1);
            row.SoftDelete("test");
            await owner.SaveChangesAsync();
        }

        await using var db = _db.NewAppContext(EcoleA);

        (await ExemptionQueries.ForStudentAsync(db, EleveDispense, Annee1, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Another_School_Never_Sees_These_Exemptions()
    {
        await using var db = _db.NewAppContext(EcoleB);

        (await ExemptionQueries.ForStudentAsync(db, EleveDispense, Annee1, default)).Should().BeEmpty();
        (await ExemptionQueries.StudentsAsync(db, Eps, Annee1, default)).Should().BeEmpty();
        (await ExemptionQueries.StudentsAsync(db, EpsB, AnneeB, default)).Should().BeEquivalentTo([EleveB]);
    }
```
(Le seeding — écoles, années, classes, `ClassSubjects` avec les propriétés de l'entité de `main` `SchoolId, ClassroomId, SubjectId, OptionGroup, IsActive, IsCustom, DisplayOrder`, matières, élèves, dispenses via `owner.StudentSubjectExemptions.Add(...)` — s'écrit comme `ClassSubjectsTests.InitializeAsync` de `main`, à copier pour la forme.)

- [ ] **Step 6: Vérifier l'échec** — `dotnet build tests/SamaEcole.IntegrationTests` : FAIL (`ExemptionQueries` absent).

- [ ] **Step 7: Implémenter `ExemptionQueries.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Exemptions;

/// <summary>
/// Requêtes de dispenses, STATIQUES et sans état : appelées par <c>SubjectFollowScope</c> (qui les mémoïse), par
/// <c>ReportCardDataService</c> et par les handlers de l'API — aucun paramètre de constructeur ajouté à un service
/// existant. Aucun filtre SchoolId à la main : le Global Query Filter et la policy RLS bornent tout à l'école
/// courante (règle #2) ; une dispense supprimée logiquement est déjà écartée.
/// </summary>
public static class ExemptionQueries
{
    /// <summary>Les matières dont l'élève est dispensé pour cette année, triées par nom (comme le résumé de notes).</summary>
    public static async Task<IReadOnlyList<ExemptSubject>> ForStudentAsync(
        IApplicationDbContext dbContext, Guid studentId, Guid schoolYearId, CancellationToken cancellationToken)
    {
        var rows = await (
            from x in dbContext.StudentSubjectExemptions.AsNoTracking()
            join s in dbContext.Subjects.AsNoTracking() on x.SubjectId equals s.Id
            where x.StudentId == studentId && x.SchoolYearId == schoolYearId
            select new { s.Id, s.Name, s.Coefficient })
            .ToListAsync(cancellationToken);

        return rows
            .OrderBy(r => r.Name)
            .Select(r => new ExemptSubject(r.Id, r.Name, r.Coefficient))
            .ToList();
    }

    /// <summary>Les élèves dispensés de cette matière pour cette année (feuilles de notes, import).</summary>
    public static async Task<IReadOnlySet<Guid>> StudentsAsync(
        IApplicationDbContext dbContext, Guid subjectId, Guid schoolYearId, CancellationToken cancellationToken)
    {
        var ids = await dbContext.StudentSubjectExemptions.AsNoTracking()
            .Where(x => x.SubjectId == subjectId && x.SchoolYearId == schoolYearId)
            .Select(x => x.StudentId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return ids.ToHashSet();
    }

    /// <summary>
    /// Matières dispensables d'une classe : OBLIGATOIRES et AUTONOMES (ni activité d'un domaine, ni domaine — une
    /// matière qui porte des activités n'est jamais notée). Classe AVEC programme (Évolution N°6) : ses
    /// <c>class_subjects</c> actifs sans groupe d'options. Classe SANS programme : les matières du niveau de la classe
    /// (texte libre, comparé en mémoire sans casse ni espaces de bord). Triées par nom.
    /// </summary>
    public static async Task<IReadOnlyList<DispensableSubject>> DispensableAsync(
        IApplicationDbContext dbContext, Guid classroomId, CancellationToken cancellationToken)
    {
        var program = await dbContext.ClassSubjects.AsNoTracking()
            .Where(c => c.ClassroomId == classroomId)
            .Select(c => new { c.SubjectId, c.IsActive, c.OptionGroup })
            .ToListAsync(cancellationToken);

        var autonomous = dbContext.Subjects.AsNoTracking()
            .Where(s => s.ParentSubjectId == null && !dbContext.Subjects.Any(child => child.ParentSubjectId == s.Id));

        if (program.Count > 0)
        {
            var ids = program.Where(c => c.IsActive && c.OptionGroup == null).Select(c => c.SubjectId).ToList();
            var byProgram = await autonomous
                .Where(s => ids.Contains(s.Id))
                .Select(s => new { s.Id, s.Name })
                .ToListAsync(cancellationToken);

            return byProgram.OrderBy(s => s.Name).Select(s => new DispensableSubject(s.Id, s.Name)).ToList();
        }

        var level = await dbContext.Classrooms.AsNoTracking()
            .Where(c => c.Id == classroomId)
            .Select(c => c.Level)
            .FirstOrDefaultAsync(cancellationToken);

        if (level is null)
        {
            return [];
        }

        var byLevel = (await autonomous.Select(s => new { s.Id, s.Name, s.Level }).ToListAsync(cancellationToken))
            .Where(s => ExemptionRules.LevelMatches(s.Level, level))
            .OrderBy(s => s.Name)
            .Select(s => new DispensableSubject(s.Id, s.Name))
            .ToList();

        return byLevel;
    }
}
```

- [ ] **Step 8: Tests ciblés** — `dotnet test tests/SamaEcole.UnitTests --filter "FullyQualifiedName~ExemptionRulesTests"` et `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~ExemptionQueriesTests"` → PASS.

- [ ] **Step 9: Commit**

```bash
git add src/SamaEcole.Application/Exemptions tests/SamaEcole.UnitTests/Exemptions tests/SamaEcole.IntegrationTests/Exemptions/ExemptionQueriesTests.cs
git commit -m "feat(dispenses): règles du motif et requêtes de dispenses

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 3: La dispense dans `SubjectFollowScope`, le résumé et la grille

**Files:**
- Modify: `src/SamaEcole.Application/ClassSubjects/SubjectFollowScope.cs`
- Modify: `src/SamaEcole.Application/Grades/Queries/GetGradeSummary/GetGradeSummaryQuery.cs`, `GetGradeSummaryQueryHandler.cs`
- Modify: `src/SamaEcole.Application/Grades/Commands/ImportGradeSheet/ImportGradeSheetCommandHandler.cs` (message)
- Modify: `src/SamaEcole.Application/ReportCards/EvaluationStructureBuilder.cs`, `Queries/GetReportCardPdf/GetReportCardPdfQuery.cs` (`EvaluationLineDto`, appel de `BuildAsync`), `src/SamaEcole.Application/StateIntegration/Queries/GetSkillsBookletPdf/GetSkillsBookletPdfQueryHandler.cs`
- Modify (si nécessaire pour la compilation) : `src/SamaEcole.Application/SamaEcole.Application.csproj` (`InternalsVisibleTo` de l'assembly de tests d'intégration, `EvaluationStructureBuilder` étant `internal` — déjà fait en v1 : `git show backup/optional-subjects-v1:src/SamaEcole.Application/SamaEcole.Application.csproj`)
- Test: `tests/SamaEcole.IntegrationTests/Exemptions/SubjectFollowScopeExemptionTests.cs`, `ExemptionCalculationTests.cs`

**Interfaces:**
- Consumes : `ExemptionQueries`, `ExemptSubject` (Tâche 2).
- Produces :
  - `SubjectFollowScope.ExemptionsAsync(Guid studentId, Guid schoolYearId, CancellationToken ct) : Task<IReadOnlyList<ExemptSubject>>` (mémoïsé)
  - `ExcludedSubjectsAsync` : désormais **règles de programme ∪ dispenses** ; `RestrictedStudentsAsync` : **moins les élèves dispensés** ; `EnsureFollowsAsync` : message dédié à la dispense.
  - `public record ExemptSubjectDto(Guid SubjectId, string SubjectName, decimal Coefficient)` (motif **absent**) ; `GradeSummaryDto(…, string? Mention, IReadOnlyList<ExemptSubjectDto>? ExemptSubjects = null)` ; `EvaluationLineDto(…, string? Appreciation, bool IsExempt = false)`
  - `EvaluationStructureBuilder.BuildAsync(dbContext, classroomLevel, gradingScale, gradedSubjects, mentionsOnReferenceScale, IReadOnlySet<Guid> exemptSubjectIds, cancellationToken)` — `exemptSubjectIds` inséré **avant** `cancellationToken`.

- [ ] **Step 1: Tests du scope (intégration)**

`SubjectFollowScopeExemptionTests` — jeu : une école, année active, classe « 4ème A » **avec programme** (Maths, EPS actifs sans groupe ; Espagnol et Arabe avec groupe « LV2 » ; élèves : `EleveA` a choisi Espagnol et est dispensé d'EPS, `EleveB` a choisi Arabe, `EleveC` n'est dispensé de rien et a choisi Espagnol) et classe « 5ème A » **sans programme** (`EleveD`, dispensé de Maths). Tests, avec `new SubjectFollowScope(db)` comme dans `ClassSubjectsTests` de `main` :

```csharp
    [Fact]
    public async Task Excluded_Subjects_Are_The_Program_Exclusions_Plus_The_Exemptions()
    {
        await using var db = _db.NewAppContext(Ecole);

        var excluded = await new SubjectFollowScope(db).ExcludedSubjectsAsync(EleveA, Annee, default);

        excluded.Should().BeEquivalentTo([Arabe, Eps], "l'option non choisie ET la matière dispensée");
    }

    [Fact]
    public async Task A_Class_Without_A_Program_Still_Excludes_An_Exempted_Subject()
    {
        await using var db = _db.NewAppContext(Ecole);

        (await new SubjectFollowScope(db).ExcludedSubjectsAsync(EleveD, Annee, default)).Should().BeEquivalentTo([MathsCinquieme]);
    }

    [Fact]
    public async Task A_Student_Without_Any_Exemption_Is_Excluded_From_Nothing_More_Than_Before()
    {
        await using var db = _db.NewAppContext(Ecole);

        (await new SubjectFollowScope(db).ExcludedSubjectsAsync(EleveC, Annee, default)).Should().BeEquivalentTo([Arabe]);
    }

    [Fact]
    public async Task Restricted_Students_Stay_Null_For_A_Common_Subject_Nobody_Is_Exempted_From()
    {
        await using var db = _db.NewAppContext(Ecole);

        (await new SubjectFollowScope(db).RestrictedStudentsAsync(Classe, Maths, Annee, default)).Should().BeNull();
    }

    [Fact]
    public async Task Restricted_Students_Of_A_Common_Subject_Exclude_The_Exempted_Ones()
    {
        await using var db = _db.NewAppContext(Ecole);

        var allowed = await new SubjectFollowScope(db).RestrictedStudentsAsync(Classe, Eps, Annee, default);

        allowed.Should().BeEquivalentTo([EleveB, EleveC], "toute la classe sauf l'élève dispensé");
    }

    [Fact]
    public async Task Restricted_Students_Of_An_Option_Are_The_Choosers_Minus_The_Exempted_Ones()
    {
        await using var db = _db.NewAppContext(Ecole);

        var allowed = await new SubjectFollowScope(db).RestrictedStudentsAsync(Classe, Espagnol, Annee, default);

        allowed.Should().BeEquivalentTo([EleveA, EleveC], "les élèves qui l'ont choisie ; aucun n'est dispensé de l'Espagnol");
    }

    [Fact]
    public async Task Ensure_Follows_Refuses_An_Exempted_Subject_With_A_Dedicated_Message()
    {
        await using var db = _db.NewAppContext(Ecole);

        var act = () => new SubjectFollowScope(db).EnsureFollowsAsync(EleveA, Eps, Trimestre, "SubjectId", default);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainKey("SubjectId").WhoseValue.Should().ContainMatch("*dispensé*");
    }

    [Fact]
    public async Task Exemptions_Are_Memoised_Per_Student_And_Year()
    {
        await using var db = _db.NewAppContext(Ecole);
        var scope = new SubjectFollowScope(db);

        var first = await scope.ExemptionsAsync(EleveA, Annee, default);
        var second = await scope.ExemptionsAsync(EleveA, Annee, default);

        second.Should().BeSameAs(first);
        first.Should().ContainSingle().Which.SubjectId.Should().Be(Eps);
    }
```
(`ValidationException` = `SamaEcole.Application.Common.Exceptions.ValidationException`.)

- [ ] **Step 2: Tests de calcul (intégration)**

`ExemptionCalculationTests` — jeu de `OptionalSubjectsCalculationTests` de la v1 (`git show backup/optional-subjects-v1:tests/SamaEcole.IntegrationTests/OptionalSubjects/OptionalSubjectsCalculationTests.cs`), **réécrit** : plus de `IsOptional`/`OptionGroup` sur `Subject` ni de `EnrollmentSubjectExemption` ; les dispenses sont des `StudentSubjectExemption { SchoolId, StudentId, SubjectId, SchoolYearId, Reason }` ; les handlers se construisent avec `new SubjectFollowScope(db)` (voir `ClassSubjectsTests` de `main`). Matières : Maths (coef 4, note 12), Français (2, 16), EPS (1, note 8). Élèves : `EleveLibre` (aucune dispense) ; `EleveEps` (dispensé d'EPS, motif « Inaptitude médicale », avec la note d'EPS en base). Tests à écrire :
1. `Without_Any_Exemption_The_Summary_Is_As_Before` — `EleveLibre` : trois matières, coefficients 7, points 12×4+16×2+8×1 = 88, moyenne 88/7, `ExemptSubjects` nul ou vide.
2. `An_Exempted_Subject_Leaves_The_Summary_And_The_Coefficient_Total_Adapts` — `EleveEps` : matières {Maths, Français}, coefficients 6, points 80, moyenne 80/6 ; `ExemptSubjects` = `[new ExemptSubjectDto(Eps, "EPS", 1m)]`.
3. `The_Reported_Exempt_Coefficient_Is_The_Effective_One` — une `SubjectCoefficientOverride` de classe (`ClassroomId` renseigné, `Series` nul, contrainte `CK_…_one_scope`) fixe l'EPS à 2 : `ExemptSubjects.Single().Coefficient == 2m`.
4. `The_Grade_Of_An_Exempted_Subject_Is_Kept_In_The_Database` — compte les notes d'EPS de `EleveEps` (1).
5. `An_Exemption_Of_This_Year_Never_Hides_The_Subject_In_Another_Year` — un trimestre d'une année précédente : EPS présente, coefficients 7.
6. `The_Student_Sheet_Hides_The_Same_Subject_And_Shows_The_Same_Average_As_The_Summary` — `GetStudentDetailQueryHandler(db, new TestCurrentUser(role: Role.Directeur), new CoefficientOverrideLoader(db), new SubjectFollowScope(db))` : mêmes matières et même moyenne que le résumé.
7. `The_Evaluation_Structure_Marks_An_Exempted_Line_And_Keeps_It` — domaine « Lang & Com. » + activité + matière simple « EPS CE1 » au niveau « CE1 » ; `EvaluationStructureBuilder.BuildAsync(db, "CE1", 10, [], [], new HashSet<Guid> { epsCe1 }, default)` : le groupe de l'EPS existe, sa ligne unique a `IsExempt == true` ; avec un ensemble vide, aucune ligne n'est marquée.
8. `The_Exemption_Reason_Is_Never_Part_Of_The_Bulletin_DTOs` — `typeof(ExemptSubjectDto).GetProperties().Select(p => p.Name).Should().NotContain("Reason")`.

- [ ] **Step 3: Vérifier l'échec** — `dotnet build tests/SamaEcole.IntegrationTests` : FAIL (`ExemptionsAsync`, `ExemptSubjectDto`, le nouveau paramètre de `BuildAsync` absents).

- [ ] **Step 4: `SubjectFollowScope`**

Ajouter `using SamaEcole.Application.Exemptions;` et l'état mémoïsé :
```csharp
    private readonly Dictionary<(Guid Student, Guid Year), IReadOnlyList<ExemptSubject>> _exemptions = [];

    /// <summary>
    /// Matières dont l'élève est DISPENSÉ cette année (dispense d'une matière obligatoire, avec motif). Elles sortent
    /// des moyennes et de la saisie comme une option non suivie ; le bulletin, lui, les garde, marquées. Mémoïsé.
    /// Le motif n'est jamais porté ici : c'est une donnée sensible.
    /// </summary>
    public async Task<IReadOnlyList<ExemptSubject>> ExemptionsAsync(
        Guid studentId, Guid schoolYearId, CancellationToken cancellationToken)
    {
        if (_exemptions.TryGetValue((studentId, schoolYearId), out var cached))
        {
            return cached;
        }

        var result = await ExemptionQueries.ForStudentAsync(dbContext, studentId, schoolYearId, cancellationToken);
        _exemptions[(studentId, schoolYearId)] = result;
        return result;
    }
```
`ExcludedSubjectsAsync` : après la résolution des règles de programme, réunir avec les dispenses :
```csharp
        var result = await ResolveExcludedAsync(studentId, schoolYearId, cancellationToken);

        // Dispenses d'une matière obligatoire : une SECONDE source d'exclusion, réunie ici pour que tous les lecteurs
        // (résumé, fiche, bulletins, saisie) l'appliquent sans la connaître.
        var exemptions = await ExemptionsAsync(studentId, schoolYearId, cancellationToken);
        if (exemptions.Count > 0)
        {
            result = result.Union(exemptions.Select(e => e.SubjectId)).ToHashSet();
        }

        _excluded[(studentId, schoolYearId)] = result;
        return result;
```
(en conservant le test de cache existant en tête de méthode). `RestrictedStudentsAsync` : renommer le corps actuel en méthode privée `ResolveRestrictedAsync` (inchangé) et écrire la méthode publique :
```csharp
    public async Task<IReadOnlySet<Guid>?> RestrictedStudentsAsync(
        Guid classroomId, Guid subjectId, Guid schoolYearId, CancellationToken cancellationToken)
    {
        var restricted = await ResolveRestrictedAsync(classroomId, subjectId, schoolYearId, cancellationToken);

        // Élèves dispensés de la matière : ils sortent de la grille, de l'import et des fiches. Sans dispense,
        // le résultat est celui d'avant (null = toute la classe).
        var exempt = await ExemptionQueries.StudentsAsync(dbContext, subjectId, schoolYearId, cancellationToken);
        if (exempt.Count == 0 || restricted is { Count: 0 })
        {
            return restricted;
        }

        if (restricted is not null)
        {
            return restricted.Where(id => !exempt.Contains(id)).ToHashSet();
        }

        var classStudents = await dbContext.Students.AsNoTracking()
            .Where(s => s.ClassroomId == classroomId)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        return classStudents.Where(id => !exempt.Contains(id)).ToHashSet();
    }
```
`EnsureFollowsAsync` : après le calcul de `yearId`, **avant** le contrôle générique :
```csharp
        var exemptions = await ExemptionsAsync(studentId, yearId.Value, cancellationToken);
        if (exemptions.Any(e => e.SubjectId == subjectId))
        {
            throw new ValidationException([
                new ValidationFailure(field,
                    "Cet élève est dispensé de cette matière : aucune note ne peut y être saisie. "
                    + "Retirez la dispense sur sa fiche pour saisir une note.")
            ]);
        }
```

- [ ] **Step 5: Résumé de notes, import, structure APC**

`GetGradeSummaryQuery.cs` : ajouter
```csharp
/// <summary>
/// Matière OBLIGATOIRE dont l'élève est dispensé, portée par le résumé pour que le bulletin la marque « Dispensé(e) ».
/// Elle n'entre ni dans <see cref="GradeSummaryDto.Subjects"/> ni dans les totaux. <see cref="Coefficient"/> est le
/// coefficient EFFECTIF (surcharges comprises ; 1 au primaire) : il s'imprime barré. Le MOTIF n'est volontairement
/// pas ici — il peut être médical et ne figure sur aucun document.
/// </summary>
public record ExemptSubjectDto(Guid SubjectId, string SubjectName, decimal Coefficient);
```
et un **dernier** membre optionnel à `GradeSummaryDto` : `IReadOnlyList<ExemptSubjectDto>? ExemptSubjects = null`. `GetGradeSummaryQueryHandler` : avant le `return new GradeSummaryDto(…)` :
```csharp
        // Matières obligatoires dispensées : déjà retirées du calcul par SubjectFollowScope ; on les remonte pour que le
        // bulletin les marque. Coefficient effectif comme pour les lignes notées ; neutralisé à 1 au primaire.
        var exemptSubjects = (await followScope.ExemptionsAsync(request.StudentId, schoolYearId, cancellationToken))
            .Select(e => new ExemptSubjectDto(
                e.SubjectId, e.Name, isPrimaire ? 1m : overrides.Effective(e.SubjectId, e.Coefficient)))
            .OrderBy(e => e.SubjectName)
            .ToList();
```
et `exemptSubjects` en dernier argument du `new GradeSummaryDto(…)`. `ImportGradeSheetCommandHandler` : le message de rejet d'une note devient « … cet élève ne suit pas cette matière (option non choisie, matière désactivée pour la classe ou élève dispensé). ». `EvaluationLineDto` gagne `bool IsExempt = false` en dernier ; `EvaluationStructureBuilder.BuildAsync` reçoit `IReadOnlySet<Guid> exemptSubjectIds` **avant** `cancellationToken` et le transmet à `BuildLine` (trois sites d'appel : racine, activité, orphelin) : l'`EvaluationLineDto` construit prend en dernier argument `exemptSubjectIds.Contains(subject.Id)` ; son score reste nul (la matière est absente des notes du résumé). **La ligne n'est jamais retirée** de la grille. Appelants : `ReportCardDataService.BuildAsync` (`var exempt = (await ExemptionQueries.ForStudentAsync(dbContext, student.Id, term.SchoolYearId, ct)).Select(e => e.SubjectId).ToHashSet();`) et `GetSkillsBookletPdfQueryHandler` (idem par période, le livret ignore le marquage). Reprendre les hunks de la v1 : `git show backup/optional-subjects-v1:src/SamaEcole.Application/ReportCards/EvaluationStructureBuilder.cs` (commit `3d3b0fa`), en remplaçant `StudentExemptions` par l'ensemble d'identifiants.

- [ ] **Step 6: Tests ciblés**

Run : `dotnet build` puis
`dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~SubjectFollowScopeExemptionTests|FullyQualifiedName~ExemptionCalculationTests|FullyQualifiedName~ClassSubjects|FullyQualifiedName~GradeSummaryTests|FullyQualifiedName~EffectiveCoefficientTests|FullyQualifiedName~ApcEvaluationStructureTests|FullyQualifiedName~GetStudentDetailQueryTests|FullyQualifiedName~GetReportCardPdfTests|FullyQualifiedName~GradeCorrectionTests"`
Expected : PASS. Les tests de `main` restent verts : sans dispense, chaque méthode du scope renvoie ce qu'elle renvoyait.

- [ ] **Step 7: Commit**

```bash
git add src/SamaEcole.Application tests/SamaEcole.IntegrationTests/Exemptions
git commit -m "feat(dispenses): la dispense devient une exclusion de SubjectFollowScope (résumé, grille, saisie)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 4: Bulletin PDF — la ligne « Dispensé(e) »

**Files:**
- Modify: `src/SamaEcole.Application/ReportCards/Queries/GetReportCardPdf/GetReportCardPdfQuery.cs` (`ReportCardDto`, `ReportCardDataService`)
- Modify: `src/SamaEcole.Infrastructure/Documents/ReportCardDocument.cs` (les trois tableaux)
- Test: `tests/SamaEcole.UnitTests/ReportCards/ExemptSubjectReportCardTests.cs`, `tests/SamaEcole.IntegrationTests/Exemptions/ExemptSubjectReportCardDataTests.cs`

**Interfaces:** consomme `ExemptSubjectDto`, `GradeSummaryDto.ExemptSubjects`, `EvaluationLineDto.IsExempt` (Tâche 3). Produit `ReportCardDto(…, IReadOnlyDictionary<Guid, string?>? SubjectNamesAr = null, IReadOnlyList<ExemptSubjectDto>? ExemptSubjects = null)`, `ReportCardDocument.ExemptLabel = "Dispensé(e)"`, `ReportCardDocument.GradeRows()` et `GradeRow` (`internal`).

Cette tâche est un **portage** de la v1, dont le rendu et les tests ont été relus et vérifiés visuellement.

- [ ] **Step 1: Tests du document** — copier `tests/SamaEcole.UnitTests/ReportCards/ExemptSubjectReportCardTests.cs` depuis `backup/optional-subjects-v1` (`git show backup/optional-subjects-v1:tests/SamaEcole.UnitTests/ReportCards/ExemptSubjectReportCardTests.cs`), **inchangé**. Run : `dotnet build tests/SamaEcole.UnitTests` → FAIL (`ExemptSubjects`, `GradeRows` absents).

- [ ] **Step 2: `ReportCardDto` et `ReportCardDataService`** — appliquer les hunks du commit `d3e21ab` sur `GetReportCardPdfQuery.cs` : dernier membre optionnel `ExemptSubjects` (avec son commentaire : motif jamais porté, écart règle #12 validé) ; dans `ReportCardDataService.BuildAsync`, la résolution des noms arabes concatène les identifiants des matières dispensées et `ExemptSubjects: summary.ExemptSubjects` est passé en **argument nommé, dernier**. Les dictionnaires de rangs, appréciations et T.H restent construits sur les matières notées.

- [ ] **Step 3: `ReportCardDocument`** — appliquer les hunks du commit `d3e21ab` sur `ReportCardDocument.cs` : constante `ExemptLabel`, `GradeRow`, `GradeRows()` (fusion par nom ; sans dispense, exactement `Subjects`), `RowMetricsFor(reportCard.Subjects.Count + (reportCard.ExemptSubjects?.Count ?? 0))` dans les tableaux secondaire et primaire, la ligne dispensée des trois tableaux (secondaire : 9 cellules dont `ColumnSpan(3)` « Dispensé(e) » et coefficient `.Strikethrough()` ; primaire : 6 cellules ; APC : `ColumnSpan(2)` + cellule d'appréciation vide). **Ne pas reprendre les deux modifications d'espaces** relevées à la relecture finale de la v1 (alignement des commentaires de `columns.RelativeColumn(25f);` et `columns.RelativeColumn(3.4f);`) : `git diff origin/main -- src/SamaEcole.Infrastructure/Documents/ReportCardDocument.cs` ne doit contenir **que** des lignes liées à la dispense.

- [ ] **Step 4: Test de câblage (intégration)** — copier `ExemptSubjectReportCardDataTests.cs` de la v1 et l'adapter : `StudentSubjectExemption` à la place de l'exemption d'inscription, `new SubjectFollowScope(db)` dans la doublure de médiateur (`FakeSummaryMediator`, à copier de `ApcEvaluationStructureTests`), et le test de non-fuite du motif sur `ExemptSubjectDto` déplacé dans `ExemptSubjectReportCardTests` (test unitaire).

- [ ] **Step 5: Tests ciblés**

Run : `dotnet build`, puis
`dotnet test tests/SamaEcole.UnitTests --filter "FullyQualifiedName~ExemptSubjectReportCardTests|FullyQualifiedName~ReportCardDocumentTests|FullyQualifiedName~ApcReportCardDocumentTests"` et
`dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~ExemptSubjectReportCardDataTests|FullyQualifiedName~GetReportCardPdfTests|FullyQualifiedName~ApcEvaluationStructureTests|FullyQualifiedName~GetClassReportCardsTests|FullyQualifiedName~SimenComplianceTests"`
Expected : PASS (les tests de documents existants restent verts : sans dispense, `GradeRows()` renvoie `Subjects` tel quel).

- [ ] **Step 6: Vérification visuelle** (obligatoire : document officiel) — rendre en PNG, par un test jetable **non commité**, un bulletin secondaire, un primaire et une grille APC avec l'EPS dispensée, puis les regarder : la ligne « Dispensé(e) » à sa place alphabétique, coefficient barré (secondaire), « Moy x », T.H et appréciation vides, une seule page A5. Consigner ce qui a été regardé (et ce qui ne l'a pas été : bulletin bilingue arabe, PDF servi par l'application).

- [ ] **Step 7: Commit**

```bash
git add src/SamaEcole.Application/ReportCards src/SamaEcole.Infrastructure/Documents/ReportCardDocument.cs tests/SamaEcole.UnitTests/ReportCards/ExemptSubjectReportCardTests.cs \
  tests/SamaEcole.IntegrationTests/Exemptions/ExemptSubjectReportCardDataTests.cs
git commit -m "feat(dispenses): ligne « Dispensé(e) » sur le bulletin pour une matière dispensée

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 5: API — lire et enregistrer les dispenses d'un élève

**Files:**
- Create: `src/SamaEcole.Application/ClassSubjects/Queries/GetStudentExemptionsQuery.cs`, `Commands/SetStudentExemptionsCommand.cs`
- Modify: `src/SamaEcole.Web/Controllers/ClassSubjectsController.cs`
- Test: `tests/SamaEcole.IntegrationTests/Exemptions/StudentExemptionsApiTests.cs` (handlers), `tests/SamaEcole.FunctionalTests/…/StudentExemptionsEndpointsTests.cs` (autorisation)

**Interfaces:**
- Consumes : `ExemptionQueries`, `ExemptionRules`, `SubjectExemptionInput` (Tâche 2) ; `CoefficientRules.ActiveSchoolYearIdAsync`, `StudentYearClassroom.ResolveAsync` (`main`).
- Produces :
  - `GetStudentExemptionsQuery(Guid StudentId) : IRequest<StudentExemptionsDto>` ; `StudentExemptionsDto(Guid StudentId, Guid SchoolYearId, IReadOnlyList<ExemptibleSubjectDto> Subjects)` ; `ExemptibleSubjectDto(Guid SubjectId, string Name, bool IsExempt, string? Reason, int GradeCount)`
  - `SetStudentExemptionsCommand(Guid StudentId, IReadOnlyList<SubjectExemptionInput> Exemptions) : IRequest<Unit>, IAuditableRequest`
  - routes `GET|PUT /api/v1/class-subjects/students/{studentId}/exemptions` (`Directeur,Secretariat`, module `Pedagogy` hérité du contrôleur)

- [ ] **Step 1: Tests des handlers (intégration)** — jeu : une école (+ une autre), année active, classe « 4ème A » **avec programme** (Maths, EPS actifs sans groupe ; Espagnol avec groupe « LV2 »), un élève inscrit avec des notes d'EPS (2) et de Maths, un élève de l'autre école. Fabrique : `new SetStudentExemptionsCommandHandler(ctx, new StubTenantProvider(Ecole), new TestCurrentUser(Directeur, Role.Directeur))`, `new GetStudentExemptionsQueryHandler(ctx)`. Tests :

```csharp
    [Fact] public async Task Get_Lists_The_Dispensable_Subjects_With_Their_State_And_The_Grades_A_Dispense_Would_Hide() { … }
        // avant : EPS et Maths, IsExempt faux, Reason nul ; EPS.GradeCount == 2 ; l'option Espagnol n'est PAS listée.
    [Fact] public async Task Set_Records_The_Exemption_With_A_Trimmed_Reason() { … }
        // PUT [(Eps, "  Inaptitude médicale  ")] → 1 ligne active, motif « Inaptitude médicale », année active.
    [Fact] public async Task Set_Replaces_The_Whole_List_Idempotently() { … }
        // même PUT deux fois : 1 ligne ; PUT [(Maths, "x")] : EPS retirée (suppression logique), Maths ajoutée.
    [Fact] public async Task Changing_The_Reason_Updates_The_Row_Without_A_Duplicate() { … }
    [Fact] public async Task An_Empty_List_Removes_Every_Exemption() { … }
    [Fact] public async Task A_Missing_Reason_Is_Refused_And_Writes_Nothing() { … }
        // ValidationException, Errors.ContainKey("Exemptions") avec un message contenant « motif » ; l'état existant est inchangé
        // (partir d'une dispense existante, envoyer une liste invalide, vérifier que la dispense est toujours là).
    [Fact] public async Task An_Option_Or_A_Foreign_Subject_Cannot_Be_Exempted() { … }   // Espagnol → 422
    [Fact] public async Task The_Same_Subject_Twice_Is_Refused() { … }
    [Fact] public async Task An_Unknown_Or_Foreign_Student_Is_A_404() { … }              // KeyNotFoundException (école B)
    [Fact] public async Task Without_An_Active_School_Year_The_Request_Is_Refused() { … } // 422 (ActiveSchoolYearIdAsync)
    [Fact] public async Task A_Soft_Deleted_Row_Can_Be_Recreated() { … }
```
Chaque test écrit son corps complet (assertions via `RowsAsync()` = lecture par un contexte applicatif du tenant). Rappels : lecture/mise à jour par `NewOwnerContext()` avec `IgnoreQueryFilters()`.

- [ ] **Step 2: Vérifier l'échec** — `dotnet build tests/SamaEcole.IntegrationTests` : FAIL (types absents).

- [ ] **Step 3: La requête**

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Coefficients;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Exemptions;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.ClassSubjects.Queries;

/// <summary>
/// Matières que l'on peut dispenser à un élève (obligatoires et autonomes dans sa classe), avec l'éventuelle dispense
/// de l'année ACTIVE et son motif, et le nombre de notes de l'année qu'une dispense masquerait. Le motif peut être
/// médical : la route est réservée au Directeur et au Secrétariat, et il n'est renvoyé par AUCUNE autre requête.
/// </summary>
public record GetStudentExemptionsQuery(Guid StudentId) : IRequest<StudentExemptionsDto>;

public record StudentExemptionsDto(Guid StudentId, Guid SchoolYearId, IReadOnlyList<ExemptibleSubjectDto> Subjects);

public record ExemptibleSubjectDto(Guid SubjectId, string Name, bool IsExempt, string? Reason, int GradeCount);

public class GetStudentExemptionsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetStudentExemptionsQuery, StudentExemptionsDto>
{
    public async Task<StudentExemptionsDto> Handle(GetStudentExemptionsQuery request, CancellationToken cancellationToken)
    {
        if (!await dbContext.Students.AnyAsync(s => s.Id == request.StudentId, cancellationToken))
        {
            throw new KeyNotFoundException($"Élève {request.StudentId} introuvable dans votre établissement.");
        }

        var yearId = await CoefficientRules.ActiveSchoolYearIdAsync(dbContext, cancellationToken);
        var classroomId = await StudentYearClassroom.ResolveAsync(dbContext, request.StudentId, yearId, cancellationToken);
        if (classroomId is null)
        {
            return new StudentExemptionsDto(request.StudentId, yearId, []);
        }

        var dispensable = await ExemptionQueries.DispensableAsync(dbContext, classroomId.Value, cancellationToken);

        var reasons = await dbContext.StudentSubjectExemptions.AsNoTracking()
            .Where(x => x.StudentId == request.StudentId && x.SchoolYearId == yearId)
            .Select(x => new { x.SubjectId, x.Reason })
            .ToDictionaryAsync(x => x.SubjectId, x => x.Reason, cancellationToken);

        var subjectIds = dispensable.Select(s => s.SubjectId).Concat(reasons.Keys).Distinct().ToList();
        var gradeCounts = await (
            from g in dbContext.Grades.AsNoTracking()
            join t in dbContext.Terms.AsNoTracking() on g.TermId equals t.Id
            where g.StudentId == request.StudentId && t.SchoolYearId == yearId && subjectIds.Contains(g.SubjectId)
            group g by g.SubjectId into grouped
            select new { SubjectId = grouped.Key, Count = grouped.Count() })
            .ToDictionaryAsync(x => x.SubjectId, x => x.Count, cancellationToken);

        var subjects = dispensable
            .Select(s => reasons.TryGetValue(s.SubjectId, out var reason)
                ? new ExemptibleSubjectDto(s.SubjectId, s.Name, true, reason, gradeCounts.GetValueOrDefault(s.SubjectId))
                : new ExemptibleSubjectDto(s.SubjectId, s.Name, false, null, gradeCounts.GetValueOrDefault(s.SubjectId)))
            .ToList();

        return new StudentExemptionsDto(request.StudentId, yearId, subjects);
    }
}
```
(Le `using ... ValidationException` inutilisé ci-dessus est à retirer si le compilateur le signale.)

- [ ] **Step 4: La commande**

```csharp
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Coefficients;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.Validation;
using SamaEcole.Application.Exemptions;
using SamaEcole.Domain.Entities;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.ClassSubjects.Commands;

/// <summary>
/// PUT /api/v1/class-subjects/students/{studentId}/exemptions — REMPLACE l'ensemble des dispenses de matières
/// obligatoires de l'élève pour l'année ACTIVE (liste vide = plus aucune). Tout est validé AVANT la moindre écriture ;
/// retrait par suppression logique (règle #6) ; idempotent. Le motif est obligatoire, nettoyé, jamais journalisé.
/// </summary>
public record SetStudentExemptionsCommand(Guid StudentId, IReadOnlyList<SubjectExemptionInput> Exemptions)
    : IRequest<Unit>, IAuditableRequest;

public class SetStudentExemptionsCommandValidator : AbstractValidator<SetStudentExemptionsCommand>
{
    public SetStudentExemptionsCommandValidator()
    {
        RuleFor(x => x.StudentId).NotEmpty();
        RuleFor(x => x.Exemptions).NotNull();

        // Le motif est saisi librement puis affiché : pas de HTML. Sa présence et sa longueur sont vérifiées par
        // ExemptionRules (message par matière), pas ici.
        RuleForEach(x => x.Exemptions).ChildRules(exemption => exemption.RuleFor(e => e.Reason).NoHtml());
    }
}

public class SetStudentExemptionsCommandHandler(
    IApplicationDbContext dbContext, ITenantProvider tenantProvider, ICurrentUserService currentUser)
    : IRequestHandler<SetStudentExemptionsCommand, Unit>
{
    public async Task<Unit> Handle(SetStudentExemptionsCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");
        var actor = (currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.")).ToString();

        if (!await dbContext.Students.AnyAsync(s => s.Id == request.StudentId, cancellationToken))
        {
            throw new KeyNotFoundException($"Élève {request.StudentId} introuvable dans votre établissement.");
        }

        var yearId = await CoefficientRules.ActiveSchoolYearIdAsync(dbContext, cancellationToken);
        var classroomId = await StudentYearClassroom.ResolveAsync(dbContext, request.StudentId, yearId, cancellationToken)
            ?? throw new ValidationException([
                new ValidationFailure(nameof(request.StudentId), "Cet élève n'est rattaché à aucune classe.")
            ]);

        var dispensable = await ExemptionQueries.DispensableAsync(dbContext, classroomId, cancellationToken);
        if (ExemptionRules.Validate(dispensable, request.Exemptions) is { } message)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.Exemptions), message)]);
        }

        var wanted = request.Exemptions.ToDictionary(e => e.SubjectId, e => e.Reason!.Trim());

        var existing = await dbContext.StudentSubjectExemptions
            .Where(x => x.StudentId == request.StudentId && x.SchoolYearId == yearId)
            .ToListAsync(cancellationToken);

        foreach (var stale in existing.Where(x => !wanted.ContainsKey(x.SubjectId)))
        {
            stale.SoftDelete(actor);
        }

        foreach (var row in existing.Where(x => wanted.ContainsKey(x.SubjectId) && x.Reason != wanted[x.SubjectId]))
        {
            row.Reason = wanted[row.SubjectId];
        }

        var present = existing.Select(x => x.SubjectId).ToHashSet();
        foreach (var (subjectId, reason) in wanted.Where(w => !present.Contains(w.Key)))
        {
            dbContext.StudentSubjectExemptions.Add(new StudentSubjectExemption
            {
                SchoolId = schoolId, StudentId = request.StudentId, SubjectId = subjectId,
                SchoolYearId = yearId, Reason = reason
            });
        }

        // Une violation de l'index unique (deux secrétaires en même temps) est traduite par SaveChangesAsync en
        // ConcurrencyConflictException → 409, jamais un doublon silencieux.
        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
```

- [ ] **Step 5: Les routes** — dans `ClassSubjectsController.cs` (`StaffRoles` existe déjà), après `SetStudentOptions` :

```csharp
    /// <summary>
    /// Matières dispensables d'un élève et ses dispenses de l'année active, motif compris. Réservé au Directeur et au
    /// Secrétariat : le motif peut être médical.
    /// </summary>
    [HttpGet("students/{studentId:guid}/exemptions")]
    [Authorize(Roles = StaffRoles)]
    [ProducesResponseType<StudentExemptionsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> GetStudentExemptions(Guid studentId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetStudentExemptionsQuery(studentId), cancellationToken));

    public record SetStudentExemptionsRequest(IReadOnlyList<SubjectExemptionInput>? Exemptions);

    /// <summary>Remplace les dispenses de l'élève pour l'année active (liste vide = aucune). Motif obligatoire.</summary>
    [HttpPut("students/{studentId:guid}/exemptions")]
    [Authorize(Roles = StaffRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetStudentExemptions(
        Guid studentId, [FromBody] SetStudentExemptionsRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new SetStudentExemptionsCommand(studentId, request.Exemptions ?? []), cancellationToken);
        return NoContent();
    }
```
(usings : `SamaEcole.Application.Exemptions`.)

- [ ] **Step 6: Tests d'autorisation (fonctionnels)** — dans `tests/SamaEcole.FunctionalTests`, sur le modèle des tests d'autorisation existants d'`EnrollmentsController` / `CoefficientsEndpointsTests` (lire leur fabrique d'utilisateurs par rôle) : `GET` et `PUT …/exemptions` répondent **403** pour Finance et Enseignant, **200/204** pour Directeur et Secrétariat, **401** sans jeton. Si l'infrastructure fonctionnelle ne permet pas de fabriquer ces rôles dans cet environnement, le dire dans le rapport et ajouter à la place un test unitaire par réflexion sur `ClassSubjectsController.GetStudentExemptions`/`SetStudentExemptions` vérifiant `[Authorize(Roles = "Directeur,Secretariat")]`.

- [ ] **Step 7: Tests ciblés** — `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~StudentExemptionsApiTests|FullyQualifiedName~ClassSubjects"` et `dotnet test tests/SamaEcole.FunctionalTests --filter "FullyQualifiedName~StudentExemptionsEndpointsTests"` → PASS.

- [ ] **Step 8: Commit**

```bash
git add src/SamaEcole.Application/ClassSubjects src/SamaEcole.Web/Controllers/ClassSubjectsController.cs tests/SamaEcole.IntegrationTests/Exemptions tests/SamaEcole.FunctionalTests
git commit -m "feat(dispenses): API de lecture et d'écriture des dispenses (Directeur, Secrétariat)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 6: Section « Dispenses » de la fiche élève

**Files:**
- Create: `src/SamaEcole.Web/wwwroot/js/subject-exemptions.js`
- Modify: `src/SamaEcole.Web/wwwroot/js/students.js` (à côté de `loadStudentOptions`), `src/SamaEcole.Web/Views/Students/Index.cshtml` (sous le bloc « Matières optionnelles » de `main`, script avant `students.js`)
- Test: `src/SamaEcole.Web/tests/js/subject-exemptions.test.mjs`, `students-exemptions.test.mjs`

**Interfaces:**
- Produces (`window.subjectExemptions`) : `stateFrom(subjects) → { [subjectId]: { checked, reason } }` ; `payload(state) → [{ subjectId, reason }]` (matières cochées, motif nettoyé, liste vide si aucune) ; `missingReasons(subjects, state) → string[]` (noms des matières cochées sans motif) ; `hiddenGrades(subjects, state) → number`. (Ce sont les quatre fonctions `exemption*` de `subject-options.js` de la v1, renommées.)
- Produces (composant `studentsView`) : `studentExemptions`, `exemptionState`, `exemptionsDirty`, `isSavingExemptions`, `exemptionsError`, `exemptionsNotice`, `missingExemptionReasons`, `hiddenExemptionGrades`, `canSaveExemptions` (getters), `loadStudentExemptions(studentId)`, `toggleExemption(subjectId)`, `setExemptionReason(subjectId, reason)`, `saveStudentExemptions()`.

- [ ] **Step 1: Tests de la logique pure** — `subject-exemptions.test.mjs`, repris de `backup/optional-subjects-v1:src/SamaEcole.Web/tests/js/subject-options.test.mjs` (les quatre derniers tests, sur `MANDATORY`), en chargeant `subject-exemptions.js` et en renommant `exemptionStateFrom → stateFrom`, `exemptionsPayload → payload`, `missingReasons → missingReasons`, `hiddenExemptionGrades → hiddenGrades`. Run : `node --test src/SamaEcole.Web/tests/js/subject-exemptions.test.mjs` → FAIL (fichier absent).

- [ ] **Step 2: `subject-exemptions.js`**

```js
/**
 * Dispenses d'une matière obligatoire (motif obligatoire) — logique PURE, sans DOM ni réseau, testable sous
 * `node --test`. Aucune règle métier n'est décidée ici : le serveur (ExemptionRules) reste seul juge ; ce module ne
 * sert qu'à ne pas laisser l'utilisateur composer une demande qu'il refuserait. Le motif peut être médical : il ne
 * quitte jamais l'état du composant (aucun log, aucun stockage).
 */
(function () {
    'use strict';

    /** État initial du formulaire à partir des matières renvoyées par le serveur : case et motif repris. */
    function stateFrom(subjects) {
        return Object.fromEntries((subjects || []).map((s) => [
            s.subjectId,
            { checked: !!s.isExempt, reason: s.reason ?? '' }
        ]));
    }

    /** Les dispenses à envoyer : uniquement les matières cochées, motif nettoyé. Liste vide si aucune case n'est cochée. */
    function payload(state) {
        return Object.entries(state || {})
            .filter(([, entry]) => entry && entry.checked)
            .map(([subjectId, entry]) => ({ subjectId, reason: (entry.reason ?? '').trim() }));
    }

    /** Noms des matières cochées dont le motif est vide : le serveur refuserait l'enregistrement (422). */
    function missingReasons(subjects, state) {
        return (subjects || [])
            .filter((s) => state && state[s.subjectId] && state[s.subjectId].checked
                && !(state[s.subjectId].reason ?? '').trim())
            .map((s) => s.name);
    }

    /** Notes de l'année que ces dispenses masqueraient : somme de `gradeCount` des matières cochées. */
    function hiddenGrades(subjects, state) {
        return (subjects || [])
            .filter((s) => state && state[s.subjectId] && state[s.subjectId].checked)
            .reduce((sum, s) => sum + (s.gradeCount || 0), 0);
    }

    window.subjectExemptions = { stateFrom, payload, missingReasons, hiddenGrades };
})();
```
Run : `node --test src/SamaEcole.Web/tests/js/subject-exemptions.test.mjs` → PASS.

- [ ] **Step 3: Tests de la fiche** — `students-exemptions.test.mjs`, sur le modèle de `students-options.test.mjs` de la v1 (fabrique `ctx.initAlpine().get('studentsView')()`, **sans** `init()`, `preload` avec `auth: { role, canView: () => true }`, `pdfPreview: { state: () => ({}) }` et une doublure `api.get/put`). Données : `{ studentId: 's1', schoolYearId: 'y1', subjects: [{ subjectId: 'eps', name: 'EPS', isExempt: false, reason: null, gradeCount: 2 }, { subjectId: 'maths', name: 'Mathématiques', isExempt: true, reason: 'Inaptitude médicale', gradeCount: 5 }] }`. Tests :
1. `le chargement appelle la route des dispenses de l'élève et pré-remplit case et motif` — `api.get` reçoit `/class-subjects/students/s1/exemptions` ; `exemptionState` = `{ eps: { checked: false, reason: '' }, maths: { checked: true, reason: 'Inaptitude médicale' } }` ; `exemptionsDirty === false`.
2. `cocher une dispense marque la section modifiée et compte les notes masquées` — `toggleExemption('eps')` : `exemptionsDirty`, `hiddenExemptionGrades === 7` (EPS 2 + Maths 5, les deux cochées).
3. `une dispense sans motif bloque l'enregistrement et nomme la matière` — `missingExemptionReasons` = `['EPS']`, `canSaveExemptions === false`, `saveStudentExemptions()` n'appelle pas `put`, `exemptionsError` cite « EPS ».
4. `l'enregistrement envoie exactement les matières cochées, motif nettoyé, puis recharge` — `setExemptionReason('eps', '  Certificat  ')` : `put('/class-subjects/students/s1/exemptions', { exemptions: [{ subjectId: 'eps', reason: 'Certificat' }, { subjectId: 'maths', reason: 'Inaptitude médicale' }] })` ; `get` rappelé ; `exemptionsDirty === false` ; `exemptionsNotice` renseigné.
5. `décocher toutes les dispenses envoie une liste vide, pas « rien »` — `{ exemptions: [] }`.
6. `sans le module Pédagogie (403) ou une classe sans matière dispensable, la section reste masquée` — `get` rejette : `studentExemptions` vide, aucune erreur affichée ; `get` renvoie `subjects: []` : idem.
7. `un rôle sans droit de gestion ne charge rien` — `canManageStudent` faux : aucun appel `get`.
8. `une fiche changée entre-temps n'écrase pas l'état de l'élève suivant` — la réponse arrive après un changement de `detailStudent` : ignorée.
Run : `node --test src/SamaEcole.Web/tests/js/students-exemptions.test.mjs` → FAIL (`loadStudentExemptions` absent).

- [ ] **Step 4: `students.js`** — dans l'état, à côté de `studentOptionGroups` :

```js
        // Dispenses d'une matière obligatoire (motif obligatoire) de l'année active. Le motif peut être médical : il
        // reste dans cet état, jamais loggé ni stocké ailleurs.
        studentExemptions: [],
        exemptionState: {},
        exemptionsDirty: false,
        isSavingExemptions: false,
        exemptionsError: null,
        exemptionsNotice: null,
```
Dans `openDetail`, à côté de `this.loadStudentOptions(student.id);` : remise à zéro (`this.studentExemptions = []; this.exemptionState = {}; this.exemptionsDirty = false; this.exemptionsError = null; this.exemptionsNotice = null;`) puis `this.loadStudentExemptions(student.id);`. Méthodes (à côté de `saveStudentOptions`) :

```js
        /**
         * Section « Dispenses » de la fiche : matières obligatoires de la classe de l'élève que l'on peut dispenser,
         * avec l'état de l'année active. Réservée à ceux qui gèrent l'élève (Directeur, Secrétariat).
         */
        async loadStudentExemptions(studentId) {
            if (!this.canManageStudent) return;
            try {
                const result = await window.api.get(`/class-subjects/students/${encodeURIComponent(studentId)}/exemptions`);
                if (!this.detailStudent || this.detailStudent.id !== studentId) return; // fiche changée entre-temps
                this.studentExemptions = (result && result.subjects) || [];
                this.exemptionState = window.subjectExemptions.stateFrom(this.studentExemptions);
                this.exemptionsDirty = false;
            } catch {
                // silence-volontaire : sans module Pédagogie (403) ou sans année active, la section reste masquée.
                this.studentExemptions = [];
            }
        },

        toggleExemption(subjectId) {
            const current = this.exemptionState[subjectId] || { checked: false, reason: '' };
            this.exemptionState = { ...this.exemptionState, [subjectId]: { ...current, checked: !current.checked } };
            this.exemptionsDirty = true;
            this.exemptionsNotice = null;
        },

        setExemptionReason(subjectId, reason) {
            const current = this.exemptionState[subjectId] || { checked: false, reason: '' };
            this.exemptionState = { ...this.exemptionState, [subjectId]: { ...current, reason } };
            this.exemptionsDirty = true;
            this.exemptionsNotice = null;
        },

        get missingExemptionReasons() {
            return window.subjectExemptions.missingReasons(this.studentExemptions, this.exemptionState);
        },

        get hiddenExemptionGrades() {
            return window.subjectExemptions.hiddenGrades(this.studentExemptions, this.exemptionState);
        },

        get canSaveExemptions() {
            return this.exemptionsDirty && this.missingExemptionReasons.length === 0 && !this.isSavingExemptions;
        },

        async saveStudentExemptions() {
            if (!this.detailStudent || this.isSavingExemptions) return;

            const missing = this.missingExemptionReasons;
            if (missing.length > 0) {
                this.exemptionsError = `Renseignez le motif de la dispense : ${missing.join(', ')}.`;
                return;
            }

            this.isSavingExemptions = true;
            this.exemptionsError = null;
            this.exemptionsNotice = null;
            try {
                await window.api.put(`/class-subjects/students/${this.detailStudent.id}/exemptions`,
                    { exemptions: window.subjectExemptions.payload(this.exemptionState) });
                await this.loadStudentExemptions(this.detailStudent.id);
                this.exemptionsNotice = 'Dispenses enregistrées.';
            } catch (err) {
                this.exemptionsError = window.api.toMessage(err, "Erreur lors de l'enregistrement des dispenses.");
            } finally {
                this.isSavingExemptions = false;
            }
        },
```

- [ ] **Step 5: La vue** — `Views/Students/Index.cshtml` : `<script src="~/js/subject-exemptions.js" asp-append-version="true"></script>` avant `students.js`, puis, sous le bloc « Matières optionnelles » de `main`, dans le même style (mêmes classes, aucune nouvelle classe Tailwind) :

```html
    @* Dispenses (matières obligatoires) : l'élève ne suit pas la matière cette année-là — elle sort de ses moyennes et de
       sa saisie, et le bulletin la marque « Dispensé(e) ». Un motif est obligatoire ; il ne figure sur aucun document.
       Masquée si la classe n'a aucune matière dispensable ; modifiable par ceux qui gèrent l'élève. *@
    <div x-show="studentExemptions.length > 0" x-cloak class="bg-slate-50/80 border border-slate-200/60 rounded-xl p-4 mx-6 mb-6 shadow-sm space-y-3">
        <h3 class="text-xs font-bold text-slate-400 uppercase tracking-wider">Dispenses</h3>
        <p class="text-xs text-slate-400">
            Une matière dispensée n'entre plus dans les moyennes et n'est plus saisie ; sur le bulletin elle reste, marquée « Dispensé(e) ».
        </p>
        <template x-for="subject in studentExemptions" :key="subject.subjectId">
            <div class="space-y-2">
                <label class="flex items-center gap-2 cursor-pointer select-none text-sm text-slate-700">
                    <input type="checkbox" class="checkbox-field" :disabled="!canManageStudent"
                           :checked="exemptionState[subject.subjectId] && exemptionState[subject.subjectId].checked"
                           x-on:change="toggleExemption(subject.subjectId)">
                    <span class="font-medium" x-text="subject.name"></span>
                    <span x-show="subject.gradeCount > 0" x-cloak class="text-xs text-slate-400" x-text="'(' + subject.gradeCount + ' note(s))'"></span>
                </label>
                <div x-show="exemptionState[subject.subjectId] && exemptionState[subject.subjectId].checked" x-cloak>
                    <label :for="'exempt-reason-' + subject.subjectId" class="form-label form-label-required">Motif</label>
                    <input :id="'exempt-reason-' + subject.subjectId" type="text" maxlength="200" class="input-field"
                           placeholder="ex. Inaptitude médicale, certificat du 12/09/2026" :disabled="!canManageStudent"
                           :value="exemptionState[subject.subjectId] ? exemptionState[subject.subjectId].reason : ''"
                           x-on:input="setExemptionReason(subject.subjectId, $event.target.value)">
                </div>
            </div>
        </template>
        <p x-show="missingExemptionReasons.length > 0" x-cloak class="text-xs text-amber-700">
            Motif manquant : <span x-text="missingExemptionReasons.join(', ')"></span>.
        </p>
        <p x-show="hiddenExemptionGrades > 0" x-cloak class="text-xs text-amber-700">
            <span x-text="hiddenExemptionGrades"></span> note(s) déjà saisie(s) seront masquées des moyennes (elles restent conservées).
        </p>
        <p x-show="exemptionsError" x-cloak x-text="exemptionsError" class="field-error"></p>
        <p x-show="exemptionsNotice" x-cloak x-text="exemptionsNotice" class="text-xs text-emerald-700"></p>
        <button type="button" x-show="canManageStudent" x-on:click="saveStudentExemptions()" :disabled="!canSaveExemptions"
                class="btn-modal-secondary !h-9 !px-4 text-sm disabled:opacity-50">
            <span x-text="isSavingExemptions ? 'Enregistrement…' : 'Enregistrer les dispenses'"></span>
        </button>
    </div>
```
Attention Razor : dans ce fichier, `@click` s'écrit `@@click` ; on utilise ici `x-on:` (pas d'échappement).

- [ ] **Step 6: Tests et compilation** — `node --test src/SamaEcole.Web/tests/js/subject-exemptions.test.mjs src/SamaEcole.Web/tests/js/students-exemptions.test.mjs`, puis `npm test --prefix src/SamaEcole.Web` (toute la suite JS), `npm run build:css --prefix src/SamaEcole.Web` (`site.css` est git-ignoré) et `dotnet build` (les vues Razor compilent avec le projet web). Expected : tout vert, 0 erreur. **Le parcours navigateur n'est pas faisable par un sous-agent** : le dire dans le rapport (suivi propriétaire).

- [ ] **Step 7: Commit**

```bash
git add src/SamaEcole.Web/wwwroot/js/subject-exemptions.js src/SamaEcole.Web/wwwroot/js/students.js src/SamaEcole.Web/Views/Students/Index.cshtml \
  src/SamaEcole.Web/tests/js/subject-exemptions.test.mjs src/SamaEcole.Web/tests/js/students-exemptions.test.mjs
git commit -m "feat(dispenses): section « Dispenses » de la fiche élève

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 7: Documentation, aide et contexte actif

**Files:**
- Modify: `docs/Volume_1_Cahier_des_Charges.md` (nouvelle sous-section **à la suite du §8.8 de `main`** — le §8.8 est déjà « matières par classe et options » ; numéroter §8.9), `docs/Volume_3_DDS.md`, `docs/Volume_4_API_Design.md`, `openapi.yaml`, `docs/Volume_7_Security.md`, `docs/design-references/README.md`, `ACTIVE_CONTEXT.md`, `src/SamaEcole.Web/wwwroot/js/help.js`
- Test: `src/SamaEcole.Web/tests/js/help.test.mjs`, `help-render.test.mjs` (existants)

- [ ] **Step 1: Cahier des charges §8.9 « Dispense d'une matière obligatoire »** : ce qu'est une dispense (matière obligatoire, motif obligatoire, jamais imprimé), qui la saisit (Directeur, Secrétariat, fiche élève), l'année active, l'effet sur les moyennes (total des coefficients adapté), la saisie (élève absent des grilles et fiches, note refusée, import refusé), le bulletin (« Dispensé(e) », coefficient barré, hors totaux), les notes conservées, le rang. Renvoyer au §8.8 pour les options.
- [ ] **Step 2: DDS, API, OpenAPI, sécurité, design** — **Volume 3** : section `student_subject_exemptions` (colonnes dont `Reason` non nul varchar(200), index partiel `UX_student_subject_exemptions_key`, FK composites RESTRICT, RLS, `GRANT SELECT, INSERT, UPDATE`, purges, pas de `xmin`). **Volume 4** : les deux routes (rôles `Directeur,Secretariat`, 200/204/403/404/409/422, corps et DTO exacts du code) et un paragraphe « Effets sur les autres routes » : `POST /grades` et l'import refusent une matière dispensée ; **`PUT`/`DELETE` d'une note existante ne sont pas bloqués** (invisible dans les moyennes). **openapi.yaml** : les deux routes et leurs schémas — **citer toute description contenant une virgule, un deux-points ou un dièse** (des descriptions non citées dans un mapping de flux avaient été scindées en clés parasites en v1) et **valider le fichier avec un vrai parseur YAML** (PyYAML si disponible) avant le commit. **Volume 7** : ligne de matrice « Dispenses de matières » (Directeur, Secrétariat) et un paragraphe : le motif est une donnée sensible, jamais dans un document, un DTO de bulletin, un journal ni une erreur ; lu par une seule route, réservée aux deux rôles. **design-references/README.md** : « **Écart validé à la référence (25/09/2026) : la mention « Dispensé(e) »** » (secondaire : coefficient barré ; primaire ; APC), « aucun autre élément du bulletin ne change (règle #12) ».
- [ ] **Step 3: Fiche d'aide** — dans `help.js`, une entrée `dispenses-matieres` (mêmes champs que `matieres-par-classe-options` de `main` : `id`, `title`, `location`, `href`, `roles`, `definition`, `objectif`, `probleme`, `procedure`, `impacts`, `recommandations`), rôles Directeur et Secrétariat : définition, procédure (fiche élève › « Dispenses », cocher, saisir le motif, enregistrer, vérifier la saisie et le bulletin), impacts (moyennes, saisie, bulletin « Dispensé(e) », notes conservées, année active), recommandations (motif sobre, il n'est jamais imprimé ; retirer la dispense pour saisir une note).
- [ ] **Step 4: `ACTIVE_CONTEXT.md`** — sous-section « Dispense d'une matière obligatoire (25/09/2026) — livré » après celle de l'Évolution N°6 : branche, spec v2 et plan v2, données, intégration dans `SubjectFollowScope`, API, écran, bulletin, l'**invariant** (sans dispense, comme `main`), l'écart validé à la règle #12, et les points de vigilance de la spec §10 (dont : deux sources d'exclusion dans `SubjectFollowScope`, dispense inerte après un changement de programme ou de classe, livret de compétences, motif sensible).
- [ ] **Step 5: Tests** — `npm test --prefix src/SamaEcole.Web` → PASS (`help.test.mjs` détecte un identifiant dupliqué).
- [ ] **Step 6: Commit**

```bash
git add docs/Volume_1_Cahier_des_Charges.md docs/Volume_3_DDS.md docs/Volume_4_API_Design.md docs/Volume_7_Security.md docs/design-references/README.md openapi.yaml ACTIVE_CONTEXT.md \
  src/SamaEcole.Web/wwwroot/js/help.js
git commit -m "docs(dispenses): cahier des charges §8.9, API, DDS, aide et contexte actif

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Vérification finale (avant de proposer la fusion)

- [ ] `dotnet build` sans erreur ni avertissement nouveau ; `npm test --prefix src/SamaEcole.Web` vert.
- [ ] Tests ciblés verts : `--filter "FullyQualifiedName~Exemption|FullyQualifiedName~ClassSubjects|FullyQualifiedName~ExemptSubjectReportCard"` et les suites voisines citées à chaque tâche.
- [ ] `git diff origin/main --stat` : uniquement des fichiers de la dispense ; **aucun changement d'espaces** dans `ReportCardDocument.cs` (`git diff origin/main -w --stat` et `git diff origin/main --stat` doivent donner le même nombre de lignes pour ce fichier).
- [ ] **À lancer par le propriétaire** (consigne du 17/09/2026) : `dotnet test` complet dont `--filter Category=MultiTenant`, et les `FunctionalTests` (nettoyage `AuthApiFactory`).
- [ ] **À faire par le propriétaire** : parcours navigateur (fiche élève › Dispenses ; grille et fiche PDF de saisie sans l'élève dispensé ; bulletin d'un élève dispensé dans chacun des trois cycles) et PDF servi par l'application.
- [ ] Revue finale de la branche par un relecteur, avec la même grille que la v1 : confidentialité du motif, isolation tenant, invariant « sans dispense = `main` », lecteurs oubliés, cohérence des documents.

## Self-review (spec ↔ plan)

| Spécification v2 | Tâche |
|---|---|
| §2.1 options = modèle de `main` | Tâche 0 (repartir de `main`) ; rien à implémenter |
| §2.2 table, motif obligatoire ≤ 200, élève + année ; §3 | 1 |
| §2.3 matières dispensables ; §4.1 | 2 (`DispensableAsync`) |
| §2.4, §4.2 exclusion via `SubjectFollowScope` (4 membres) | 3 |
| §2.5 notes conservées ; §4.3 invariant | 3 (tests 1 et 4) |
| §2.6, §4.4 bulletin, écart règle #12 | 4 (rendu), 7 (README) |
| §2.7, §2.9 confidentialité, rôles, module ; §5 API | 5 (routes réservées, test 403) |
| §2.8 année active | 5 |
| §6 écran | 6 |
| §7 tests | chaque tâche |
| §8 documentation | 7 |
| Correctifs demandés (confidentialité 403, nettoyage des espaces, garde d'édition de `subjects.js`) | 5 (403) ; 4 (aucun changement d'espaces, vérifié au contrôle final) ; la garde de `subjects.js` disparaît avec la case « optionnelle » de la v1 (`subjects.js` n'est plus touché) |
| Migration EF propre sur la base de `main` | 1 |

Cohérence des types : `ExemptSubject`, `DispensableSubject`, `SubjectExemptionInput`, `ExemptionRules`, `ExemptionQueries` (Tâche 2) sont consommés à l'identique par `SubjectFollowScope`/résumé (Tâche 3) et par l'API (Tâche 5) ; `ExemptSubjectDto`, `GradeSummaryDto.ExemptSubjects`, `EvaluationLineDto.IsExempt` (Tâche 3) par `ReportCardDto`/`ReportCardDocument` (Tâche 4) ; les DTO `StudentExemptionsDto`/`ExemptibleSubjectDto` (Tâche 5) par `subject-exemptions.js` et `students.js` (Tâche 6).
