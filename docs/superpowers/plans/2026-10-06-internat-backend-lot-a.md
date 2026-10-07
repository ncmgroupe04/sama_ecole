# Internat — Lot A (schéma, reprise des données, purges) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Poser le schéma `Dormitory → DormitoryRoom → Bed → BoardingEnrollment → BoardingLeave / BoardingAttendance` (entités, configuration EF, migration avec RLS), reprendre les données Internat existantes, et patcher les fonctions de purge.

**Architecture:** Deux migrations additives. `AddBoardingDormitoryModel` crée les six tables, leurs index/contraintes/policies RLS et reprend les données de `Enrollment.RoomId/BoardingStatus`. `AddBoardingToPurges` patche `reset_school_data` et `delete_school_year`. Les anciennes colonnes `enrollments.BoardingStatus/RoomId` ne sont **pas** touchées (suppression = lot F). Aucun handler, endpoint ni PDF dans ce lot.

**Tech Stack:** .NET 10, EF Core + Npgsql, PostgreSQL 16 (RLS), xUnit + FluentAssertions + Testcontainers.

**Spec:** `docs/superpowers/specs/2026-10-06-internat-backend-and-profile-isolation-design.md` (PR #60, branche `docs/internat-backend-and-profile-isolation-design`). Lire §2.2, §3, §4.1, §3.7 avant de commencer.

## Global Constraints

- Toute table tenant : `SchoolId`, Global Query Filter (automatique via `ITenantEntity`) **et** policy RLS `{table}_tenant_isolation` ajoutée à la main dans la migration (AGENTS.md règle #2).
- `GRANT SELECT, INSERT, UPDATE` seulement au rôle `sama_ecole_app` — jamais `DELETE` (règle #6).
- Verrou optimiste : colonne système `xmin` (`builder.Property<uint>("xmin").IsRowVersion()`) sur les six tables.
- FK `OnDelete(Restrict)`, composites `(SchoolId, X)` vers la clé alternative `(SchoolId, Id)` du parent.
- Enums persistés en string (`HasConversion<string>()`).
- Index uniques d'identité **partiels** (`WHERE NOT "IsDeleted"`).
- Ne jamais modifier une migration déjà appliquée ; les nouvelles migrations sont additives et `Down()` exact.
- Pas d'écriture sur `enrollments.BoardingStatus` / `RoomId` dans ce lot (lecture seule pour la reprise).
- Prérequis d'exécution des tests : **Docker Desktop démarré** (Testcontainers `postgres:16-alpine`).
- Un commit par tâche, message en français, terminé par la ligne `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.

## Correction de la spec §4.1 (point 4)

La spec dit « chaque `Enrollment` pensionnaire non annulé → un `BoardingEnrollment` actif ». Appliqué à toutes les années, des inscriptions de **années passées** occuperaient des lits. Règle retenue ici : année **active** → séjour actif ; autre année → séjour **clos** (`IsActive = false`, `EndDate` = fin de l'année, `BedId` nul). La spec est corrigée dans la PR #60.

## Carte des fichiers

| Fichier | Rôle |
|---|---|
| `src/SamaEcole.Domain/Enums/BoardingEnums.cs` (créer) | `DormitoryGender`, `BoardingRegime`, `BedStatus`, `BoardingLeaveReason` |
| `src/SamaEcole.Domain/Entities/Dormitory.cs`, `DormitoryRoom.cs`, `Bed.cs`, `BoardingEnrollment.cs`, `AllowedExitPerson.cs`, `BoardingLeave.cs`, `BoardingAttendance.cs` (créer) | Entités |
| `src/SamaEcole.Application/Common/Interfaces/IApplicationDbContext.cs`, `src/SamaEcole.Persistence/ApplicationDbContext.cs` (modifier) | 6 `DbSet` |
| `src/SamaEcole.Persistence/Configurations/{Dormitory,DormitoryRoom,Bed,BoardingEnrollment,BoardingLeave,BoardingAttendance}Configuration.cs` (créer) | Mapping, index, CHECK, FK |
| `src/SamaEcole.Persistence/Migrations/*_AddBoardingDormitoryModel.cs` (générer puis éditer) | Tables, RLS, grants, reprise |
| `src/SamaEcole.Persistence/Migrations/*_AddBoardingToPurges.cs` (créer à partir de la génération) | Patch des deux fonctions de purge |
| `tests/SamaEcole.IntegrationTests/Boarding/BoardingSchemaIsolationTests.cs` (créer) | RLS, contraintes, droits |
| `tests/SamaEcole.IntegrationTests/Boarding/BoardingBackfillMigrationTests.cs` (créer) | Reprise aller-retour |
| `tests/SamaEcole.IntegrationTests/Boarding/BoardingPurgeTests.cs` (créer) | Purges avec pensionnaires |

---

### Task 1 : Enums, entités, DbSets

**Files:**
- Create: `src/SamaEcole.Domain/Enums/BoardingEnums.cs`, les 7 fichiers d'entités ci-dessus
- Modify: `src/SamaEcole.Application/Common/Interfaces/IApplicationDbContext.cs`, `src/SamaEcole.Persistence/ApplicationDbContext.cs`

**Interfaces — Produces:** `Dormitory`, `DormitoryRoom`, `Bed`, `BoardingEnrollment`, `AllowedExitPerson`, `BoardingLeave`, `BoardingAttendance` ; `DbSet` : `Dormitories`, `DormitoryRooms`, `Beds`, `BoardingEnrollments`, `BoardingLeaves`, `BoardingAttendances`.

- [ ] **Step 1 : enums**

```csharp
// src/SamaEcole.Domain/Enums/BoardingEnums.cs
namespace SamaEcole.Domain.Enums;

/// <summary>Genre d'un pavillon. <c>Mixte</c> n'existe que pour la reprise de données (spec Q2) : l'API le refuse.</summary>
public enum DormitoryGender { Garcons, Filles, Mixte }

/// <summary>Régime d'un séjour. Un externe n'a simplement pas de séjour actif.</summary>
public enum BoardingRegime { Interne, DemiPensionnaire }

/// <summary>
/// Statut d'un lit. Seuls <c>Available</c> et <c>Maintenance</c> sont STOCKÉS (CHECK en base) ;
/// <c>Occupied</c> est projeté à la lecture depuis le séjour actif (spec N2).
/// </summary>
public enum BedStatus { Available, Occupied, Maintenance }

public enum BoardingLeaveReason { Weekend, Sante, Famille, Autre }
```

- [ ] **Step 2 : entités** (une classe par fichier, `namespace SamaEcole.Domain.Entities;`, `using SamaEcole.Domain.Common; using SamaEcole.Domain.Enums;`)

```csharp
public class Dormitory : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }
    public required string Name { get; set; }
    public DormitoryGender Gender { get; set; }
    public string? SupervisorName { get; set; }
    public string? SupervisorPhone { get; set; }
    /// <summary>Liaison facultative à un compte Surveillant (spec Q7).</summary>
    public Guid? SupervisorUserId { get; set; }
    public string? Notes { get; set; }
}

public class DormitoryRoom : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }
    public Guid DormitoryId { get; set; }
    public required string Name { get; set; }
}

public class Bed : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }
    public Guid DormitoryRoomId { get; set; }
    public int BedNumber { get; set; }
    /// <summary>Jamais <see cref="BedStatus.Occupied"/> en base (CHECK).</summary>
    public BedStatus Status { get; set; } = BedStatus.Available;
}

public class AllowedExitPerson
{
    public string Name { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
}

/// <summary>Séjour d'un élève à l'internat pour UNE inscription (spec §3.4). MedicalNotes = donnée de santé.</summary>
public class BoardingEnrollment : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }
    public Guid StudentId { get; set; }
    public Guid EnrollmentId { get; set; }
    public BoardingRegime Regime { get; set; }
    public Guid? BedId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public string? MedicalNotes { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public List<AllowedExitPerson> AllowedExitPersons { get; set; } = [];
}

public class BoardingLeave : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }
    public Guid BoardingEnrollmentId { get; set; }
    public DateOnly LeaveDate { get; set; }
    public DateOnly ExpectedReturnDate { get; set; }
    public DateOnly? ActualReturnDate { get; set; }
    public BoardingLeaveReason Reason { get; set; }
    public string? ReasonDetail { get; set; }
    public required string AccompaniedBy { get; set; }
    /// <summary>Figé à la déclaration (spec Q5) : l'accompagnateur n'était pas dans la liste des personnes habilitées.</summary>
    public bool IsCompanionUnlisted { get; set; }
}

public class BoardingAttendance : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }
    public Guid BoardingEnrollmentId { get; set; }
    public DateOnly Date { get; set; }
    public bool IsPresent { get; set; }
    public string? Note { get; set; }
}
```

- [ ] **Step 3 : DbSets.** Dans `IApplicationDbContext` ajouter `DbSet<Dormitory> Dormitories { get; }`, `DbSet<DormitoryRoom> DormitoryRooms { get; }`, `DbSet<Bed> Beds { get; }`, `DbSet<BoardingEnrollment> BoardingEnrollments { get; }`, `DbSet<BoardingLeave> BoardingLeaves { get; }`, `DbSet<BoardingAttendance> BoardingAttendances { get; }` ; dans `ApplicationDbContext` les six `public DbSet<X> Name => Set<X>();` correspondants (même style que `StudentSubjectExemptions`).

- [ ] **Step 4 : compiler.** Run: `dotnet build SamaEcole.sln` — Expected: build OK (les entités sans configuration sont acceptées par EF jusqu'à la tâche 2 ; **ne pas** générer de migration maintenant).

- [ ] **Step 5 : commit** `feat(internat): entités Dormitory/Room/Bed/BoardingEnrollment/Leave/Attendance`.

---

### Task 2 : Configurations EF Core

**Files:** Create les six `*Configuration.cs` dans `src/SamaEcole.Persistence/Configurations/`.

**Interfaces — Consumes:** entités de la tâche 1. **Produces:** noms d'index/contraintes utilisés par les tests de la tâche 3 : `UX_dormitories_name`, `UX_dormitory_rooms_name`, `UX_beds_number`, `UX_boarding_enrollments_active_enrollment`, `UX_boarding_enrollments_active_bed`, `UX_boarding_leaves_open`, `UX_boarding_attendances_night`, `CK_beds_stored_status`, `CK_boarding_enrollments_*`, `CK_boarding_leaves_*`.

- [ ] **Step 1 : `DormitoryConfiguration`**

```csharp
public class DormitoryConfiguration : IEntityTypeConfiguration<Dormitory>
{
    public void Configure(EntityTypeBuilder<Dormitory> builder)
    {
        builder.ToTable("dormitories");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.SchoolId).IsRequired();
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(d => d.Name).IsRequired().HasMaxLength(100);
        builder.Property(d => d.Gender).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(d => d.SupervisorName).HasMaxLength(150);
        builder.Property(d => d.SupervisorPhone).HasMaxLength(30);
        builder.Property(d => d.Notes).HasMaxLength(1000);

        builder.HasAlternateKey(d => new { d.SchoolId, d.Id });
        builder.HasIndex(d => new { d.SchoolId, d.Name }).IsUnique()
            .HasDatabaseName("UX_dormitories_name").HasFilter("\"IsDeleted\" = false");

        builder.HasOne<School>().WithMany().HasForeignKey(d => d.SchoolId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(d => d.SupervisorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 2 : `DormitoryRoomConfiguration`** — table `dormitory_rooms`, `xmin`, `Name` 100 requis, `HasAlternateKey(r => new { r.SchoolId, r.Id })`, index unique `UX_dormitory_rooms_name` sur `(SchoolId, DormitoryId, Name)` filtre `"IsDeleted" = false`, FK `School` Restrict, FK composite :

```csharp
builder.HasOne<Dormitory>().WithMany()
    .HasForeignKey(r => new { r.SchoolId, r.DormitoryId })
    .HasPrincipalKey(d => new { d.SchoolId, d.Id })
    .OnDelete(DeleteBehavior.Restrict);
```

- [ ] **Step 3 : `BedConfiguration`** — table `beds`, `xmin`, `HasAlternateKey`, `Status` `HasConversion<string>().HasMaxLength(20).IsRequired().HasDefaultValue(BedStatus.Available)`, index unique `UX_beds_number` sur `(SchoolId, DormitoryRoomId, BedNumber)` filtre non supprimé, FK School, FK composite vers `DormitoryRoom` (même forme qu'au step 2), et :

```csharp
builder.ToTable("beds", t =>
{
    t.HasCheckConstraint("CK_beds_stored_status", "\"Status\" IN ('Available','Maintenance')");
    t.HasCheckConstraint("CK_beds_number_positive", "\"BedNumber\" >= 1");
});
```

> Attention : `builder.ToTable("beds")` du step 1 doit être **remplacé** par cet appel unique (un seul `ToTable` par configuration).

- [ ] **Step 4 : `BoardingEnrollmentConfiguration`**

```csharp
builder.ToTable("boarding_enrollments", t =>
{
    t.HasCheckConstraint("CK_boarding_enrollments_dates", "\"EndDate\" IS NULL OR \"EndDate\" >= \"StartDate\"");
    t.HasCheckConstraint("CK_boarding_enrollments_half_board_no_bed", "\"Regime\" <> 'DemiPensionnaire' OR \"BedId\" IS NULL");
    t.HasCheckConstraint("CK_boarding_enrollments_inactive_no_bed", "\"IsActive\" OR \"BedId\" IS NULL");
    t.HasCheckConstraint("CK_boarding_enrollments_ended_has_end", "\"IsActive\" OR \"EndDate\" IS NOT NULL");
});
builder.HasKey(b => b.Id);
builder.Property(b => b.SchoolId).IsRequired();
builder.Property<uint>("xmin").IsRowVersion();
builder.Property(b => b.Regime).HasConversion<string>().HasMaxLength(20).IsRequired();
builder.Property(b => b.MedicalNotes).HasMaxLength(2000);
builder.Property(b => b.EmergencyContactName).HasMaxLength(150);
builder.Property(b => b.EmergencyContactPhone).HasMaxLength(30);
builder.OwnsMany(b => b.AllowedExitPersons, o => o.ToJson("AllowedExitPersons"));

builder.HasAlternateKey(b => new { b.SchoolId, b.Id });

builder.HasIndex(b => new { b.SchoolId, b.EnrollmentId }).IsUnique()
    .HasDatabaseName("UX_boarding_enrollments_active_enrollment")
    .HasFilter("\"IsActive\" AND NOT \"IsDeleted\"");
builder.HasIndex(b => b.BedId).IsUnique()
    .HasDatabaseName("UX_boarding_enrollments_active_bed")
    .HasFilter("\"IsActive\" AND NOT \"IsDeleted\" AND \"BedId\" IS NOT NULL");
builder.HasIndex(b => new { b.SchoolId, b.StudentId });

builder.HasOne<School>().WithMany().HasForeignKey(b => b.SchoolId).OnDelete(DeleteBehavior.Restrict);
builder.HasOne<Student>().WithMany().HasForeignKey(b => new { b.SchoolId, b.StudentId })
    .HasPrincipalKey(s => new { s.SchoolId, s.Id }).OnDelete(DeleteBehavior.Restrict);
builder.HasOne<Enrollment>().WithMany().HasForeignKey(b => new { b.SchoolId, b.EnrollmentId })
    .HasPrincipalKey(e => new { e.SchoolId, e.Id }).OnDelete(DeleteBehavior.Restrict);
builder.HasOne<Bed>().WithMany().HasForeignKey(b => new { b.SchoolId, b.BedId })
    .HasPrincipalKey(x => new { x.SchoolId, x.Id }).OnDelete(DeleteBehavior.Restrict);
```

- [ ] **Step 5 : `BoardingLeaveConfiguration`** — table `boarding_leaves` (CHECK `CK_boarding_leaves_expected` : `"ExpectedReturnDate" >= "LeaveDate"` ; `CK_boarding_leaves_actual` : `"ActualReturnDate" IS NULL OR "ActualReturnDate" >= "LeaveDate"`), `xmin`, `Reason` string(20), `AccompaniedBy` requis 150, `ReasonDetail` 500, index unique `UX_boarding_leaves_open` sur `BoardingEnrollmentId` filtre `"ActualReturnDate" IS NULL AND NOT "IsDeleted"`, `HasAlternateKey` inutile, FK School Restrict, FK composite `(SchoolId, BoardingEnrollmentId)` → `BoardingEnrollment (SchoolId, Id)` Restrict.

- [ ] **Step 6 : `BoardingAttendanceConfiguration`** — table `boarding_attendances`, `xmin`, `Note` 300, index unique `UX_boarding_attendances_night` sur `(BoardingEnrollmentId, Date)` filtre non supprimé, index `(SchoolId, Date)`, FK School + FK composite vers `BoardingEnrollment`.

- [ ] **Step 7 : compiler.** Run: `dotnet build SamaEcole.sln` — Expected: OK. Commit `feat(internat): configuration EF des entités Internat`.

---

### Task 3 : Migration du schéma + RLS (tests d'abord)

**Files:**
- Create: `tests/SamaEcole.IntegrationTests/Boarding/BoardingSchemaIsolationTests.cs`
- Generate/Modify: `src/SamaEcole.Persistence/Migrations/<ts>_AddBoardingDormitoryModel.cs`

**Interfaces — Consumes:** noms de la tâche 2.

- [ ] **Step 1 : écrire les tests (échouent : tables absentes).** Squelette, sur le modèle de `StudentSubjectExemptionIsolationTests` (`[Trait("Category","MultiTenant")]`, `IAsyncLifetime`, `RlsTestDatabase _db`). `InitializeAsync` sème par `_db.NewOwnerContext()` deux écoles A/B, chacune avec `School`, `SchoolYear` active, `Classroom`, `Student`, `Enrollment` (un `Enrollment` : `Type = EnrollmentType.NewEnrollment`, `Status = EnrollmentStatus.Confirmed`, `ReceiptNumber = "REC-A-1"`/`"REC-B-1"`, `EnrolledAt = DateTimeOffset.UtcNow`). Helpers privés (SQL brut sous `_db.OpenRawAppConnectionAsync(session)`) :

```csharp
private async Task<Guid> InsertDormitoryAsync(Guid session, Guid school, string name = "Pavillon A")
    => await InsertAsync(session, """
        INSERT INTO dormitories ("Id","SchoolId","Name","Gender","CreatedAt","IsDeleted")
        VALUES (@id,@school,@name,'Garcons',NOW(),FALSE)
        """, ("school", school), ("name", name));

private async Task<Guid> InsertRoomAsync(Guid session, Guid school, Guid dormitory, string name = "Ch. 1") => ... // dormitory_rooms
private async Task<Guid> InsertBedAsync(Guid session, Guid school, Guid room, int number = 1, string status = "Available") => ... // beds
private async Task<Guid> InsertBoarderAsync(Guid session, Guid school, Guid student, Guid enrollment,
    string regime = "Interne", Guid? bed = null, bool active = true) => ... // boarding_enrollments, "StartDate" = DATE '2026-09-15',
        // "EndDate" = CASE WHEN @active THEN NULL ELSE DATE '2026-10-01' END, "AllowedExitPersons" = '[]'::jsonb
private async Task<Guid> InsertLeaveAsync(Guid session, Guid school, Guid boarder, string leave = "2026-10-02", string expected = "2026-10-04", string? actual = null) => ...
private async Task<Guid> InsertAttendanceAsync(Guid session, Guid school, Guid boarder, string date = "2026-10-02") => ...
```

`InsertAsync(session, sql, params (string name, object? value)[] p)` génère un `Guid.NewGuid()` pour `@id`, exécute `ExecuteNonQueryAsync` et le retourne. Tests (`[Fact]`, noms anglais comme le reste du dépôt) :

1. `Raw_Query_Should_Never_Return_Other_School_Dormitories` — insère un pavillon par école, `count(*)` sous A = 1, sous B = 1, sous `null` = 0.
2. `Writing_A_Dormitory_Into_Another_School_Should_Be_Rejected` — session A, `SchoolId` B → `PostgresException` `InsufficientPrivilege`.
3. `The_Application_Role_Cannot_Physically_Delete_Any_Boarding_Row` — `[Theory]` sur les 6 tables : `DELETE FROM {table}` → `InsufficientPrivilege`.
4. `A_Dormitory_Name_Is_Unique_Per_School_Until_Soft_Deleted` — doublon → `UniqueViolation` ; après `UPDATE ... SET "IsDeleted"=TRUE` la recréation passe ; même nom dans l'école B autorisé.
5. `A_Bed_Can_Be_Held_By_One_Active_Boarder_Only` — deux séjours actifs sur le même lit → `UniqueViolation` ; après avoir clos le premier (`IsActive=FALSE, EndDate=…, BedId=NULL`), le second passe.
6. `An_Enrollment_Has_One_Active_Boarding_Stay` — deux séjours actifs pour le même `EnrollmentId` → `UniqueViolation`.
7. `A_Half_Boarder_Cannot_Hold_A_Bed` ; `An_Inactive_Boarder_Cannot_Hold_A_Bed` ; `An_Ended_Stay_Must_Have_An_End_Date` — `CheckViolation`.
8. `A_Bed_Cannot_Be_Stored_As_Occupied` — `status = 'Occupied'` → `CheckViolation`.
9. `A_Boarder_Cannot_Reference_A_Bed_Of_Another_School` — lit de B référencé depuis un séjour de A → `ForeignKeyViolation`.
10. `A_Boarder_Has_At_Most_One_Open_Leave` ; `A_Leave_Cannot_Return_Before_It_Starts` (`expected < leave` → `CheckViolation`).
11. `Attendance_Is_Unique_Per_Boarder_And_Night_Until_Soft_Deleted`.

- [ ] **Step 2 : lancer — doit échouer.** Run: `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~BoardingSchemaIsolationTests"` — Expected: FAIL (`relation "dormitories" does not exist`).

- [ ] **Step 3 : générer la migration.** Run: `dotnet ef migrations add AddBoardingDormitoryModel -p src/SamaEcole.Persistence -s src/SamaEcole.Web`. Relire le fichier : six `CreateTable`, des `AddUniqueConstraint` `(SchoolId, Id)` sur `enrollments` (et seulement les clés manquantes), les `CreateIndex` filtrés, les CHECK. Aucun `DropColumn`/`DropTable` d'objet existant ne doit apparaître — sinon STOP, le modèle a dérivé.

- [ ] **Step 4 : ajouter la RLS à la main.** En tête de la classe `private static readonly string[] TenantTables = ["dormitories","dormitory_rooms","beds","boarding_enrollments","boarding_leaves","boarding_attendances"]; private const string AppRole = "sama_ecole_app";`. À la fin de `Up`, **copier à l'identique** la boucle `foreach (var table in TenantTables)` de `20260925191029_AddStudentSubjectExemptions.cs` (ENABLE RLS, `CREATE POLICY {table}_tenant_isolation`, `GRANT SELECT, INSERT, UPDATE`). En tête de `Down`, la boucle `DROP POLICY IF EXISTS {table}_tenant_isolation ON "{table}";`. Ajouter au commentaire de classe : « EF ne génère pas les policies RLS (AGENTS.md règle #2) ».

- [ ] **Step 5 : relancer — doit passer.** Run: la commande du step 2 puis `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~RlsCoverageTests"` — Expected: PASS (le test générique voit les six tables).

- [ ] **Step 6 : commit** `feat(internat): migration AddBoardingDormitoryModel (tables, contraintes, RLS)`.

---

### Task 4 : Reprise des données (tests d'abord)

**Files:**
- Create: `tests/SamaEcole.IntegrationTests/Boarding/BoardingBackfillMigrationTests.cs`
- Modify: `src/SamaEcole.Persistence/Migrations/<ts>_AddBoardingDormitoryModel.cs` (`Up`, après la boucle RLS)

- [ ] **Step 1 : écrire le test.** Même mécanique que `TenantSubscriptionSchemaTests` : `private const string PreviousMigration = "20261006145320_AddManagedCyclesToSchoolSettings";`, `MigrateToAsync(string? target)` via `owner.GetService<IMigrator>().MigrateAsync(target)`. Données **héritées** semées par EF dans le schéma courant (école A : année active « 2026-2027 » + année passée « 2025-2026 » ; école B : un dortoir avec un interne, pour prouver l'isolation) :

| Bâtiment | Chambres (type, capacité) | Inscriptions (année active sauf mention) |
|---|---|---|
| Pavillon Garçons | Dortoir 101 (2), Dortoir 102 (1) | g1, g2 `M` Interne ch.101 ; g3, g4 `M` Interne ch.102 ; g1 **année passée** Interne ch.101 |
| Pavillon Filles | Dortoir 201 (2) | f1 `F` Interne ch.201 ; f2 `F` DemiPensionnaire ch.201 |
| Pavillon Mixte | Dortoir 301 (3) | m1 `M`, m2 `F` Interne ch.301 |
| Bloc classes | Salle 1 (`SalleDeClasse`, 30) | — |
| — | — | x1 Interne `RoomId = null` ; x2 Externe ; x3 Interne **Cancelled** ch.101 |

`EnrolledAt` croissant et distinct par inscription (`new DateTimeOffset(2026, 9, 1 + i, 8, 0, 0, TimeSpan.Zero)`). Après `MigrateToAsync(PreviousMigration)` puis `MigrateToAsync(null)`, vérifier en SQL brut propriétaire :

- 3 `dormitories` pour l'école A, dont `Id` = `Building.Id` ; genres `Garcons`, `Filles`, `Mixte` ; aucun pour « Bloc classes ».
- 4 `dormitory_rooms` d'`Id` = `Room.Id`.
- Lits : ch.101 → 2, ch.102 → **2** (`max(capacité 1, 2 internes)`), ch.201 → 2, ch.301 → 3 (soit 9) ; numérotés 1..N.
- `boarding_enrollments` de l'école A : 9 actifs (g1,g2,g3,g4,f1,f2,m1,m2,x1) + 1 clos (g1 année passée : `IsActive=false`, `EndDate` = fin de l'année passée, `BedId` nul). x2 et x3 : aucune ligne.
- g1 et g2 : lits n°1 et n°2 de la ch.101, dans l'ordre d'`EnrolledAt` ; f2 (`DemiPensionnaire`) et x1 : `BedId` nul ; aucun `BedId` en doublon.
- Données héritées intactes : `enrollments."RoomId"` et `"BoardingStatus"` inchangés.
- L'école B a sa propre reprise et rien de l'école A.
- **Contrôle d'invariants (spec §4.1.5)** : `count(boarding_enrollments actifs)` = `count(enrollments de l'année active, régime ≠ Externe, statut ≠ Cancelled)`.

Deuxième test : `Migration_Is_Reversible_And_Replayable` — `Previous` → `null` → `Previous` → `null`, mêmes comptes ; les tables n'existent pas après `Previous` (`to_regclass`).

- [ ] **Step 2 : lancer — doit échouer** (tables vides après migration). Run: `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~BoardingBackfillMigrationTests"` — Expected: FAIL (0 pavillon).

- [ ] **Step 3 : ajouter le SQL de reprise** à la fin de `Up` (après la RLS), en quatre `migrationBuilder.Sql(...)`. Chaque instruction est idempotente (`ON CONFLICT DO NOTHING` / `NOT EXISTS`). La migration s'exécute avec le rôle propriétaire (exempté de RLS).

```sql
-- 1. Pavillons : un par bâtiment vivant ayant ≥ 1 chambre Dortoir vivante. Id = Building.Id.
--    Genre déduit des élèves de l'année ACTIVE logés dans ses chambres ; ambigu ou vide → Mixte.
INSERT INTO dormitories ("Id","SchoolId","Name","Gender","CreatedAt","IsDeleted")
SELECT b."Id", b."SchoolId", b."Name",
       CASE g.gender WHEN 'M' THEN 'Garcons' WHEN 'F' THEN 'Filles' ELSE 'Mixte' END,
       NOW(), FALSE
FROM buildings b
LEFT JOIN LATERAL (
    SELECT CASE WHEN count(DISTINCT s."Gender") = 1 THEN min(s."Gender") END AS gender
    FROM enrollments e
    JOIN school_years y ON y."SchoolId" = e."SchoolId" AND y."Id" = e."SchoolYearId" AND y."IsActive" AND NOT y."IsDeleted"
    JOIN rooms r ON r."SchoolId" = e."SchoolId" AND r."Id" = e."RoomId"
    JOIN students s ON s."SchoolId" = e."SchoolId" AND s."Id" = e."StudentId"
    WHERE r."BuildingId" = b."Id" AND r."Type" = 'Dortoir' AND NOT r."IsDeleted"
      AND e."BoardingStatus" <> 'Externe' AND e."Status" <> 'Cancelled' AND NOT e."IsDeleted"
) g ON TRUE
WHERE NOT b."IsDeleted"
  AND EXISTS (SELECT 1 FROM rooms r WHERE r."SchoolId" = b."SchoolId" AND r."BuildingId" = b."Id"
              AND r."Type" = 'Dortoir' AND NOT r."IsDeleted")
ON CONFLICT ("Id") DO NOTHING;

-- 2. Chambres : Id = Room.Id.
INSERT INTO dormitory_rooms ("Id","SchoolId","DormitoryId","Name","CreatedAt","IsDeleted")
SELECT r."Id", r."SchoolId", r."BuildingId", r."Name", NOW(), FALSE
FROM rooms r
JOIN dormitories d ON d."SchoolId" = r."SchoolId" AND d."Id" = r."BuildingId"
WHERE r."Type" = 'Dortoir' AND NOT r."IsDeleted"
ON CONFLICT ("Id") DO NOTHING;

-- 3. Lits : max(capacité, internes de l'année active) par chambre, numérotés 1..N.
INSERT INTO beds ("Id","SchoolId","DormitoryRoomId","BedNumber","Status","CreatedAt","IsDeleted")
SELECT gen_random_uuid(), dr."SchoolId", dr."Id", n::int, 'Available', NOW(), FALSE
FROM dormitory_rooms dr
JOIN rooms r ON r."SchoolId" = dr."SchoolId" AND r."Id" = dr."Id"
CROSS JOIN LATERAL generate_series(1, GREATEST(r."Capacity", (
    SELECT count(*) FROM enrollments e
    JOIN school_years y ON y."SchoolId" = e."SchoolId" AND y."Id" = e."SchoolYearId" AND y."IsActive" AND NOT y."IsDeleted"
    WHERE e."SchoolId" = r."SchoolId" AND e."RoomId" = r."Id" AND e."BoardingStatus" = 'Interne'
      AND e."Status" <> 'Cancelled' AND NOT e."IsDeleted"))) AS n
WHERE NOT EXISTS (SELECT 1 FROM beds b WHERE b."SchoolId" = dr."SchoolId" AND b."DormitoryRoomId" = dr."Id");

-- 4. Séjours. Année active → actif (lit n° rang d'inscription pour un Interne dont la chambre a été reprise) ;
--    autre année → clos à la fin de l'année, sans lit.
WITH legacy AS (
    SELECT e."Id" AS enrollment_id, e."SchoolId", e."StudentId", e."BoardingStatus" AS regime, e."RoomId",
           (e."EnrolledAt" AT TIME ZONE 'UTC')::date AS started, y."IsActive" AS year_active, y."EndDate" AS year_end,
           CASE WHEN y."IsActive" AND e."BoardingStatus" = 'Interne' AND e."RoomId" IS NOT NULL
                THEN row_number() OVER (PARTITION BY e."RoomId" ORDER BY e."EnrolledAt", e."Id") END AS seat
    FROM enrollments e
    JOIN school_years y ON y."SchoolId" = e."SchoolId" AND y."Id" = e."SchoolYearId"
    WHERE e."BoardingStatus" <> 'Externe' AND e."Status" <> 'Cancelled' AND NOT e."IsDeleted"
)
INSERT INTO boarding_enrollments
    ("Id","SchoolId","StudentId","EnrollmentId","Regime","BedId","StartDate","EndDate","IsActive","AllowedExitPersons","CreatedAt","IsDeleted")
SELECT gen_random_uuid(), l."SchoolId", l."StudentId", l.enrollment_id, l.regime, bed."Id",
       l.started, CASE WHEN l.year_active THEN NULL ELSE GREATEST(l.year_end, l.started) END,
       l.year_active, '[]'::jsonb, NOW(), FALSE
FROM legacy l
LEFT JOIN beds bed ON bed."SchoolId" = l."SchoolId" AND bed."DormitoryRoomId" = l."RoomId"
                  AND bed."BedNumber" = l.seat AND NOT bed."IsDeleted"
WHERE NOT EXISTS (SELECT 1 FROM boarding_enrollments be WHERE be."SchoolId" = l."SchoolId" AND be."EnrollmentId" = l.enrollment_id);
```

> Si `AllowedExitPersons` est généré `nullable` ou avec un autre type par EF, adapter la valeur insérée (lire la migration générée au Task 3 step 3). `Down` n'a rien à reprendre : il supprime les tables et laisse les colonnes héritées telles quelles.

- [ ] **Step 4 : relancer.** Run: `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~BoardingBackfillMigrationTests|FullyQualifiedName~BoardingSchemaIsolationTests"` — Expected: PASS.

- [ ] **Step 5 : commit** `feat(internat): reprise des dortoirs, lits et séjours existants dans la migration`.

---

### Task 5 : Patch des fonctions de purge (tests d'abord)

**Files:**
- Create: `tests/SamaEcole.IntegrationTests/Boarding/BoardingPurgeTests.cs`
- Create: `src/SamaEcole.Persistence/Migrations/<ts>_AddBoardingToPurges.cs` (+ Designer) via `dotnet ef migrations add AddBoardingToPurges …` (migration vide générée, puis remplie à la main — convention de `AddStudentSubjectExemptionsToPurges`)

- [ ] **Step 1 : écrire les tests** (réutiliser les helpers de la tâche 3 en les extrayant dans une classe `BoardingSqlSeed` partagée dans `tests/SamaEcole.IntegrationTests/Boarding/` si le copier-coller dépasse 40 lignes). Données : école A (année active + année passée, un séjour par année, avec une sortie et un pointage chacun), école B (un séjour).

  - `Resetting_The_School_Data_Removes_Boarding_Rows_Of_That_School_Only` : `SELECT count(*) FROM reset_school_data(@school)` sous A → les 6 tables vides pour A, B intact.
  - `Deleting_A_School_Year_Removes_Its_Stays_Leaves_And_Attendances_And_Keeps_Other_Years` : `delete_school_year(@school, @pastYear)` → le séjour de l'année passée, sa sortie et son pointage disparaissent ; ceux de l'année active et les pavillons/chambres/lits (non rattachés à une année) subsistent.
  - Relancer aussi les gardes génériques `Every_Restrict_Foreign_Key_Into_A_Purged_Table_Must_Come_From_A_Purged_Table_Too` (`ResetSchoolDataTests`) et `Every_Restrict_Foreign_Key_Into_A_Handled_Table_Must_Come_From_A_Handled_Table_Too` (`DeleteSchoolYearTests`) : elles doivent **échouer avant** le patch (FK `boarding_*` → `enrollments`/`students`) et passer après.

- [ ] **Step 2 : lancer — doit échouer.** Run: `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~BoardingPurgeTests|FullyQualifiedName~Every_Restrict_Foreign_Key"` — Expected: FAIL (23503 ou garde générique).

- [ ] **Step 3 : écrire la migration.** Copier la structure de `20261005205214_AddDaaraHalqaAndHizbTracking.cs` (méthodes `InsertBefore(function, anchor, inserted, marker)` et `Remove(function, with, without, marker)` **telles quelles**) et de `20260925191146_AddStudentSubjectExemptionsToPurges.cs` (étape `delete_school_year`). Constantes :

```csharp
// reset_school_data : enfants avant parents, tous avant « enrollments » (FK RESTRICT vers enrollments/students).
private const string ResetAnchor = "['enrollments',";
private const string ResetInsert =
    "['boarding_attendances', 'Pointages de nuit'],\n                        " +
    "['boarding_leaves', 'Sorties et permissions'],\n                        " +
    "['boarding_enrollments', 'Séjours à l''internat'],\n                        " +
    "['beds', 'Lits'],\n                        " +
    "['dormitory_rooms', 'Chambres d''internat'],\n                        " +
    "['dormitories', 'Pavillons'],\n                        ";

// delete_school_year : seuls les séjours de l'année sont retirés (pavillons/chambres/lits ne sont pas annuels).
private const string YearAnchor = "DELETE FROM enrollments";
private const string YearStep =
    "DELETE FROM boarding_attendances WHERE \"SchoolId\" = p_school_id AND \"BoardingEnrollmentId\" IN (\n" +
    "        SELECT be.\"Id\" FROM boarding_enrollments be JOIN enrollments e ON e.\"SchoolId\" = be.\"SchoolId\" AND e.\"Id\" = be.\"EnrollmentId\"\n" +
    "        WHERE e.\"SchoolYearId\" = p_school_year_id AND e.\"SchoolId\" = p_school_id);\n" +
    "    GET DIAGNOSTICS v_deleted = ROW_COUNT;\n" +
    "    label := 'Pointages de nuit'; rows_deleted := v_deleted; RETURN NEXT;\n\n" +
    "    DELETE FROM boarding_leaves WHERE \"SchoolId\" = p_school_id AND \"BoardingEnrollmentId\" IN (\n" +
    "        SELECT be.\"Id\" FROM boarding_enrollments be JOIN enrollments e ON e.\"SchoolId\" = be.\"SchoolId\" AND e.\"Id\" = be.\"EnrollmentId\"\n" +
    "        WHERE e.\"SchoolYearId\" = p_school_year_id AND e.\"SchoolId\" = p_school_id);\n" +
    "    GET DIAGNOSTICS v_deleted = ROW_COUNT;\n" +
    "    label := 'Sorties et permissions'; rows_deleted := v_deleted; RETURN NEXT;\n\n" +
    "    DELETE FROM boarding_enrollments be USING enrollments e\n" +
    "    WHERE be.\"SchoolId\" = p_school_id AND e.\"SchoolId\" = be.\"SchoolId\" AND e.\"Id\" = be.\"EnrollmentId\"\n" +
    "      AND e.\"SchoolYearId\" = p_school_year_id;\n" +
    "    GET DIAGNOSTICS v_deleted = ROW_COUNT;\n" +
    "    label := 'Séjours à l''internat'; rows_deleted := v_deleted; RETURN NEXT;\n\n    ";
```

`Up` : `Sql(InsertBefore("reset_school_data(uuid)", ResetAnchor, ResetInsert, "boarding_attendances"))` et `Sql(InsertBefore("delete_school_year(uuid, uuid)", YearAnchor, YearStep, "boarding_attendances"))`. `Down` : les deux `Remove(...)` avec `ResetInsert + ResetAnchor` / `YearStep + YearAnchor`. Si l'ancre est introuvable ou ambiguë la migration **échoue** (comportement voulu) : relire la définition courante (`SELECT pg_get_functiondef('reset_school_data(uuid)'::regprocedure)`) avant d'ajuster.

> Vérifier dans la définition courante de `delete_school_year` le nom exact des variables (`p_school_id`, `p_school_year_id`, `v_deleted`, `label`, `rows_deleted`) — ils sont repris de `AddStudentSubjectExemptionsToPurges`.

- [ ] **Step 4 : relancer.** Run: la commande du step 2, puis `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~ResetSchoolDataTests|FullyQualifiedName~DeleteSchoolYearTests"` — Expected: PASS. Ajouter un test aller-retour : `Down` du patch laisse les fonctions identiques à l'octet (`pg_get_functiondef` avant/après `MigrateTo` précédent).

- [ ] **Step 5 : commit** `feat(internat): inscrit les tables Internat dans reset_school_data et delete_school_year`.

---

### Task 6 : Vérification globale et documentation

**Files:** Modify `docs/ACTIVE_CONTEXT.md`, `docs/Volume_3_DDS.md` (section schéma).

- [ ] **Step 1 : `dotnet build SamaEcole.sln`** — Expected : aucun nouvel avertissement.
- [ ] **Step 2 : suite complète.** Run: `dotnet test SamaEcole.sln` — Expected : tout vert ; les échecs intermittents déjà connus (Documents, Daara, Quran, `TenantSubscriptionSchemaTests`) se relancent seuls en vert — sinon les signaler, ne pas les masquer.
- [ ] **Step 3 : aucune dérive du modèle.** Run: `dotnet ef migrations has-pending-model-changes -p src/SamaEcole.Persistence -s src/SamaEcole.Web` (ou le test de snapshot existant) — Expected : aucun changement en attente.
- [ ] **Step 4 : documentation.** `docs/Volume_3_DDS.md` : décrire les six tables, leurs index partiels et CHECK. `docs/ACTIVE_CONTEXT.md` : entrée « Internat — lot A livré (schéma + reprise + purges), anciennes colonnes conservées jusqu'au lot F ».
- [ ] **Step 5 : commit** `docs(internat): schéma Pavillon/Lit dans le DDS et ACTIVE_CONTEXT`, puis `git push -u origin feat/internat-backend` et ouverture de la PR (gabarit `.github/PULL_REQUEST_TEMPLATE.md` ; cocher « nouvelle table `ITenantEntity` : test `MultiTenant` »).

---

## Self-review

- **Couverture de la spec** : §3.1–3.6 → tâches 1-2 ; §3.7 RLS → tâche 3, purges → tâche 5, soft delete (index partiels) → tâche 2 ; §4.1 reprise → tâche 4 (avec la correction « année active ») ; N2/N3/N7 → CHECK et index uniques tâche 2-3. **Hors lot A, volontairement** : §4.2 bascule, §4.3 contract, §5–§8 (handlers, endpoints, PDF, IHM) = lots B à G.
- **Placeholders** : aucun ; la seule adaptation conditionnelle (`AllowedExitPersons` nullable) est signalée avec sa vérification.
- **Cohérence des noms** : tables, index et `DbSet` identiques entre tâches 1, 2, 3, 4, 5.
