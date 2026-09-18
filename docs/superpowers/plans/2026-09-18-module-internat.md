# Module Internat — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rendre le module Internat (`SchoolSettings.IsInternatEnabled`) pleinement opérationnel : logement (pavillon/chambre), régime (Externe/Demi-pensionnaire/Interne) porté par l'inscription, facturation de la pension, écran de gestion `/internat`, et intégration dans la fiche élève et le formulaire d'inscription.

**Architecture:** Réutilise `Building`/`Room` (module Infrastructures, +`RoomType.Dortoir`) et `FeeCategory`/`ClassFee` (module Frais, +`FeeCategory.IsBoardingFee`) plutôt que de construire un moteur parallèle. Le régime et la chambre vivent sur `Enrollment` (portée annuelle, comme `IsRepeating`) ; l'occupation d'une chambre est un simple comptage (`COUNT(Enrollment actives) < Room.Capacity`), pas d'entité `Bed`. CQRS/MediatR strict (AGENTS.md règle #7), verrou optimiste xmin déjà porté par `Enrollment` (règle #5), `IAuditableRequest` pour la traçabilité des changements de chambre.

**Tech Stack:** ASP.NET Core 9 (C#), EF Core + Npgsql, MediatR, FluentValidation, Alpine.js + Tailwind (Razor), xUnit + FluentAssertions + Testcontainers PostgreSQL.

**Spec:** `docs/superpowers/specs/2026-09-18-module-internat-design.md` (validée, commit `a538cae`).

## Global Constraints

- PostgreSQL uniquement, jamais de code conditionnel multi-SGBD (AGENTS.md règle #1).
- Toute table tenant porte RLS **+** Global Query Filter — `enrollments`, `rooms`, `fee_categories` sont déjà protégées ; cette plan n'ajoute AUCUNE table, seulement des colonnes (règle #2).
- Aucune suppression physique (règle #6) — la ligne de pension déjà facturée n'est jamais retirée à la libération (spec §2, décision #7).
- Verrouillage optimiste xmin sur `Enrollment` — tout changement passe par `SetOriginalConcurrencyToken` (règle #5).
- CQRS strict : Commands pour l'écriture, Queries pour la lecture, aucun service ne mélange les deux (règle #7).
- Aucune logique métier dans un contrôleur (règle #8) — les contrôleurs de ce plan ne font que traduire HTTP ↔ MediatR.
- Le `SchoolId` vient toujours du JWT via `ITenantProvider`, jamais d'un paramètre client (règle #10).
- Erreurs API au format normalisé existant (`ValidationException` → 422 avec dictionnaire de champs, `ConcurrencyConflictException` → 409, `KeyNotFoundException` → 404) — ne pas inventer un nouveau format.

---

### Task 1: Domaine — enums et propriétés d'entité

**Files:**
- Modify: `src/SamaEcole.Domain/Enums/CommonEnums.cs:361-367` (ajouter `Dortoir` à `RoomType`)
- Create: `src/SamaEcole.Domain/Enums/BoardingStatus.cs`
- Modify: `src/SamaEcole.Domain/Entities/Enrollment.cs` (+ `BoardingStatus`, `RoomId`)
- Modify: `src/SamaEcole.Domain/Entities/FeeCategory.cs` (+ `IsBoardingFee`)

**Interfaces:**
- Produces: `SamaEcole.Domain.Enums.BoardingStatus { Externe, DemiPensionnaire, Interne }`, `RoomType.Dortoir`, `Enrollment.BoardingStatus` (défaut `Externe`), `Enrollment.RoomId` (`Guid?`), `FeeCategory.IsBoardingFee` (`bool`, défaut `false`).

- [ ] **Step 1: Ajouter `Dortoir` à `RoomType`**

Dans `src/SamaEcole.Domain/Enums/CommonEnums.cs`, remplacer :
```csharp
public enum RoomType
{
    SalleDeClasse,
    Laboratoire,
    Bureau,
    Autre
}
```
par :
```csharp
public enum RoomType
{
    SalleDeClasse,
    Laboratoire,
    Bureau,
    Dortoir,
    Autre
}
```

- [ ] **Step 2: Créer l'enum `BoardingStatus`**

Créer `src/SamaEcole.Domain/Enums/BoardingStatus.cs` :
```csharp
namespace SamaEcole.Domain.Enums;

/// <summary>
/// Régime d'hébergement d'un élève (module Internat), porté par <see cref="Entities.Enrollment"/> —
/// portée ANNUELLE, comme <see cref="Entities.Enrollment.IsRepeating"/> : une réinscription
/// reconfirme ou change le régime, jamais un report automatique d'une année sur l'autre.
/// </summary>
public enum BoardingStatus
{
    /// <summary>Ne loge pas dans l'établissement. Valeur par défaut.</summary>
    Externe,

    /// <summary>Prend au moins un repas sur place mais ne loge pas la nuit.</summary>
    DemiPensionnaire,

    /// <summary>Loge dans l'établissement — seul régime qui exige une <see cref="Entities.Enrollment.RoomId"/>.</summary>
    Interne
}
```

- [ ] **Step 3: Étendre `Enrollment`**

Dans `src/SamaEcole.Domain/Entities/Enrollment.cs`, ajouter en fin de classe (avant l'accolade fermante), juste après `EnrolledAt` :
```csharp
    /// <summary>
    /// Régime d'hébergement (module Internat), portée ANNUELLE comme <see cref="IsRepeating"/>.
    /// Défaut Externe : le module est désactivé par défaut (SchoolSettingsDefaults.IsInternatEnabled),
    /// donc toute inscription existante ou nouvelle sans saisie explicite reste Externe.
    /// </summary>
    public BoardingStatus BoardingStatus { get; set; } = BoardingStatus.Externe;

    /// <summary>
    /// Chambre affectée (module Internat) — significatif seulement si <see cref="BoardingStatus"/> ≠
    /// Externe. Null pour un externe, ou un interne/demi-pensionnaire pas encore affecté à une
    /// chambre précise (l'école a choisi le régime avant de loger l'élève).
    /// </summary>
    public Guid? RoomId { get; set; }
```
Ajouter `using SamaEcole.Domain.Enums;` en tête de fichier si absent (déjà présent, le fichier importe déjà `SamaEcole.Domain.Enums` pour `EnrollmentType`/`EnrollmentStatus`).

- [ ] **Step 4: Étendre `FeeCategory`**

Dans `src/SamaEcole.Domain/Entities/FeeCategory.cs`, ajouter après `IsRecurring` :
```csharp
    /// <summary>
    /// Désigne cette catégorie comme frais d'internat (module Internat) : elle peut être incluse
    /// automatiquement sur l'inscription d'un élève Interne/Demi-pensionnaire (voir
    /// BoardingFeeLineBuilder), en plus des ClassFee ordinaires appliqués à tous les élèves de la
    /// classe. Faux par défaut — un choix explicite de l'école, comme IsRecurring.
    /// </summary>
    public bool IsBoardingFee { get; set; }
```

- [ ] **Step 5: Vérifier la compilation**

Run: `dotnet build`
Expected: `Build succeeded.` (0 erreur, 0 avertissement nouveau).

- [ ] **Step 6: Commit**

```bash
git add src/SamaEcole.Domain/Enums/CommonEnums.cs src/SamaEcole.Domain/Enums/BoardingStatus.cs src/SamaEcole.Domain/Entities/Enrollment.cs src/SamaEcole.Domain/Entities/FeeCategory.cs
git commit -m "feat(internat): ajouter BoardingStatus, RoomType.Dortoir et FeeCategory.IsBoardingFee"
```

---

### Task 2: Persistence — configuration EF et migration

**Files:**
- Modify: `src/SamaEcole.Persistence/Configurations/RoomConfiguration.cs` (+ clé alternative `(SchoolId, Id)`)
- Modify: `src/SamaEcole.Persistence/Configurations/FeeCategoryConfiguration.cs` (+ `IsBoardingFee`)
- Modify: `src/SamaEcole.Persistence/Configurations/EnrollmentConfiguration.cs` (+ `BoardingStatus`, `RoomId` + FK composite)
- Create: migration `AddInternatBoarding` (via `dotnet ef migrations add`)

**Interfaces:**
- Consumes: `Enrollment.BoardingStatus`/`RoomId`, `FeeCategory.IsBoardingFee` (Task 1).
- Produces: colonnes `enrollments.boarding_status` (text, défaut `'Externe'`), `enrollments.room_id` (uuid, nullable, FK composite `(school_id, room_id)` → `rooms(school_id, id)`), `fee_categories.is_boarding_fee` (bool, défaut `false`).

- [ ] **Step 1: Ajouter la clé alternative sur `Room`**

Dans `src/SamaEcole.Persistence/Configurations/RoomConfiguration.cs`, après la ligne `builder.Property(r => r.Type)...` (avant les `HasIndex`), ajouter :
```csharp
        // Clé alternative (SchoolId, Id) : cible de la FK COMPOSITE d'enrollments.room_id (module
        // Internat), même raisonnement que FeeCategoryConfiguration — sans elle, une inscription
        // pourrait pointer une chambre d'une AUTRE école (la RLS masque, mais n'empêche pas d'exister).
        builder.HasAlternateKey(r => new { r.SchoolId, r.Id });
```

- [ ] **Step 2: Configurer `FeeCategory.IsBoardingFee`**

Dans `src/SamaEcole.Persistence/Configurations/FeeCategoryConfiguration.cs`, après `builder.Property(c => c.IsRecurring).IsRequired();`, ajouter :
```csharp
        builder.Property(c => c.IsBoardingFee).IsRequired().HasDefaultValue(false);
```

- [ ] **Step 3: Configurer `Enrollment.BoardingStatus`/`RoomId`**

Dans `src/SamaEcole.Persistence/Configurations/EnrollmentConfiguration.cs`, après `builder.Property(e => e.IsRepeating).IsRequired().HasDefaultValue(false);`, ajouter :
```csharp
        // Régime d'hébergement (module Internat) : défaut base 'Externe' pour que toute inscription
        // existante (créée avant cette colonne) reste Externe, jamais NULL — même contrat qu'IsRepeating.
        builder.Property(e => e.BoardingStatus)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(BoardingStatus.Externe);
```
Après le bloc `builder.HasOne<SchoolYear>()...OnDelete(DeleteBehavior.Restrict);` (fin de la méthode), ajouter :
```csharp

        // FK COMPOSITE (SchoolId, RoomId) → Room, même défense anti cross-tenant que Student/Classroom/
        // SchoolYear ci-dessus. Nullable : une chambre non affectée (régime posé, logement pas encore
        // choisi) ou un élève Externe.
        builder.HasOne<Room>()
            .WithMany()
            .HasForeignKey(e => new { e.SchoolId, e.RoomId })
            .HasPrincipalKey(r => new { r.SchoolId, r.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasIndex(e => new { e.SchoolId, e.RoomId });
```
Ajouter `using SamaEcole.Domain.Enums;` en tête de fichier si absent.

- [ ] **Step 4: Générer la migration**

Run: `dotnet ef migrations add AddInternatBoarding -p src/SamaEcole.Persistence -s src/SamaEcole.Web`
Expected: fichier `src/SamaEcole.Persistence/Migrations/<timestamp>_AddInternatBoarding.cs` créé, avec dans `Up()` : `AddColumn<string>("boarding_status", "enrollments", defaultValue: "Externe")`, `AddColumn<Guid>("room_id", "enrollments", nullable: true)`, `AddColumn<bool>("is_boarding_fee", "fee_categories", defaultValue: false)`, un `AddForeignKey` composite sur `(school_id, room_id)` et un `CreateIndex` sur `(school_id, room_id)`.

Ouvrir le fichier généré et vérifier qu'AUCUNE ligne ne touche `TenantTables`, une policy RLS ou un `GRANT` — ce sont des colonnes sur des tables déjà protégées (Global Constraints), pas de nouvelle table. Si l'échafaudage EF a inféré un `defaultValue` incorrect pour `boarding_status` (ex. chaîne vide au lieu de `"Externe"`), le corriger à la main avant application (même vigilance que la migration `AddParentSummonsOutcome`, ACTIVE_CONTEXT.md §Présences & Discipline).

- [ ] **Step 5: Appliquer la migration en local**

Run: `dotnet ef database update -p src/SamaEcole.Persistence -s src/SamaEcole.Web`
Expected: `Done.` sans erreur. Si la base locale n'est pas démarrée, `docker compose up -d` d'abord.

- [ ] **Step 6: Vérifier la compilation**

Run: `dotnet build`
Expected: `Build succeeded.`

- [ ] **Step 7: Commit**

```bash
git add src/SamaEcole.Persistence/Configurations/RoomConfiguration.cs src/SamaEcole.Persistence/Configurations/FeeCategoryConfiguration.cs src/SamaEcole.Persistence/Configurations/EnrollmentConfiguration.cs src/SamaEcole.Persistence/Migrations/
git commit -m "feat(internat): migration AddInternatBoarding (boarding_status, room_id, is_boarding_fee)"
```

---

### Task 3: Application — helper partagé `BoardingFeeLineBuilder`

**Files:**
- Create: `src/SamaEcole.Application/Enrollments/BoardingFeeLineBuilder.cs`
- Test: `tests/SamaEcole.UnitTests/Enrollments/BoardingFeeLineBuilderTests.cs`

**Interfaces:**
- Consumes: `IApplicationDbContext.ClassFees`, `.FeeCategories` (existants), `FeeCategory.IsBoardingFee` (Task 1).
- Produces: `BoardingFeeLineBuilder.BuildMissingBoardingLinesAsync(IApplicationDbContext dbContext, Guid schoolId, Guid classroomId, int tuitionMonths, IReadOnlySet<Guid> existingFeeCategoryIds, CancellationToken ct) : Task<List<EnrollmentFeeLine>>` — utilisé par Task 4 (`CreateEnrollmentCommandHandler`) et Task 7 (`ChangeBoardingAssignmentCommandHandler`).

Ce helper factorise EXACTEMENT le même calcul que `CreateEnrollmentCommandHandler.BuildFeeLinesAsync` (montants copiés depuis `ClassFee`, mensualité × `tuitionMonths`), restreint aux catégories `IsBoardingFee` et EXCLUANT celles déjà présentes sur l'inscription (`existingFeeCategoryIds`) — c'est ce paramètre qui garantit qu'un second changement de chambre ne double-facture jamais la pension (spec §5.2).

- [ ] **Step 1: Écrire le test (échoue — le type n'existe pas encore)**

Créer `tests/SamaEcole.UnitTests/Enrollments/BoardingFeeLineBuilderTests.cs` :
```csharp
using SamaEcole.Application.Enrollments;
using SamaEcole.Domain.Entities;
using SamaEcole.UnitTests.Common;
using FluentAssertions;
using Xunit;

namespace SamaEcole.UnitTests.Enrollments;

public class BoardingFeeLineBuilderTests
{
    private static readonly Guid SchoolId = Guid.NewGuid();
    private static readonly Guid ClassroomId = Guid.NewGuid();
    private static readonly Guid PensionCategoryId = Guid.NewGuid();
    private static readonly Guid OtherCategoryId = Guid.NewGuid();

    private static FakeApplicationDbContext NewContext()
    {
        var db = new FakeApplicationDbContext();

        db.FeeCategories.Add(new FeeCategory
        {
            Id = PensionCategoryId, SchoolId = SchoolId, Name = "Pension",
            IsRecurring = true, IsBoardingFee = true
        });
        db.FeeCategories.Add(new FeeCategory
        {
            Id = OtherCategoryId, SchoolId = SchoolId, Name = "Mensualité",
            IsRecurring = true, IsBoardingFee = false
        });
        db.ClassFees.Add(new ClassFee
        {
            SchoolId = SchoolId, FeeCategoryId = PensionCategoryId, ClassroomId = ClassroomId, Amount = 20_000m
        });
        db.ClassFees.Add(new ClassFee
        {
            SchoolId = SchoolId, FeeCategoryId = OtherCategoryId, ClassroomId = ClassroomId, Amount = 15_000m
        });

        return db;
    }

    [Fact]
    public async Task Includes_Only_Categories_Flagged_IsBoardingFee()
    {
        var db = NewContext();

        var lines = await BoardingFeeLineBuilder.BuildMissingBoardingLinesAsync(
            db, SchoolId, ClassroomId, tuitionMonths: 9, existingFeeCategoryIds: new HashSet<Guid>(), CancellationToken.None);

        lines.Should().ContainSingle();
        lines[0].FeeCategoryId.Should().Be(PensionCategoryId);
        lines[0].Designation.Should().Be("Pension");
        lines[0].UnitAmount.Should().Be(20_000m);
        lines[0].Months.Should().Be(9);
        lines[0].LineTotal.Should().Be(180_000m);
    }

    [Fact]
    public async Task Excludes_Categories_Already_Present_On_The_Enrollment()
    {
        var db = NewContext();

        var lines = await BoardingFeeLineBuilder.BuildMissingBoardingLinesAsync(
            db, SchoolId, ClassroomId, tuitionMonths: 9,
            existingFeeCategoryIds: new HashSet<Guid> { PensionCategoryId }, CancellationToken.None);

        lines.Should().BeEmpty();
    }

    [Fact]
    public async Task Returns_Empty_When_No_ClassFee_Exists_For_The_Boarding_Category()
    {
        var db = NewContext();
        db.ClassFees.Clear();

        var lines = await BoardingFeeLineBuilder.BuildMissingBoardingLinesAsync(
            db, SchoolId, ClassroomId, tuitionMonths: 9, existingFeeCategoryIds: new HashSet<Guid>(), CancellationToken.None);

        lines.Should().BeEmpty();
    }
}
```

Si `tests/SamaEcole.UnitTests/Common/FakeApplicationDbContext.cs` (ou équivalent in-memory léger implémentant `IApplicationDbContext`) n'existe pas déjà, vérifier d'abord son nom exact :
Run: `grep -r "IApplicationDbContext" tests/SamaEcole.UnitTests --include=*.cs -l | head -5`
Adapter le nom de la classe fake utilisée ci-dessus à celle réellement présente dans `tests/SamaEcole.UnitTests/Common/` (le projet Students/Enrollments a déjà des tests unitaires de Handler qui en dépendent forcément) plutôt que d'en créer une seconde.

- [ ] **Step 2: Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test tests/SamaEcole.UnitTests --filter "FullyQualifiedName~BoardingFeeLineBuilderTests"`
Expected: FAIL — `BoardingFeeLineBuilder` n'existe pas (erreur de compilation).

- [ ] **Step 3: Implémenter `BoardingFeeLineBuilder`**

Créer `src/SamaEcole.Application/Enrollments/BoardingFeeLineBuilder.cs` :
```csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Enrollments;

/// <summary>
/// Construit les lignes de frais d'internat MANQUANTES d'une inscription (module Internat) — même
/// calcul que CreateEnrollmentCommandHandler.BuildFeeLinesAsync (montants COPIÉS depuis ClassFee,
/// mensualité × tuitionMonths), restreint aux catégories FeeCategory.IsBoardingFee et EXCLUANT
/// celles déjà présentes sur l'inscription (<paramref name="existingFeeCategoryIds"/>) : c'est ce
/// filtre qui garantit qu'un second changement de chambre (ChangeBoardingAssignmentCommand) ne
/// double-facture jamais la pension. Partagé par CreateEnrollmentCommandHandler (aucune ligne
/// existante — élève Externe/nouvel Interne) et ChangeBoardingAssignmentCommandHandler (l'inscription
/// existe déjà, ses lignes actuelles bornent ce qui reste à ajouter).
/// </summary>
public static class BoardingFeeLineBuilder
{
    public static async Task<List<EnrollmentFeeLine>> BuildMissingBoardingLinesAsync(
        IApplicationDbContext dbContext,
        Guid schoolId,
        Guid classroomId,
        int tuitionMonths,
        IReadOnlySet<Guid> existingFeeCategoryIds,
        CancellationToken cancellationToken)
    {
        var boardingFees = await (
            from fee in dbContext.ClassFees
            join category in dbContext.FeeCategories on fee.FeeCategoryId equals category.Id
            where fee.ClassroomId == classroomId && category.IsBoardingFee
            select new { fee.FeeCategoryId, category.Name, category.IsRecurring, fee.Amount })
            .ToListAsync(cancellationToken);

        return boardingFees
            .Where(f => !existingFeeCategoryIds.Contains(f.FeeCategoryId))
            .Select(f =>
            {
                var months = f.IsRecurring ? tuitionMonths : 1;
                return new EnrollmentFeeLine
                {
                    SchoolId = schoolId,
                    FeeCategoryId = f.FeeCategoryId,
                    Designation = f.Name,
                    IsRecurring = f.IsRecurring,
                    UnitAmount = f.Amount,
                    Months = months,
                    LineTotal = f.Amount * months
                };
            })
            .ToList();
    }
}
```

- [ ] **Step 4: Lancer le test pour vérifier qu'il passe**

Run: `dotnet test tests/SamaEcole.UnitTests --filter "FullyQualifiedName~BoardingFeeLineBuilderTests"`
Expected: PASS (3/3).

- [ ] **Step 5: Commit**

```bash
git add src/SamaEcole.Application/Enrollments/BoardingFeeLineBuilder.cs tests/SamaEcole.UnitTests/Enrollments/BoardingFeeLineBuilderTests.cs
git commit -m "feat(internat): BoardingFeeLineBuilder partagé (inclusion sans doublon des frais IsBoardingFee)"
```

---

### Task 4: Application — étendre `CreateEnrollmentCommand`

**Files:**
- Modify: `src/SamaEcole.Application/Enrollments/Commands/CreateEnrollment/CreateEnrollmentCommand.cs`
- Modify: `src/SamaEcole.Application/Enrollments/Commands/CreateEnrollment/CreateEnrollmentCommandValidator.cs`
- Modify: `src/SamaEcole.Application/Enrollments/Commands/CreateEnrollment/CreateEnrollmentCommandHandler.cs`
- Test: `tests/SamaEcole.IntegrationTests/Enrollments/EnrollmentBoardingTests.cs`

**Interfaces:**
- Consumes: `BoardingStatus` (Task 1), `BoardingFeeLineBuilder.BuildMissingBoardingLinesAsync` (Task 3), `Room` (existant), `SchoolSettings.IsInternatEnabled` (existant depuis commit `12ffbb2`).
- Produces: `CreateEnrollmentCommand.BoardingStatus` (défaut `Externe`), `.RoomId` (`Guid?`), `.IncludeBoardingFee` (`bool`, défaut `false`) — consommés par Task 17 (formulaire d'inscription).

- [ ] **Step 1: Écrire les tests d'intégration (échouent — les champs n'existent pas encore)**

Créer `tests/SamaEcole.IntegrationTests/Enrollments/EnrollmentBoardingTests.cs`, en suivant EXACTEMENT le fixture de `tests/SamaEcole.IntegrationTests/Enrollments/EnrollmentTests.cs` (`RlsTestDatabase`, `StubTenantProvider`, `_db.NewGenerator`, `NoOpKpiCacheService` — dupliquer cette petite classe `file sealed` locale comme le fait déjà `EnrollmentTests.cs`, elle est `file`-scoped donc invisible d'un autre fichier) :
```csharp
using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments.Commands.CreateEnrollment;
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Enrollments;

file sealed class NoOpKpiCacheService : IKpiCacheService
{
    public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken) =>
        factory(cancellationToken);

    public void Invalidate(string key) { }
}

[Trait("Category", "MultiTenant")]
public class EnrollmentBoardingTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("33333333-1111-1111-1111-111111111111");
    private static readonly Guid ClasseA = Guid.Parse("33333333-0000-0000-0000-00000000000a");
    private static readonly Guid AnneeA = Guid.Parse("33333333-0000-0000-0000-00000000000b");
    private static readonly Guid Dortoir = Guid.Parse("33333333-0000-0000-0000-00000000000c");
    private static readonly Guid CatPension = Guid.Parse("33333333-0000-0000-0000-00000000000d");
    private static readonly Guid Batiment = Guid.Parse("33333333-0000-0000-0000-00000000000e");

    private readonly int _year = AcademicYear.ForDate(DateTimeOffset.UtcNow);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = EcoleA, Name = "École A", Phone = "77 123 45 67" });
        owner.Classrooms.Add(new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = AnneeA, SchoolId = EcoleA, Label = $"{_year}-{_year + 1}",
            StartDate = new DateOnly(_year, 10, 1), EndDate = new DateOnly(_year + 1, 6, 30), IsActive = true
        });
        owner.Buildings.Add(new Building { Id = Batiment, SchoolId = EcoleA, Name = "Pavillon A" });
        owner.Rooms.Add(new Room { Id = Dortoir, SchoolId = EcoleA, BuildingId = Batiment, Name = "Chambre 1", Type = RoomType.Dortoir, Capacity = 1 });
        owner.FeeCategories.Add(new FeeCategory { Id = CatPension, SchoolId = EcoleA, Name = "Pension", IsRecurring = true, IsBoardingFee = true });
        owner.ClassFees.Add(new ClassFee { SchoolId = EcoleA, FeeCategoryId = CatPension, ClassroomId = ClasseA, Amount = 20_000m });
        owner.SchoolSettings.Add(new SchoolSettings { SchoolId = EcoleA, IsInternatEnabled = true });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private CreateEnrollmentCommandHandler NewHandler(ApplicationDbContext db) =>
        new(db, new StubTenantProvider(EcoleA), _db.NewGenerator(db), TimeProvider.System, new NoOpKpiCacheService());

    private static CreateEnrollmentCommand NewCommand(BoardingStatus status, Guid? roomId, bool includeFee) => new()
    {
        Type = EnrollmentType.NewEnrollment,
        ClassroomId = ClasseA,
        FullName = "Fatou Ndiaye",
        BirthDate = new DateOnly(2015, 3, 1),
        BirthPlace = "Dakar",
        Gender = "F",
        BoardingStatus = status,
        RoomId = roomId,
        IncludeBoardingFee = includeFee
    };

    [Fact]
    public async Task Interne_With_IncludeBoardingFee_Adds_The_Pension_Line()
    {
        await using var db = _db.NewAppContext();
        var receipt = await NewHandler(db).Handle(NewCommand(BoardingStatus.Interne, Dortoir, includeFee: true), CancellationToken.None);

        receipt.FeeLines.Should().Contain(l => l.Designation == "Pension" && l.LineTotal == 20_000m * 9);
    }

    [Fact]
    public async Task Interne_Without_IncludeBoardingFee_Does_Not_Add_The_Pension_Line()
    {
        await using var db = _db.NewAppContext();
        var receipt = await NewHandler(db).Handle(NewCommand(BoardingStatus.Interne, Dortoir, includeFee: false), CancellationToken.None);

        receipt.FeeLines.Should().NotContain(l => l.Designation == "Pension");
    }

    [Fact]
    public async Task Externe_Student_Never_Sees_The_Pension_Line_Even_If_Requested()
    {
        await using var db = _db.NewAppContext();
        var receipt = await NewHandler(db).Handle(NewCommand(BoardingStatus.Externe, roomId: null, includeFee: true), CancellationToken.None);

        receipt.FeeLines.Should().NotContain(l => l.Designation == "Pension");
    }

    [Fact]
    public async Task Room_At_Capacity_Is_Rejected_With_422()
    {
        await using var db1 = _db.NewAppContext();
        await NewHandler(db1).Handle(NewCommand(BoardingStatus.Interne, Dortoir, includeFee: false), CancellationToken.None);

        await using var db2 = _db.NewAppContext();
        var act = () => NewHandler(db2).Handle(NewCommand(BoardingStatus.Interne, Dortoir, includeFee: false), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .Where(e => e.Errors.ContainsKey("RoomId"));
    }

    [Fact]
    public async Task Boarding_Status_Rejected_With_422_When_Internat_Module_Disabled()
    {
        await using var setup = _db.NewOwnerContext();
        var settings = await setup.SchoolSettings.SingleAsync(s => s.SchoolId == EcoleA);
        settings.IsInternatEnabled = false;
        await setup.SaveChangesAsync(CancellationToken.None);

        await using var db = _db.NewAppContext();
        var act = () => NewHandler(db).Handle(NewCommand(BoardingStatus.Interne, Dortoir, includeFee: false), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .Where(e => e.Errors.ContainsKey("BoardingStatus"));
    }
}
```
Adapter `_db.NewAppContext()` / `_db.NewOwnerContext()` / `_db.NewGenerator` au nom EXACT des méthodes de `RlsTestDatabase` (vérifiées dans `tests/SamaEcole.IntegrationTests/Common/` lors de l'implémentation — `EnrollmentTests.cs` les utilise déjà, s'y référer directement plutôt que d'en deviner la signature).

- [ ] **Step 2: Lancer les tests pour vérifier qu'ils échouent**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~EnrollmentBoardingTests"`
Expected: FAIL — `CreateEnrollmentCommand` n'a pas encore de `BoardingStatus`/`RoomId`/`IncludeBoardingFee` (erreur de compilation).

- [ ] **Step 3: Étendre `CreateEnrollmentCommand`**

Dans `src/SamaEcole.Application/Enrollments/Commands/CreateEnrollment/CreateEnrollmentCommand.cs`, après `public string? GuardianPhone { get; init; }`, ajouter :
```csharp

    // --- Régime & Hébergement (module Internat) ---

    /// <summary>Défaut Externe : un élève qui ne loge pas dans l'établissement.</summary>
    public BoardingStatus BoardingStatus { get; init; } = BoardingStatus.Externe;

    /// <summary>Chambre affectée — requis si <see cref="BoardingStatus"/> ≠ Externe (voir le Validator).</summary>
    public Guid? RoomId { get; init; }

    /// <summary>
    /// Si vrai ET qu'une catégorie FeeCategory.IsBoardingFee a un ClassFee sur la classe choisie,
    /// ajoute la ligne de pension au compte financier de l'inscription (voir BoardingFeeLineBuilder).
    /// Sans effet pour un élève Externe.
    /// </summary>
    public bool IncludeBoardingFee { get; init; }
```
Ajouter `using SamaEcole.Domain.Enums;` en tête (déjà présent pour `EnrollmentType`).

- [ ] **Step 4: Étendre `CreateEnrollmentCommandValidator`**

Dans `src/SamaEcole.Application/Enrollments/Commands/CreateEnrollment/CreateEnrollmentCommandValidator.cs`, après `RuleFor(x => x.ClassroomId).NotEmpty();`, ajouter :
```csharp
        RuleFor(x => x.BoardingStatus).IsInEnum();

        // Un régime Interne/Demi-pensionnaire sans chambre est une saisie incomplète — RoomId reste
        // libre pour Externe (spec §3.3 : significatif seulement si BoardingStatus != Externe).
        RuleFor(x => x.RoomId)
            .NotNull()
            .When(x => x.BoardingStatus != BoardingStatus.Externe)
            .WithMessage("Une chambre est requise pour un régime Interne ou Demi-pensionnaire.");
```
Ajouter `using SamaEcole.Domain.Enums;` en tête.

- [ ] **Step 5: Étendre `CreateEnrollmentCommandHandler`**

Dans `src/SamaEcole.Application/Enrollments/Commands/CreateEnrollment/CreateEnrollmentCommandHandler.cs` :

1. Ajouter `using SamaEcole.Application.Enrollments;` (pour `BoardingFeeLineBuilder`, même namespace racine — vérifier si un `using` explicite est nécessaire selon le namespace exact du fichier).

2. Juste après le chargement de `classroom` (avant `var receipt = await dbContext.ExecuteInTransactionAsync(...)`), ajouter la garde de module :
```csharp
        // Garde serveur (AGENTS.md règle sur les modules) : un client qui poste un régime non-Externe
        // alors que le Directeur n'a pas activé l'Internat est rejeté en 422 — jamais accepté puis
        // silencieusement ignoré (même philosophie que la garde déjà en place pour SchoolModule.Pedagogy).
        if (request.BoardingStatus != BoardingStatus.Externe)
        {
            var settings = await dbContext.SchoolSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.SchoolId == schoolId, cancellationToken);
            var internatEnabled = settings?.IsInternatEnabled ?? SchoolSettingsDefaults.IsInternatEnabled;

            if (!internatEnabled)
            {
                throw new ValidationException([
                    new ValidationFailure(
                        nameof(request.BoardingStatus),
                        "Le module Internat n'est pas activé pour votre établissement.")
                ]);
            }
        }
```

3. À l'intérieur de la transaction, juste après le bloc `alreadyEnrolled` (avant `var tuitionMonths = await ResolveTuitionMonthsAsync(ct);`), ajouter la vérification de capacité :
```csharp
            // Capacité revérifiée DANS la transaction (spec §5.1) : deux inscriptions concurrentes sur
            // le dernier lit d'une chambre ne doivent jamais toutes les deux réussir.
            if (request.RoomId is { } roomId)
            {
                var room = await dbContext.Rooms.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Id == roomId, ct)
                    ?? throw new ValidationException([
                        new ValidationFailure(nameof(request.RoomId), "La chambre indiquée n'existe pas dans votre établissement.")
                    ]);

                var occupied = await dbContext.Enrollments.CountAsync(
                    e => e.RoomId == roomId && e.SchoolYearId == activeYear.Id && e.Status != EnrollmentStatus.Cancelled, ct);

                if (occupied >= room.Capacity)
                {
                    throw new ValidationException([
                        new ValidationFailure(nameof(request.RoomId), "Cette chambre a atteint sa capacité maximale.")
                    ]);
                }
            }
```

4. Remplacer :
```csharp
            var tuitionMonths = await ResolveTuitionMonthsAsync(ct);
            var lines = await BuildFeeLinesAsync(schoolId, request.ClassroomId, tuitionMonths, ct);
            var totalDue = lines.Sum(l => l.LineTotal);
```
par :
```csharp
            var tuitionMonths = await ResolveTuitionMonthsAsync(ct);
            var lines = await BuildFeeLinesAsync(schoolId, request.ClassroomId, tuitionMonths, ct);

            // Pension (module Internat) : catégories IsBoardingFee, seulement pour Interne/Demi-
            // pensionnaire et seulement si IncludeBoardingFee — les élèves Externe de la même classe
            // ne voient jamais ces lignes (spec §5.1).
            if (request.BoardingStatus != BoardingStatus.Externe && request.IncludeBoardingFee)
            {
                var boardingLines = await BoardingFeeLineBuilder.BuildMissingBoardingLinesAsync(
                    dbContext, schoolId, request.ClassroomId, tuitionMonths, existingFeeCategoryIds: new HashSet<Guid>(), ct);
                lines.AddRange(boardingLines);
            }

            var totalDue = lines.Sum(l => l.LineTotal);
```

5. Dans la construction de `new Enrollment { ... }`, après `IsRepeating = request.IsRepeating,`, ajouter :
```csharp
                BoardingStatus = request.BoardingStatus,
                RoomId = request.RoomId,
```

- [ ] **Step 6: Lancer les tests pour vérifier qu'ils passent**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~EnrollmentBoardingTests"`
Expected: PASS (5/5). Nécessite PostgreSQL disponible pour Testcontainers (Docker en cours d'exécution).

- [ ] **Step 7: Vérifier la non-régression des tests existants**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~Enrollments"`
Expected: PASS — `EnrollmentTests` (existant) toujours vert, `EnrollmentBoardingTests` (nouveau) vert.

- [ ] **Step 8: Commit**

```bash
git add src/SamaEcole.Application/Enrollments/Commands/CreateEnrollment/ tests/SamaEcole.IntegrationTests/Enrollments/EnrollmentBoardingTests.cs
git commit -m "feat(internat): régime, chambre et frais de pension sur CreateEnrollmentCommand"
```

---

### Task 5: Application — `GetInternatDashboardQuery`

**Files:**
- Create: `src/SamaEcole.Application/Internat/Queries/GetInternatDashboard/GetInternatDashboardQuery.cs`
- Test: `tests/SamaEcole.IntegrationTests/Internat/GetInternatDashboardQueryTests.cs`

**Interfaces:**
- Consumes: `Room`, `Building`, `Enrollment.BoardingStatus`/`RoomId`, `Student`, `Classroom`, `SchoolYear.IsActive` (existants + Task 1).
- Produces: `GetInternatDashboardQuery : IRequest<InternatDashboardDto>`, `InternatDashboardDto`, `DormitoryRoomDto`, `BoardingOccupantDto` — consommés par Task 8 (`InternatController`) et Task 15 (écran).

- [ ] **Step 1: Écrire le test d'intégration (échoue)**

Créer `tests/SamaEcole.IntegrationTests/Internat/GetInternatDashboardQueryTests.cs`, fixture identique à Task 4 (un `Building`/`Room` de type `Dortoir`, deux élèves inscrits dont un `Interne` affecté) :
```csharp
using FluentAssertions;
using SamaEcole.Application.Internat.Queries.GetInternatDashboard;
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Internat;

[Trait("Category", "MultiTenant")]
public class GetInternatDashboardQueryTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("44444444-1111-1111-1111-111111111111");
    private static readonly Guid Batiment = Guid.Parse("44444444-0000-0000-0000-00000000000a");
    private static readonly Guid ChambrePleine = Guid.Parse("44444444-0000-0000-0000-00000000000b");
    private static readonly Guid ChambreLibre = Guid.Parse("44444444-0000-0000-0000-00000000000c");
    private static readonly Guid ClasseA = Guid.Parse("44444444-0000-0000-0000-00000000000d");
    private static readonly Guid AnneeA = Guid.Parse("44444444-0000-0000-0000-00000000000e");
    private static readonly Guid EleveInterne = Guid.Parse("44444444-0000-0000-0000-00000000000f");

    private readonly int _year = AcademicYear.ForDate(DateTimeOffset.UtcNow);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = EcoleA, Name = "École A", Phone = "77 123 45 67" });
        owner.Classrooms.Add(new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = AnneeA, SchoolId = EcoleA, Label = $"{_year}-{_year + 1}",
            StartDate = new DateOnly(_year, 10, 1), EndDate = new DateOnly(_year + 1, 6, 30), IsActive = true
        });
        owner.Buildings.Add(new Building { Id = Batiment, SchoolId = EcoleA, Name = "Pavillon A" });
        owner.Rooms.Add(new Room { Id = ChambrePleine, SchoolId = EcoleA, BuildingId = Batiment, Name = "Chambre 1", Type = RoomType.Dortoir, Capacity = 1 });
        owner.Rooms.Add(new Room { Id = ChambreLibre, SchoolId = EcoleA, BuildingId = Batiment, Name = "Chambre 2", Type = RoomType.Dortoir, Capacity = 2 });
        owner.Students.Add(new Student
        {
            Id = EleveInterne, SchoolId = EcoleA, Matricule = "ELEV-0001", FullName = "Awa Fall",
            BirthDate = new DateOnly(2014, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA,
            GuardianPhone = "771234567"
        });
        owner.Enrollments.Add(new Enrollment
        {
            SchoolId = EcoleA, StudentId = EleveInterne, SchoolYearId = AnneeA, ClassroomId = ClasseA,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            BoardingStatus = BoardingStatus.Interne, RoomId = ChambrePleine,
            TotalDue = 0, ReceiptNumber = "REC-TEST-0001", EnrolledAt = DateTimeOffset.UtcNow
        });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Reports_Occupancy_Per_Room_And_Global_Kpis()
    {
        await using var db = _db.NewAppContext();
        var handler = new GetInternatDashboardQueryHandler(db);

        var result = await handler.Handle(new GetInternatDashboardQuery(), CancellationToken.None);

        result.TotalCapacity.Should().Be(3);
        result.TotalOccupied.Should().Be(1);
        result.InterneCount.Should().Be(1);
        result.DemiPensionnaireCount.Should().Be(0);
        result.FullRoomsCount.Should().Be(1);

        var full = result.Rooms.Single(r => r.RoomId == ChambrePleine);
        full.OccupantsCount.Should().Be(1);
        full.Capacity.Should().Be(1);
        full.Occupants.Should().ContainSingle(o => o.StudentId == EleveInterne && o.GuardianPhone == "771234567");

        var free = result.Rooms.Single(r => r.RoomId == ChambreLibre);
        free.OccupantsCount.Should().Be(0);
        free.Occupants.Should().BeEmpty();
    }
}
```
Adapter la construction de `GetInternatDashboardQueryHandler` au constructeur réel défini au Step 2 (`IApplicationDbContext` seul, pas de tenant provider explicite : le Global Query Filter + RLS bornent déjà la lecture au tenant courant, comme `GetStudentDetailQueryHandler`).

- [ ] **Step 2: Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~GetInternatDashboardQueryTests"`
Expected: FAIL — le type n'existe pas.

- [ ] **Step 3: Implémenter la Query**

Créer `src/SamaEcole.Application/Internat/Queries/GetInternatDashboard/GetInternatDashboardQuery.cs` :
```csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Internat.Queries.GetInternatDashboard;

/// <summary>
/// GET /api/v1/internat/dashboard — tableau de bord de l'Internat (module Internat, spec §5.3) :
/// KPIs globaux et occupation par chambre pour l'année scolaire ACTIVE. LECTURE seule, bornée au
/// tenant courant par le Global Query Filter + la RLS (aucun SchoolId accepté du client).
/// </summary>
public record GetInternatDashboardQuery : IRequest<InternatDashboardDto>;

public record InternatDashboardDto(
    int TotalCapacity,
    int TotalOccupied,
    int InterneCount,
    int DemiPensionnaireCount,
    int FullRoomsCount,
    int RoomsWithFreeSpaceCount,
    IReadOnlyList<DormitoryRoomDto> Rooms);

public record DormitoryRoomDto(
    Guid RoomId,
    string RoomName,
    string BuildingName,
    int Capacity,
    int OccupantsCount,
    IReadOnlyList<BoardingOccupantDto> Occupants);

public record BoardingOccupantDto(
    Guid StudentId,
    Guid EnrollmentId,
    string FullName,
    string ClassroomName,
    string? GuardianPhone,
    string BoardingStatus);

public class GetInternatDashboardQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetInternatDashboardQuery, InternatDashboardDto>
{
    public async Task<InternatDashboardDto> Handle(GetInternatDashboardQuery request, CancellationToken cancellationToken)
    {
        var activeYearId = await dbContext.SchoolYears.AsNoTracking()
            .Where(y => y.IsActive)
            .Select(y => (Guid?)y.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var dormitories = await dbContext.Rooms.AsNoTracking()
            .Where(r => r.Type == RoomType.Dortoir)
            .Select(r => new
            {
                r.Id,
                r.Name,
                r.Capacity,
                BuildingName = dbContext.Buildings.AsNoTracking()
                    .Where(b => b.Id == r.BuildingId)
                    .Select(b => b.Name)
                    .FirstOrDefault() ?? "Bâtiment supprimé"
            })
            .ToListAsync(cancellationToken);

        // Aucune année active : aucun élève ne peut être affecté (CreateEnrollmentCommandHandler
        // exige déjà une année active), donc toutes les chambres apparaissent vides plutôt que
        // de lever une erreur — l'écran doit rester consultable pour créer des dortoirs en amont.
        var occupants = activeYearId is null
            ? []
            : await (
                from e in dbContext.Enrollments.AsNoTracking()
                join s in dbContext.Students.AsNoTracking() on e.StudentId equals s.Id
                join c in dbContext.Classrooms.AsNoTracking() on e.ClassroomId equals c.Id
                where e.SchoolYearId == activeYearId && e.Status != Domain.Enums.EnrollmentStatus.Cancelled
                      && e.RoomId != null
                select new
                {
                    RoomId = e.RoomId!.Value,
                    StudentId = s.Id,
                    EnrollmentId = e.Id,
                    s.FullName,
                    ClassroomName = c.Name,
                    s.GuardianPhone,
                    BoardingStatus = e.BoardingStatus.ToString()
                })
                .ToListAsync(cancellationToken);

        var occupantsByRoom = occupants.ToLookup(o => o.RoomId);

        var rooms = dormitories
            .Select(r =>
            {
                var roomOccupants = occupantsByRoom[r.Id]
                    .Select(o => new BoardingOccupantDto(
                        o.StudentId, o.EnrollmentId, o.FullName, o.ClassroomName, o.GuardianPhone, o.BoardingStatus))
                    .OrderBy(o => o.FullName)
                    .ToList();

                return new DormitoryRoomDto(r.Id, r.Name, r.BuildingName, r.Capacity, roomOccupants.Count, roomOccupants);
            })
            .OrderBy(r => r.BuildingName).ThenBy(r => r.RoomName)
            .ToList();

        return new InternatDashboardDto(
            TotalCapacity: rooms.Sum(r => r.Capacity),
            TotalOccupied: rooms.Sum(r => r.OccupantsCount),
            InterneCount: occupants.Count(o => o.BoardingStatus == nameof(BoardingStatus.Interne)),
            DemiPensionnaireCount: occupants.Count(o => o.BoardingStatus == nameof(BoardingStatus.DemiPensionnaire)),
            FullRoomsCount: rooms.Count(r => r.OccupantsCount >= r.Capacity),
            RoomsWithFreeSpaceCount: rooms.Count(r => r.OccupantsCount < r.Capacity),
            Rooms: rooms);
    }
}
```

- [ ] **Step 4: Lancer le test pour vérifier qu'il passe**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~GetInternatDashboardQueryTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SamaEcole.Application/Internat/Queries/GetInternatDashboard/ tests/SamaEcole.IntegrationTests/Internat/GetInternatDashboardQueryTests.cs
git commit -m "feat(internat): GetInternatDashboardQuery (KPIs + occupation par chambre)"
```

---

### Task 6: Application — `SearchBoardableStudentsQuery`

**Files:**
- Create: `src/SamaEcole.Application/Internat/Queries/SearchBoardableStudents/SearchBoardableStudentsQuery.cs`
- Test: `tests/SamaEcole.IntegrationTests/Internat/SearchBoardableStudentsQueryTests.cs`

**Interfaces:**
- Consumes: `Student`, `Enrollment` (existants + Task 1), `SchoolYear.IsActive`.
- Produces: `SearchBoardableStudentsQuery(string SearchTerm) : IRequest<IReadOnlyList<BoardableStudentDto>>`, `BoardableStudentDto` — consommé par Task 8 (`InternatController`) et Task 16 (modale d'affectation).

- [ ] **Step 1: Écrire le test d'intégration (échoue)**

Créer `tests/SamaEcole.IntegrationTests/Internat/SearchBoardableStudentsQueryTests.cs`, fixture avec deux élèves inscrits sur l'année active (un `Externe`, un `Interne` déjà en chambre) :
```csharp
using FluentAssertions;
using SamaEcole.Application.Internat.Queries.SearchBoardableStudents;
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Internat;

[Trait("Category", "MultiTenant")]
public class SearchBoardableStudentsQueryTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("55555555-1111-1111-1111-111111111111");
    private static readonly Guid ClasseA = Guid.Parse("55555555-0000-0000-0000-00000000000a");
    private static readonly Guid AnneeA = Guid.Parse("55555555-0000-0000-0000-00000000000b");
    private static readonly Guid Chambre = Guid.Parse("55555555-0000-0000-0000-00000000000c");
    private static readonly Guid Batiment = Guid.Parse("55555555-0000-0000-0000-00000000000d");
    private static readonly Guid EleveExterne = Guid.Parse("55555555-0000-0000-0000-00000000000e");
    private static readonly Guid EleveInterne = Guid.Parse("55555555-0000-0000-0000-00000000000f");

    private readonly int _year = AcademicYear.ForDate(DateTimeOffset.UtcNow);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = EcoleA, Name = "École A", Phone = "77 123 45 67" });
        owner.Classrooms.Add(new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = AnneeA, SchoolId = EcoleA, Label = $"{_year}-{_year + 1}",
            StartDate = new DateOnly(_year, 10, 1), EndDate = new DateOnly(_year + 1, 6, 30), IsActive = true
        });
        owner.Buildings.Add(new Building { Id = Batiment, SchoolId = EcoleA, Name = "Pavillon A" });
        owner.Rooms.Add(new Room { Id = Chambre, SchoolId = EcoleA, BuildingId = Batiment, Name = "Chambre 1", Type = RoomType.Dortoir, Capacity = 2 });

        owner.Students.Add(new Student { Id = EleveExterne, SchoolId = EcoleA, Matricule = "ELEV-0001", FullName = "Moussa Diop", BirthDate = new DateOnly(2014, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = ClasseA });
        owner.Students.Add(new Student { Id = EleveInterne, SchoolId = EcoleA, Matricule = "ELEV-0002", FullName = "Awa Fall", BirthDate = new DateOnly(2014, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA });

        owner.Enrollments.Add(new Enrollment { SchoolId = EcoleA, StudentId = EleveExterne, SchoolYearId = AnneeA, ClassroomId = ClasseA, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, BoardingStatus = BoardingStatus.Externe, TotalDue = 0, ReceiptNumber = "REC-0001", EnrolledAt = DateTimeOffset.UtcNow });
        owner.Enrollments.Add(new Enrollment { SchoolId = EcoleA, StudentId = EleveInterne, SchoolYearId = AnneeA, ClassroomId = ClasseA, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, BoardingStatus = BoardingStatus.Interne, RoomId = Chambre, TotalDue = 0, ReceiptNumber = "REC-0002", EnrolledAt = DateTimeOffset.UtcNow });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Finds_By_Partial_Name_And_Reports_Current_Boarding_State()
    {
        await using var db = _db.NewAppContext();
        var handler = new SearchBoardableStudentsQueryHandler(db);

        var result = await handler.Handle(new SearchBoardableStudentsQuery("fall"), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].FullName.Should().Be("Awa Fall");
        result[0].BoardingStatus.Should().Be("Interne");
        result[0].CurrentRoomName.Should().Be("Chambre 1");
    }

    [Fact]
    public async Task Finds_By_Matricule()
    {
        await using var db = _db.NewAppContext();
        var handler = new SearchBoardableStudentsQueryHandler(db);

        var result = await handler.Handle(new SearchBoardableStudentsQuery("ELEV-0001"), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].FullName.Should().Be("Moussa Diop");
        result[0].BoardingStatus.Should().Be("Externe");
        result[0].CurrentRoomName.Should().BeNull();
    }
}
```

- [ ] **Step 2: Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~SearchBoardableStudentsQueryTests"`
Expected: FAIL.

- [ ] **Step 3: Implémenter la Query**

Créer `src/SamaEcole.Application/Internat/Queries/SearchBoardableStudents/SearchBoardableStudentsQuery.cs` :
```csharp
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Internat.Queries.SearchBoardableStudents;

/// <summary>
/// GET /api/v1/internat/students/search?term= — autocomplétion élève pour la modale d'affectation
/// (spec §5.4). Restreint aux inscriptions ACTIVES de l'année en cours : un élève sans inscription
/// active n'a rien à affecter. Retourne le régime et la chambre ACTUELS pour le badge de statut et
/// l'avertissement de transfert côté UI.
/// </summary>
public record SearchBoardableStudentsQuery(string SearchTerm) : IRequest<IReadOnlyList<BoardableStudentDto>>;

public record BoardableStudentDto(
    Guid StudentId,
    Guid EnrollmentId,
    string Matricule,
    string FullName,
    string ClassroomName,
    string BoardingStatus,
    Guid? CurrentRoomId,
    string? CurrentRoomName);

public class SearchBoardableStudentsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<SearchBoardableStudentsQuery, IReadOnlyList<BoardableStudentDto>>
{
    public async Task<IReadOnlyList<BoardableStudentDto>> Handle(
        SearchBoardableStudentsQuery request, CancellationToken cancellationToken)
    {
        var term = request.SearchTerm.Trim();
        if (term.Length == 0)
        {
            return [];
        }

        var pattern = $"%{term}%";

        var results = await (
            from e in dbContext.Enrollments.AsNoTracking()
            join s in dbContext.Students.AsNoTracking() on e.StudentId equals s.Id
            join c in dbContext.Classrooms.AsNoTracking() on e.ClassroomId equals c.Id
            join y in dbContext.SchoolYears.AsNoTracking() on e.SchoolYearId equals y.Id
            where y.IsActive
                  && e.Status != Domain.Enums.EnrollmentStatus.Cancelled
                  && (EF.Functions.ILike(s.FullName, pattern) || EF.Functions.ILike(s.Matricule, pattern))
            select new
            {
                s.Id,
                EnrollmentId = e.Id,
                s.Matricule,
                s.FullName,
                ClassroomName = c.Name,
                e.BoardingStatus,
                e.RoomId
            })
            .OrderBy(r => r.FullName)
            .Take(20)
            .ToListAsync(cancellationToken);

        var roomIds = results.Where(r => r.RoomId is not null).Select(r => r.RoomId!.Value).Distinct().ToList();
        var roomNames = roomIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.Rooms.AsNoTracking()
                .Where(r => roomIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);

        return results
            .Select(r => new BoardableStudentDto(
                r.Id, r.EnrollmentId, r.Matricule, r.FullName, r.ClassroomName, r.BoardingStatus.ToString(),
                r.RoomId, r.RoomId is { } id ? roomNames.GetValueOrDefault(id) : null))
            .ToList();
    }
}
```

- [ ] **Step 4: Lancer le test pour vérifier qu'il passe**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~SearchBoardableStudentsQueryTests"`
Expected: PASS (2/2).

- [ ] **Step 5: Commit**

```bash
git add src/SamaEcole.Application/Internat/Queries/SearchBoardableStudents/ tests/SamaEcole.IntegrationTests/Internat/SearchBoardableStudentsQueryTests.cs
git commit -m "feat(internat): SearchBoardableStudentsQuery (autocomplétion élève)"
```

---

### Task 7: Application — `ChangeBoardingAssignmentCommand`

**Files:**
- Create: `src/SamaEcole.Application/Internat/Commands/ChangeBoardingAssignment/ChangeBoardingAssignmentCommand.cs`
- Test: `tests/SamaEcole.IntegrationTests/Internat/ChangeBoardingAssignmentTests.cs`

**Interfaces:**
- Consumes: `BoardingFeeLineBuilder` (Task 3), `IApplicationDbContext.SetOriginalConcurrencyToken` (existant), `IAuditableRequest` (existant).
- Produces: `ChangeBoardingAssignmentCommand(Guid EnrollmentId, Guid? RoomId, BoardingStatus BoardingStatus, bool IncludeBoardingFee, uint RowVersion) : IRequest<EnrollmentBoardingDto>, IAuditableRequest`, `EnrollmentBoardingDto` — consommé par Task 8 (`InternatController`) et Task 16 (modale d'affectation).

- [ ] **Step 1: Écrire les tests d'intégration (échouent)**

Créer `tests/SamaEcole.IntegrationTests/Internat/ChangeBoardingAssignmentTests.cs` :
```csharp
using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Internat.Commands.ChangeBoardingAssignment;
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Internat;

[Trait("Category", "MultiTenant")]
public class ChangeBoardingAssignmentTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("66666666-1111-1111-1111-111111111111");
    private static readonly Guid ClasseA = Guid.Parse("66666666-0000-0000-0000-00000000000a");
    private static readonly Guid AnneeA = Guid.Parse("66666666-0000-0000-0000-00000000000b");
    private static readonly Guid Batiment = Guid.Parse("66666666-0000-0000-0000-00000000000c");
    private static readonly Guid ChambreA = Guid.Parse("66666666-0000-0000-0000-00000000000d");
    private static readonly Guid ChambreB = Guid.Parse("66666666-0000-0000-0000-00000000000e");
    private static readonly Guid CatPension = Guid.Parse("66666666-0000-0000-0000-00000000000f");
    private static readonly Guid Eleve = Guid.Parse("66666666-0000-0000-0000-000000000010");
    private static readonly Guid Inscription = Guid.Parse("66666666-0000-0000-0000-000000000011");

    private readonly int _year = AcademicYear.ForDate(DateTimeOffset.UtcNow);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = EcoleA, Name = "École A", Phone = "77 123 45 67" });
        owner.Classrooms.Add(new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.SchoolYears.Add(new SchoolYear { Id = AnneeA, SchoolId = EcoleA, Label = $"{_year}-{_year + 1}", StartDate = new DateOnly(_year, 10, 1), EndDate = new DateOnly(_year + 1, 6, 30), IsActive = true });
        owner.Buildings.Add(new Building { Id = Batiment, SchoolId = EcoleA, Name = "Pavillon A" });
        owner.Rooms.Add(new Room { Id = ChambreA, SchoolId = EcoleA, BuildingId = Batiment, Name = "Chambre A", Type = RoomType.Dortoir, Capacity = 1 });
        owner.Rooms.Add(new Room { Id = ChambreB, SchoolId = EcoleA, BuildingId = Batiment, Name = "Chambre B", Type = RoomType.Dortoir, Capacity = 1 });
        owner.FeeCategories.Add(new FeeCategory { Id = CatPension, SchoolId = EcoleA, Name = "Pension", IsRecurring = true, IsBoardingFee = true });
        owner.ClassFees.Add(new ClassFee { SchoolId = EcoleA, FeeCategoryId = CatPension, ClassroomId = ClasseA, Amount = 20_000m });
        owner.Students.Add(new Student { Id = Eleve, SchoolId = EcoleA, Matricule = "ELEV-0001", FullName = "Awa Fall", BirthDate = new DateOnly(2014, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA });
        owner.Enrollments.Add(new Enrollment { Id = Inscription, SchoolId = EcoleA, StudentId = Eleve, SchoolYearId = AnneeA, ClassroomId = ClasseA, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, BoardingStatus = BoardingStatus.Externe, TotalDue = 15_000m, ReceiptNumber = "REC-0001", EnrolledAt = DateTimeOffset.UtcNow });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static async Task<uint> CurrentRowVersionAsync(ApplicationDbContext db) =>
        await db.Enrollments.Where(e => e.Id == Inscription).Select(e => EF.Property<uint>(e, "xmin")).SingleAsync();

    [Fact]
    public async Task Assigning_A_Room_Adds_The_Pension_Line_Once()
    {
        await using var db1 = _db.NewAppContext();
        var rowVersion = await CurrentRowVersionAsync(db1);
        var handler1 = new ChangeBoardingAssignmentCommandHandler(db1);

        var result1 = await handler1.Handle(
            new ChangeBoardingAssignmentCommand(Inscription, ChambreA, BoardingStatus.Interne, IncludeBoardingFee: true, rowVersion),
            CancellationToken.None);

        result1.TotalDue.Should().Be(15_000m + 20_000m * 9);

        // Second changement de chambre (toujours Interne) : la pension ne doit PAS être doublée.
        await using var db2 = _db.NewAppContext();
        var rowVersion2 = await CurrentRowVersionAsync(db2);
        var handler2 = new ChangeBoardingAssignmentCommandHandler(db2);

        var result2 = await handler2.Handle(
            new ChangeBoardingAssignmentCommand(Inscription, ChambreB, BoardingStatus.Interne, IncludeBoardingFee: true, rowVersion2),
            CancellationToken.None);

        result2.TotalDue.Should().Be(15_000m + 20_000m * 9); // inchangé, pas de doublon
        result2.RoomId.Should().Be(ChambreB);
    }

    [Fact]
    public async Task Releasing_A_Student_Keeps_The_Pension_Line_Due()
    {
        await using var db1 = _db.NewAppContext();
        var rowVersion = await CurrentRowVersionAsync(db1);
        await new ChangeBoardingAssignmentCommandHandler(db1).Handle(
            new ChangeBoardingAssignmentCommand(Inscription, ChambreA, BoardingStatus.Interne, IncludeBoardingFee: true, rowVersion),
            CancellationToken.None);

        await using var db2 = _db.NewAppContext();
        var rowVersion2 = await CurrentRowVersionAsync(db2);
        var released = await new ChangeBoardingAssignmentCommandHandler(db2).Handle(
            new ChangeBoardingAssignmentCommand(Inscription, RoomId: null, BoardingStatus.Externe, IncludeBoardingFee: false, rowVersion2),
            CancellationToken.None);

        released.RoomId.Should().BeNull();
        released.BoardingStatus.Should().Be(nameof(BoardingStatus.Externe));
        released.TotalDue.Should().Be(15_000m + 20_000m * 9); // la pension déjà facturée reste due (spec §2, décision #7)
    }

    [Fact]
    public async Task Room_At_Capacity_Is_Rejected_With_422()
    {
        // Occupe ChambreA avec un premier élève.
        await using var setup = _db.NewOwnerContext();
        var autreEleve = Guid.NewGuid();
        var autreInscription = Guid.NewGuid();
        setup.Students.Add(new Student { Id = autreEleve, SchoolId = EcoleA, Matricule = "ELEV-0002", FullName = "Moussa Diop", BirthDate = new DateOnly(2014, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = ClasseA });
        setup.Enrollments.Add(new Enrollment { Id = autreInscription, SchoolId = EcoleA, StudentId = autreEleve, SchoolYearId = AnneeA, ClassroomId = ClasseA, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, BoardingStatus = BoardingStatus.Interne, RoomId = ChambreA, TotalDue = 0, ReceiptNumber = "REC-0002", EnrolledAt = DateTimeOffset.UtcNow });
        await setup.SaveChangesAsync(CancellationToken.None);

        await using var db = _db.NewAppContext();
        var rowVersion = await CurrentRowVersionAsync(db);
        var act = () => new ChangeBoardingAssignmentCommandHandler(db).Handle(
            new ChangeBoardingAssignmentCommand(Inscription, ChambreA, BoardingStatus.Interne, IncludeBoardingFee: false, rowVersion),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>().Where(e => e.Errors.ContainsKey("RoomId"));
    }

    [Fact]
    public async Task Stale_RowVersion_Is_Rejected_With_Concurrency_Conflict()
    {
        await using var db1 = _db.NewAppContext();
        var staleRowVersion = await CurrentRowVersionAsync(db1);

        // Un autre changement passe en premier, avance le xmin.
        await using var db2 = _db.NewAppContext();
        var currentRowVersion = await CurrentRowVersionAsync(db2);
        await new ChangeBoardingAssignmentCommandHandler(db2).Handle(
            new ChangeBoardingAssignmentCommand(Inscription, ChambreA, BoardingStatus.Interne, IncludeBoardingFee: false, currentRowVersion),
            CancellationToken.None);

        // Le premier appelant, avec le jeton PÉRIMÉ, doit être rejeté en conflit — jamais un écrasement silencieux.
        await using var db3 = _db.NewAppContext();
        var act = () => new ChangeBoardingAssignmentCommandHandler(db3).Handle(
            new ChangeBoardingAssignmentCommand(Inscription, ChambreB, BoardingStatus.Interne, IncludeBoardingFee: false, staleRowVersion),
            CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }
}
```

- [ ] **Step 2: Lancer les tests pour vérifier qu'ils échouent**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~ChangeBoardingAssignmentTests"`
Expected: FAIL — le type n'existe pas.

- [ ] **Step 3: Implémenter la Command**

Créer `src/SamaEcole.Application/Internat/Commands/ChangeBoardingAssignment/ChangeBoardingAssignmentCommand.cs` :
```csharp
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Internat.Commands.ChangeBoardingAssignment;

/// <summary>
/// POST /api/v1/internat/assignments — affecte, réaffecte ou libère un élève d'une chambre (spec
/// §5.2). RoomId null = libération. Réservé Directeur/Secretariat/Surveillant (InternatController).
///
/// <see cref="RowVersion"/> : verrou optimiste xmin d'Enrollment (AGENTS.md règle #5) — un dossier
/// modifié entre-temps (double clic, un autre utilisateur) est rejeté en 409, jamais écrasé.
/// </summary>
public record ChangeBoardingAssignmentCommand(
    Guid EnrollmentId, Guid? RoomId, BoardingStatus BoardingStatus, bool IncludeBoardingFee, uint RowVersion)
    : IRequest<EnrollmentBoardingDto>, IAuditableRequest;

public record EnrollmentBoardingDto(
    Guid EnrollmentId, BoardingStatus BoardingStatus, Guid? RoomId, decimal TotalDue, uint RowVersion);

public class ChangeBoardingAssignmentCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<ChangeBoardingAssignmentCommand, EnrollmentBoardingDto>
{
    public async Task<EnrollmentBoardingDto> Handle(
        ChangeBoardingAssignmentCommand request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la RLS bornent déjà la recherche à l'école courante : une
        // inscription d'une autre école renvoie 404, jamais un changement silencieux.
        var enrollment = await dbContext.Enrollments
            .FirstOrDefaultAsync(e => e.Id == request.EnrollmentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Inscription {request.EnrollmentId} introuvable.");

        if (request.RoomId is { } roomId)
        {
            var room = await dbContext.Rooms.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == roomId, cancellationToken)
                ?? throw new ValidationException([
                    new ValidationFailure(nameof(request.RoomId), "La chambre indiquée n'existe pas dans votre établissement.")
                ]);

            // Recompte l'occupation EN EXCLUANT l'inscription courante : confirmer une chambre déjà
            // occupée par CE MÊME élève (aucun changement réel) ne doit jamais être refusé pour cause
            // de capacité (spec §5.2).
            var occupied = await dbContext.Enrollments.CountAsync(
                e => e.RoomId == roomId && e.Id != request.EnrollmentId
                     && e.SchoolYearId == enrollment.SchoolYearId && e.Status != EnrollmentStatus.Cancelled,
                cancellationToken);

            if (occupied >= room.Capacity)
            {
                throw new ValidationException([
                    new ValidationFailure(nameof(request.RoomId), "Cette chambre a atteint sa capacité maximale.")
                ]);
            }
        }

        // Cœur du verrou optimiste (AGENTS.md règle #5) : positionné AVANT toute modification, pour
        // qu'un jeton périmé rejette l'écriture entière au SaveChangesAsync, jamais un état partiel.
        dbContext.SetOriginalConcurrencyToken(enrollment, request.RowVersion);

        enrollment.BoardingStatus = request.BoardingStatus;
        enrollment.RoomId = request.RoomId;

        // Pension : ajoutée seulement si absente (jamais de doublon sur un second transfert), jamais
        // retirée à la libération (spec §2, décision #7 — le dû annuel déjà facturé reste figé).
        if (request.IncludeBoardingFee && request.BoardingStatus != BoardingStatus.Externe)
        {
            var existingFeeCategoryIds = await dbContext.EnrollmentFeeLines.AsNoTracking()
                .Where(l => l.EnrollmentId == enrollment.Id)
                .Select(l => l.FeeCategoryId)
                .ToListAsync(cancellationToken);

            var tuitionMonths = await ResolveTuitionMonthsAsync(enrollment.SchoolId, cancellationToken);
            var newLines = await BoardingFeeLineBuilder.BuildMissingBoardingLinesAsync(
                dbContext, enrollment.SchoolId, enrollment.ClassroomId, tuitionMonths,
                existingFeeCategoryIds.ToHashSet(), cancellationToken);

            foreach (var line in newLines)
            {
                line.EnrollmentId = enrollment.Id;
                dbContext.EnrollmentFeeLines.Add(line);
                enrollment.TotalDue += line.LineTotal;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new EnrollmentBoardingDto(
            enrollment.Id, enrollment.BoardingStatus, enrollment.RoomId, enrollment.TotalDue,
            (uint)0); // RowVersion à jour lue séparément par l'appelant si nécessaire (voir Task 8/16)
    }

    private async Task<int> ResolveTuitionMonthsAsync(Guid schoolId, CancellationToken ct)
    {
        var settings = await dbContext.SchoolSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.SchoolId == schoolId, ct);
        var months = settings?.TuitionMonthsPerYear ?? SchoolSettingsDefaults.TuitionMonthsPerYear;

        return months < SchoolSettingsDefaults.MinTuitionMonths
            ? SchoolSettingsDefaults.TuitionMonthsPerYear
            : months;
    }
}
```

**Note d'implémentation (RowVersion de retour) :** `EnrollmentBoardingDto.RowVersion` renvoyé à `0` ci-dessus est un espace réservé technique à corriger PENDANT ce step, pas après : après `SaveChangesAsync`, relire le xmin réel avec `await dbContext.Enrollments.Where(e => e.Id == enrollment.Id).Select(e => EF.Property<uint>(e, "xmin")).SingleAsync(cancellationToken)` et l'utiliser dans le DTO retourné, exactement comme `StudentIdentityDto.RowVersion`/`AcademicHistoryEntryDto.RowVersion` sont lus ailleurs dans le dépôt (`GetStudentDetailQueryHandler`). Le test `Assigning_A_Room_Adds_The_Pension_Line_Once` de ce Task réutilise `result1`/`result2` sans vérifier `RowVersion`, mais Task 16 (modale d'affectation) a besoin d'un jeton VALIDE pour permettre un second changement sans 409 — ne pas laisser cette valeur à 0 dans le code final.

- [ ] **Step 4: Lancer les tests pour vérifier qu'ils passent**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~ChangeBoardingAssignmentTests"`
Expected: PASS (4/4).

- [ ] **Step 5: Commit**

```bash
git add src/SamaEcole.Application/Internat/Commands/ChangeBoardingAssignment/ tests/SamaEcole.IntegrationTests/Internat/ChangeBoardingAssignmentTests.cs
git commit -m "feat(internat): ChangeBoardingAssignmentCommand (affectation, capacité 422, xmin 409)"
```

---

### Task 8: Web — `InternatController` et route de page

**Files:**
- Create: `src/SamaEcole.Web/Controllers/InternatController.cs`
- Modify: `src/SamaEcole.Web/Controllers/PagesController.cs` (+ route `/internat`)
- Modify: `openapi.yaml` (+ `FeeCategory.isBoardingFee`)
- Test: `tests/SamaEcole.FunctionalTests/Internat/InternatModuleGateTests.cs`

**Interfaces:**
- Consumes: `GetInternatDashboardQuery` (Task 5), `SearchBoardableStudentsQuery` (Task 6), `ChangeBoardingAssignmentCommand` (Task 7).
- Produces: `GET /api/v1/internat/dashboard`, `GET /api/v1/internat/students/search?term=`, `POST /api/v1/internat/assignments`, page `GET /internat` — consommés par Task 15/16 (frontend).

- [ ] **Step 1: Écrire le test fonctionnel du gate de module (échoue)**

Créer `tests/SamaEcole.FunctionalTests/Internat/InternatModuleGateTests.cs`, en suivant le style de `tests/SamaEcole.FunctionalTests/Schools/SchoolModuleTogglesEndpointsTests.cs` (`AuthApiFactory`, `ResetTestUsersAsync`, `ApiError`) :
```csharp
using System.Net;
using System.Net.Http.Json;
using SamaEcole.FunctionalTests.Common;
using FluentAssertions;
using Xunit;

namespace SamaEcole.FunctionalTests.Internat;

/// <summary>
/// Le module Internat est DÉSACTIVÉ par défaut (SchoolSettingsDefaults.IsInternatEnabled = false,
/// contrairement à Pédagogie/Finance) : contrairement à SchoolModuleTogglesEndpointsTests, ce test
/// prouve le refus PAR DÉFAUT, pas seulement après désactivation explicite.
/// </summary>
public class InternatModuleGateTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Dashboard_Is_Forbidden_By_Default_With_MODULE_DISABLED()
    {
        var directeur = await factory.LoginAsDirecteurAsync(_client);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/internat/dashboard");
        request.Headers.Add("Authorization", $"Bearer {directeur.AccessToken}");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var error = (await response.Content.ReadFromJsonAsync<ApiError>())!;
        error.Code.Should().Be("MODULE_DISABLED");
    }

    [Fact]
    public async Task Enabling_Internat_Grants_Access()
    {
        var directeur = await factory.LoginAsDirecteurAsync(_client);

        var settingsRequest = new HttpRequestMessage(HttpMethod.Put, "/api/v1/schools/current/settings");
        settingsRequest.Headers.Add("Authorization", $"Bearer {directeur.AccessToken}");
        settingsRequest.Content = JsonContent.Create(new { isInternatEnabled = true });
        (await _client.SendAsync(settingsRequest)).StatusCode.Should().Be(HttpStatusCode.OK);

        var dashboardRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/internat/dashboard");
        dashboardRequest.Headers.Add("Authorization", $"Bearer {directeur.AccessToken}");
        var response = await _client.SendAsync(dashboardRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```
Adapter les noms exacts (`factory.LoginAsDirecteurAsync`, la forme du corps `PUT /schools/current/settings`) à ceux réellement utilisés par `SchoolModuleTogglesEndpointsTests.cs` — le lire intégralement avant d'écrire ce fichier plutôt que de deviner sa méthode d'authentification.

- [ ] **Step 2: Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test tests/SamaEcole.FunctionalTests --filter "FullyQualifiedName~InternatModuleGateTests"`
Expected: FAIL — 404 (la route n'existe pas encore).

- [ ] **Step 3: Créer `InternatController`**

Créer `src/SamaEcole.Web/Controllers/InternatController.cs` :
```csharp
using SamaEcole.Application.Internat.Commands.ChangeBoardingAssignment;
using SamaEcole.Application.Internat.Queries.GetInternatDashboard;
using SamaEcole.Application.Internat.Queries.SearchBoardableStudents;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Module Internat (spec docs/superpowers/specs/2026-09-18-module-internat-design.md). Contrôleur
/// mince, aucune logique métier ici (AGENTS.md règle #8). Verrouillé par [RequireModule] — un
/// Directeur qui n'a pas activé l'Internat reçoit 403 MODULE_DISABLED sur toutes les routes
/// ci-dessous, y compris la lecture (spec §4 : le module est désactivé par défaut, contrairement à
/// Pédagogie/Finance).
/// </summary>
[ApiController]
[Route("api/v1/internat")]
[Authorize]
[RequireModule(SchoolModule.Internat)]
public class InternatController(ISender mediator) : ControllerBase
{
    // Directeur + Secretariat + Surveillant — décision actée en brainstorming (spec §4), même trio
    // que ParentSummonsController pour la Vie Scolaire.
    private const string ManageRoles = "Directeur,Secretariat,Surveillant";

    [HttpGet("dashboard")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<InternatDashboardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetInternatDashboardQuery(), cancellationToken));

    [HttpGet("students/search")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<IReadOnlyList<BoardableStudentDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SearchStudents([FromQuery] string term, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new SearchBoardableStudentsQuery(term), cancellationToken));

    public record ChangeBoardingAssignmentRequest(Guid? RoomId, BoardingStatus BoardingStatus, bool IncludeBoardingFee, uint RowVersion);

    [HttpPost("assignments/{enrollmentId:guid}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<EnrollmentBoardingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ChangeAssignment(
        Guid enrollmentId, [FromBody] ChangeBoardingAssignmentRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new ChangeBoardingAssignmentCommand(
                enrollmentId, request.RoomId, request.BoardingStatus, request.IncludeBoardingFee, request.RowVersion),
            cancellationToken));
}
```

- [ ] **Step 4: Ajouter la route de page**

Dans `src/SamaEcole.Web/Controllers/PagesController.cs`, après le bloc `Inventory()`, ajouter :
```csharp

    // Module Internat : logement, régime, affectation de chambre. InternatController garde l'accès
    // ([RequireModule(SchoolModule.Internat)] + rôles) et la RLS isole.
    [HttpGet("/internat")]
    public IActionResult Internat() => View("~/Views/Internat/Index.cshtml");
```

- [ ] **Step 5: Documenter `FeeCategory.isBoardingFee` dans openapi.yaml**

Dans `openapi.yaml:5146-5151`, remplacer :
```yaml
    FeeCategory:
      type: object
      properties:
        id: { type: string, format: uuid }
        name: { type: string, example: "Mensualité" }
        isRecurring: { type: boolean }
```
par :
```yaml
    FeeCategory:
      type: object
      properties:
        id: { type: string, format: uuid }
        name: { type: string, example: "Mensualité" }
        isRecurring: { type: boolean }
        isBoardingFee:
          type: boolean
          description: >-
            Module Internat : catégorie incluable automatiquement sur l'inscription d'un élève
            Interne/Demi-pensionnaire, en plus du barème ordinaire.
```
(Les routes `/api/v1/internat/*` et `/api/v1/enrollments` ne sont pas documentées dans `openapi.yaml` — ni ne l'étaient déjà `/api/v1/enrollments`, `/api/v1/inventory` ou `/api/v1/exams` avant ce plan : suivre la pratique déjà en place plutôt que de rouvrir un chantier de documentation séparé.)

- [ ] **Step 6: Lancer le test pour vérifier qu'il passe**

Run: `dotnet test tests/SamaEcole.FunctionalTests --filter "FullyQualifiedName~InternatModuleGateTests"`
Expected: PASS (2/2). Nécessite PostgreSQL (Testcontainers).

- [ ] **Step 7: Vérifier la compilation complète**

Run: `dotnet build`
Expected: `Build succeeded.`

- [ ] **Step 8: Commit**

```bash
git add src/SamaEcole.Web/Controllers/InternatController.cs src/SamaEcole.Web/Controllers/PagesController.cs openapi.yaml tests/SamaEcole.FunctionalTests/Internat/InternatModuleGateTests.cs
git commit -m "feat(internat): InternatController (dashboard, recherche, affectation) + route /internat"
```

---

### Task 9: Application — `FeeCategory.IsBoardingFee` à la création

**Files:**
- Modify: `src/SamaEcole.Application/Finance/Commands/CreateFeeCategory/CreateFeeCategoryCommand.cs`
- Modify: `src/SamaEcole.Application/Finance/Commands/CreateFeeCategory/CreateFeeCategoryCommandHandler.cs`
- Test: fichier de test unitaire existant du Handler (localiser via `grep -r CreateFeeCategoryCommandHandler tests/ -l`) — étendre plutôt que dupliquer.

**Interfaces:**
- Consumes: `FeeCategory.IsBoardingFee` (Task 1).
- Produces: `CreateFeeCategoryCommand.IsBoardingFee` (`bool`, défaut `false`), `CreateFeeCategoryResult.IsBoardingFee` — consommés par Task 12 (checkbox Fees).

- [ ] **Step 1: Localiser et lire le test unitaire existant**

Run: `grep -rl "CreateFeeCategoryCommandHandler" tests/`
Lire le fichier trouvé pour connaître son fixture exact (probablement `tests/SamaEcole.UnitTests/Finance/CreateFeeCategoryCommandHandlerTests.cs` ou équivalent) avant de l'étendre.

- [ ] **Step 2: Ajouter un test qui échoue**

Dans ce fichier, ajouter (adapter le nom du fake DbContext/tenant provider à celui déjà utilisé par les tests voisins du même fichier) :
```csharp
[Fact]
public async Task Sets_IsBoardingFee_When_Requested()
{
    var db = /* même fixture que les autres tests de ce fichier */;
    var handler = new CreateFeeCategoryCommandHandler(db, /* même tenant provider stub */);

    var result = await handler.Handle(
        new CreateFeeCategoryCommand("Pension", IsRecurring: true, IsBoardingFee: true), CancellationToken.None);

    result.IsBoardingFee.Should().BeTrue();
}
```

- [ ] **Step 3: Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test --filter "FullyQualifiedName~CreateFeeCategoryCommandHandlerTests"`
Expected: FAIL — `IsBoardingFee` n'existe pas sur `CreateFeeCategoryCommand`/`CreateFeeCategoryResult`.

- [ ] **Step 4: Étendre `CreateFeeCategoryCommand`**

Lire `src/SamaEcole.Application/Finance/Commands/CreateFeeCategory/CreateFeeCategoryCommand.cs` puis ajouter un paramètre `IsBoardingFee` (défaut `false`) au `record`, et un champ `IsBoardingFee` sur `CreateFeeCategoryResult`, en suivant exactement la forme déjà prise par `IsRecurring` dans ce même fichier.

- [ ] **Step 5: Étendre `CreateFeeCategoryCommandHandler`**

Dans `src/SamaEcole.Application/Finance/Commands/CreateFeeCategory/CreateFeeCategoryCommandHandler.cs`, dans la construction de `new FeeCategory { ... }`, ajouter `IsBoardingFee = request.IsBoardingFee,` après `IsRecurring = request.IsRecurring`, et dans le `return new CreateFeeCategoryResult(...)`, ajouter `category.IsBoardingFee`.

- [ ] **Step 6: Lancer le test pour vérifier qu'il passe**

Run: `dotnet test --filter "FullyQualifiedName~CreateFeeCategoryCommandHandlerTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/SamaEcole.Application/Finance/Commands/CreateFeeCategory/
git commit -m "feat(internat): IsBoardingFee sur CreateFeeCategoryCommand"
```

---

### Task 10: Web — icône `bed` dans le sprite

**Files:**
- Modify: `src/SamaEcole.Web/Views/Shared/_IconSprite.cshtml`

**Interfaces:**
- Produces: `<icon name="bed" />` disponible partout — consommé par Task 11 (entrée sidebar).

- [ ] **Step 1: Sourcer le glyphe**

Le sprite existant (`icon-building`, `icon-archive`, …) reprend le jeu Fluent UI System Icons (Volume 5 §2.3, doc de `IconTagHelper`), sans dépendance npm locale (les chemins SVG sont copiés à la main). Récupérer le glyphe canonique `ic_fluent_bed_24_regular` depuis le dépôt officiel Microsoft :

Run: `curl -s https://raw.githubusercontent.com/microsoft/fluentui-system-icons/main/assets/Bed/SVG/ic_fluent_bed_24_regular.svg`

Extraire la valeur de l'attribut `d="..."` du (ou des) `<path>` du fichier obtenu.

- [ ] **Step 2: Ajouter le symbole**

Dans `src/SamaEcole.Web/Views/Shared/_IconSprite.cshtml`, après le `</symbol>` de `icon-building` (ligne 85), ajouter :
```html
 <symbol id="icon-bed" viewBox="0 0 24 24">
 <path d="<COLLER ICI le(s) attribut(s) d du fichier sourcé au Step 1>" />
 </symbol>
```
Respecter l'indentation à un espace des symboles voisins (convention déjà en place dans ce fichier).

- [ ] **Step 3: Vérifier visuellement**

Ce fichier n'a pas de test automatisé (icônes = rendu visuel). Vérifier au navigateur (skill `run`) sur n'importe quel écran existant en ajoutant temporairement `<icon name="bed" class="w-6 h-6" />` — ou directement lors de la vérification de Task 11 (l'icône apparaît dans la sidebar). Ne PAS committer de balise de test temporaire.

- [ ] **Step 4: Commit**

```bash
git add src/SamaEcole.Web/Views/Shared/_IconSprite.cshtml
git commit -m "feat(internat): ajouter l'icône bed (Fluent UI System Icons) au sprite partagé"
```

---

### Task 11: Web — navigation (sidebar)

**Files:**
- Modify: `src/SamaEcole.Web/wwwroot/js/auth.js:456-482`
- Modify: `src/SamaEcole.Web/Views/Shared/_Layout.cshtml:137, ~193`

**Interfaces:**
- Consumes: `icon-bed` (Task 10), `GET /schools/current/settings` (existant, retourne déjà `isInternatEnabled` depuis commit `12ffbb2`).
- Produces: `sidebarNav().internatEnabled` (bool, défaut `false`) — lu par `_Layout.cshtml`.

- [ ] **Step 1: Étendre `sidebarNav()` dans `auth.js`**

Dans `src/SamaEcole.Web/wwwroot/js/auth.js`, remplacer (lignes 458-482) :
```javascript
        pedagogyEnabled: true,
        financeEnabled: true,

        async init() {
            // Super Admin plateforme : pas d'école, pas de settings. On laisse les valeurs par défaut.
            if (!window.auth.isAuthenticated() || window.auth.role === 'SuperAdmin') return;
            try {
                const s = await window.api.get('/schools/current/settings');
                this.isPublicSchool = (s && s.typeEtablissement === 'Public');
                this.pedagogyEnabled = !s || s.isPedagogyEnabled !== false;
                this.financeEnabled = !s || s.isFinanceEnabled !== false;
            } catch {
                // Non bloquant : en cas d'erreur réseau, la sidebar reste complète (sûr par défaut).
                this.isPublicSchool = false;
                this.pedagogyEnabled = true;
                this.financeEnabled = true;
            }
        },
```
par :
```javascript
        pedagogyEnabled: true,
        financeEnabled: true,

        /**
         * Module Internat (Paramètres › Modules) — INVERSE de pedagogyEnabled/financeEnabled :
         * désactivé par défaut (SchoolSettingsDefaults.IsInternatEnabled = false), donc masqué tant
         * que la réponse n'est pas arrivée ou en cas d'erreur réseau (sûr par défaut = caché, pas
         * affiché, puisque le module est réservé/inerte tant que le Directeur ne l'a pas activé).
         */
        internatEnabled: false,

        async init() {
            // Super Admin plateforme : pas d'école, pas de settings. On laisse les valeurs par défaut.
            if (!window.auth.isAuthenticated() || window.auth.role === 'SuperAdmin') return;
            try {
                const s = await window.api.get('/schools/current/settings');
                this.isPublicSchool = (s && s.typeEtablissement === 'Public');
                this.pedagogyEnabled = !s || s.isPedagogyEnabled !== false;
                this.financeEnabled = !s || s.isFinanceEnabled !== false;
                this.internatEnabled = !!s && s.isInternatEnabled === true;
            } catch {
                // Non bloquant : en cas d'erreur réseau, la sidebar reste complète pour
                // Pédagogie/Finance (socle métier, sûr par défaut) mais Internat reste masqué —
                // il n'y a rien de "sûr par défaut" à afficher pour un module réservé/inerte.
                this.isPublicSchool = false;
                this.pedagogyEnabled = true;
                this.financeEnabled = true;
                this.internatEnabled = false;
            }
        },
```

- [ ] **Step 2: Ajouter l'entrée de menu dans `_Layout.cshtml`**

Ligne 137, ajouter `/internat` à la liste des préfixes qui ouvrent le pôle « Gestion Scolaire » :
```csharp
bool gestionScolaireOpen = path.StartsWith("/eleves") || path.StartsWith("/inscriptions") || path.StartsWith("/classes") || path.StartsWith("/infrastructures") || path.StartsWith("/inventaire") || path.StartsWith("/internat") || path.StartsWith("/matieres") || path.StartsWith("/enseignants") || path.StartsWith("/notes") || path.StartsWith("/examens") || path.StartsWith("/integration-etatique") || path.StartsWith("/rapports/assiduite") || path.StartsWith("/caisse");
```

Après l'entrée `/inventaire` (autour de la ligne 193), ajouter :
```html
<a href="/internat" x-show="canView(['Directeur', 'Secretariat', 'Surveillant']) && internatEnabled" class="@(path.StartsWith("/internat") ? LinkActive : LinkIdle)">
    <icon name="bed" class="w-5 h-5" />
    <span class="text-sm font-medium">Internat</span>
</a>
```
Le rôle `Surveillant` figure déjà dans l'union du pôle (ligne 158, ajouté pour l'Inventaire) : aucune modification de cette union n'est nécessaire.

- [ ] **Step 3: Vérifier au navigateur**

Utiliser le skill `run` : se connecter en Directeur, activer le module Internat depuis Paramètres › Modules (déjà livré, commit `12ffbb2`), vérifier que l'entrée « Internat » apparaît dans la sidebar SANS rechargement de page (Alpine réactif), puis le désactiver et vérifier sa disparition.

- [ ] **Step 4: Commit**

```bash
git add src/SamaEcole.Web/wwwroot/js/auth.js src/SamaEcole.Web/Views/Shared/_Layout.cshtml
git commit -m "feat(internat): entrée de navigation Internat, réactive au toggle du module"
```

---

### Task 12: Web — checkbox « Frais d'internat » sur les catégories de frais

**Files:**
- Modify: `src/SamaEcole.Web/wwwroot/js/fees.js:81, 218`
- Modify: `src/SamaEcole.Web/Views/Fees/Index.cshtml:174-179`

**Interfaces:**
- Consumes: `CreateFeeCategoryCommand.IsBoardingFee` (Task 9).

- [ ] **Step 1: Étendre l'état Alpine dans `fees.js`**

Ligne 81, remplacer `newCategory: { name: '', isRecurring: true },` par `newCategory: { name: '', isRecurring: true, isBoardingFee: false },`
Ligne 218, remplacer `this.newCategory = { name: '', isRecurring: true };` par `this.newCategory = { name: '', isRecurring: true, isBoardingFee: false };`

Localiser (par recherche du même fichier) l'endroit où `newCategory` est envoyé au serveur (probablement un appel `window.api.post('/finance/fee-categories', this.newCategory)` ou similaire dans une méthode `saveCategory()`/`createCategory()`) et vérifier que `isBoardingFee` est bien inclus dans le corps envoyé (un objet spread `{...this.newCategory}` ou littéral explicite — si explicite, y ajouter `isBoardingFee: this.newCategory.isBoardingFee`).

- [ ] **Step 2: Ajouter la case à cocher dans `Views/Fees/Index.cshtml`**

Après le bloc (lignes 174-179) :
```html
<div class="flex items-start gap-2">
    <input id="cat-recurring" type="checkbox" x-model="newCategory.isRecurring" class="checkbox-field mt-1">
    <label for="cat-recurring" class="text-sm text-slate-700">
        Frais mensuel (dû chaque mois)
        <span class="block text-xs text-slate-400">Décochez pour un frais ponctuel : inscription, uniforme…</span>
    </label>
</div>
```
ajouter :
```html
<div class="flex items-start gap-2">
    <input id="cat-boarding" type="checkbox" x-model="newCategory.isBoardingFee" class="checkbox-field mt-1">
    <label for="cat-boarding" class="text-sm text-slate-700">
        Frais d'internat (module Internat)
        <span class="block text-xs text-slate-400">Proposable automatiquement à l'inscription d'un élève Interne/Demi-pensionnaire (écran Internat, Inscriptions).</span>
    </label>
</div>
```

- [ ] **Step 3: Vérifier au navigateur**

Skill `run` : créer une catégorie « Pension » avec la case cochée, vérifier en base (`docker exec ... psql -c "select name, is_boarding_fee from fee_categories"`) qu'elle est bien à `true`.

- [ ] **Step 4: Commit**

```bash
git add src/SamaEcole.Web/wwwroot/js/fees.js src/SamaEcole.Web/Views/Fees/Index.cshtml
git commit -m "feat(internat): case 'Frais d'internat' à la création d'une catégorie de frais"
```

---

### Task 13: Web — `RoomType.Dortoir` dans l'écran Bâtiments & Salles

**Files:**
- Modify: `src/SamaEcole.Web/Views/Buildings/Index.cshtml:270-271, 302-303`
- Modify: `src/SamaEcole.Web/wwwroot/js/buildings.js` (fonction `roomTypeLabel`, si elle existe déjà — sinon la localiser sous un autre nom)

**Interfaces:**
- Consumes: `RoomType.Dortoir` (Task 1).

- [ ] **Step 1: Ajouter l'option dans les deux `select-field`**

Dans `src/SamaEcole.Web/Views/Buildings/Index.cshtml`, remplacer les DEUX occurrences (création ligne 271, édition ligne 303) de :
```
options-expr="[{value:'SalleDeClasse', label:'Salle de classe'},{value:'Laboratoire', label:'Laboratoire'},{value:'Bureau', label:'Bureau'},{value:'Autre', label:'Autre'}]"
```
par :
```
options-expr="[{value:'SalleDeClasse', label:'Salle de classe'},{value:'Laboratoire', label:'Laboratoire'},{value:'Bureau', label:'Bureau'},{value:'Dortoir', label:'Dortoir'},{value:'Autre', label:'Autre'}]"
```

- [ ] **Step 2: Étendre `roomTypeLabel` dans `buildings.js`**

Localiser la fonction `roomTypeLabel` référencée en `Views/Buildings/Index.cshtml:146` (`x-text="roomTypeLabel(room.type)"`) dans `src/SamaEcole.Web/wwwroot/js/buildings.js`, et y ajouter le mappage `Dortoir` → `'Dortoir'` (même style que les autres entrées de ce mapping, à lire avant d'éditer).

- [ ] **Step 3: Vérifier au navigateur**

Skill `run` : créer une salle de type Dortoir depuis `/infrastructures`, vérifier qu'elle s'affiche avec le badge « Dortoir » dans la liste.

- [ ] **Step 4: Commit**

```bash
git add src/SamaEcole.Web/Views/Buildings/Index.cshtml src/SamaEcole.Web/wwwroot/js/buildings.js
git commit -m "feat(internat): type de salle Dortoir dans l'écran Bâtiments & Salles"
```

---

### Task 14: Application + Web — badge d'hébergement sur la fiche élève

**Files:**
- Modify: `src/SamaEcole.Application/Students/Queries/GetStudentDetail/GetStudentDetailQuery.cs`
- Test: `tests/SamaEcole.IntegrationTests/Students/` (localiser le test existant de `GetStudentDetailQueryHandler` s'il existe, sinon ajouter un test ciblé)
- Modify: `src/SamaEcole.Web/Views/Students/Index.cshtml:277-350` (zone des blocs d'identité)

**Interfaces:**
- Consumes: `Enrollment.BoardingStatus`/`RoomId` (Task 1), `Room`/`Building` (existants).
- Produces: `AcademicHistoryEntryDto.BoardingStatus` (string), `AcademicHistoryEntryDto.RoomLabel` (string?, ex. "Pavillon A — Chambre 102") — consommés par la fiche élève.

- [ ] **Step 1: Étendre `AcademicHistoryEntryDto`**

Dans `src/SamaEcole.Application/Students/Queries/GetStudentDetail/GetStudentDetailQuery.cs`, remplacer :
```csharp
public record AcademicHistoryEntryDto(
    Guid EnrollmentId,
    Guid SchoolYearId,
    string SchoolYearLabel,
    string ClassroomName,
    string EnrollmentType,
    string Status,
    bool IsActiveYear,
    decimal? GeneralAverage,
    DateTimeOffset EnrolledAt,
    uint RowVersion);
```
par :
```csharp
public record AcademicHistoryEntryDto(
    Guid EnrollmentId,
    Guid SchoolYearId,
    string SchoolYearLabel,
    string ClassroomName,
    string EnrollmentType,
    string Status,
    bool IsActiveYear,
    decimal? GeneralAverage,
    DateTimeOffset EnrolledAt,
    uint RowVersion,

    // Module Internat : régime de CETTE inscription (portée annuelle, comme le reste de la ligne).
    // RoomLabel est null pour Externe ou pour un régime posé sans chambre affectée pour l'instant.
    string BoardingStatus,
    string? RoomLabel);
```

- [ ] **Step 2: Étendre la projection dans `GetStudentDetailQueryHandler`**

Dans la requête LINQ `enrollments` (bloc `from e in dbContext.Enrollments...select new { ... }`), ajouter aux champs sélectionnés :
```csharp
                e.BoardingStatus,
                RoomLabel = e.RoomId == null ? null :
                    dbContext.Rooms.AsNoTracking()
                        .Where(r => r.Id == e.RoomId)
                        .Select(r => r.Name + " — " + dbContext.Buildings.AsNoTracking()
                            .Where(b => b.Id == r.BuildingId).Select(b => b.Name).FirstOrDefault())
                        .FirstOrDefault()
```
Et dans la construction de `academicHistory` (`.Select(e => new AcademicHistoryEntryDto(...))`), ajouter en fin d'appel `e.BoardingStatus.ToString(), e.RoomLabel`.

- [ ] **Step 3: Écrire/étendre un test d'intégration**

Localiser un éventuel test existant de `GetStudentDetailQueryHandler` :
Run: `grep -rl "GetStudentDetailQueryHandler" tests/`
S'il existe, y ajouter un cas couvrant un élève `Interne` affecté à une chambre, vérifiant `academicHistory[0].BoardingStatus == "Interne"` et `RoomLabel` au format `"<Salle> — <Bâtiment>"`. Sinon, créer `tests/SamaEcole.IntegrationTests/Students/GetStudentDetailBoardingTests.cs` avec le même fixture que Task 5.

- [ ] **Step 4: Lancer les tests**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~GetStudentDetail"`
Expected: PASS.

- [ ] **Step 5: Ajouter le badge dans `Views/Students/Index.cshtml`**

Dans le bloc de la grille d'identité (après le bloc IEN, lignes ~344-350), ajouter un bloc affichant le régime de l'inscription ACTIVE (`studentDetail.academicHistory.find(h => h.isActiveYear)`) :
```html
<div>
    <p class="text-[11px] font-bold text-slate-400 uppercase tracking-wider mb-1">Hébergement</p>
    <template x-if="activeEnrollment() && activeEnrollment().boardingStatus !== 'Externe'">
        <badge variant="info" x-text="activeEnrollment().boardingStatus === 'Interne' ? ('Interne' + (activeEnrollment().roomLabel ? ' — ' + activeEnrollment().roomLabel : '')) : 'Demi-pensionnaire'"></badge>
    </template>
    <template x-if="!activeEnrollment() || activeEnrollment().boardingStatus === 'Externe'">
        <span class="text-sm text-slate-400">Externe</span>
    </template>
</div>
```
Ajouter la méthode helper dans le composant Alpine du fichier (`students.js` ou inline selon où vit la logique de cette page — la localiser d'abord) :
```javascript
activeEnrollment() {
    return this.studentDetail?.academicHistory?.find(h => h.isActiveYear) ?? null;
}
```

- [ ] **Step 6: Vérifier au navigateur**

Skill `run` : ouvrir la fiche d'un élève Interne affecté à une chambre, vérifier le badge « Interne — Chambre X — Pavillon Y ». Ouvrir la fiche d'un élève Externe, vérifier l'absence de badge coloré.

- [ ] **Step 7: Commit**

```bash
git add src/SamaEcole.Application/Students/Queries/GetStudentDetail/GetStudentDetailQuery.cs src/SamaEcole.Web/Views/Students/Index.cshtml tests/SamaEcole.IntegrationTests/Students/
git commit -m "feat(internat): badge d'hébergement sur la fiche élève"
```

---

### Task 15: Web — écran `/internat` (tableau de bord, lecture seule)

**Files:**
- Create: `src/SamaEcole.Web/Views/Internat/Index.cshtml`
- Create: `src/SamaEcole.Web/wwwroot/js/internat.js`
- Modify: `src/SamaEcole.Web/Views/Shared/_Layout.cshtml` (script include, si les scripts sont listés explicitement — vérifier le pattern déjà en place pour `inventory.js`)

**Interfaces:**
- Consumes: `GET /api/v1/internat/dashboard` (Task 8).
- Produces: page `/internat` fonctionnelle — Task 16 y ajoute la modale d'affectation (même fichier `internat.js`, composant Alpine étendu).

- [ ] **Step 1: Lire un écran de référence**

Lire intégralement `src/SamaEcole.Web/Views/Inventory/Index.cshtml` et `src/SamaEcole.Web/wwwroot/js/inventory.js` (structure la plus proche : plusieurs onglets, cartes, un composant Alpine `data()` avec `init()` chargeant une query en GET) pour reproduire exactement le squelette de page (layout, includes, structure `@{ Layout = ... }`, montage du composant Alpine).

- [ ] **Step 2: Créer `internat.js` (partie tableau de bord)**

Créer `src/SamaEcole.Web/wwwroot/js/internat.js` :
```javascript
document.addEventListener('alpine:init', () => {
    Alpine.data('internatPage', () => ({
        loading: true,
        error: null,
        dashboard: null,

        async init() {
            await this.loadDashboard();
        },

        async loadDashboard() {
            this.loading = true;
            this.error = null;
            try {
                this.dashboard = await window.api.get('/internat/dashboard');
            } catch (e) {
                this.error = window.api.toMessage ? window.api.toMessage(e) : "Impossible de charger le tableau de bord de l'internat.";
            } finally {
                this.loading = false;
            }
        },

        occupancyColor(room) {
            if (room.occupantsCount >= room.capacity) return 'bg-danger-500';
            if (room.occupantsCount === 0) return 'bg-slate-300';
            return 'bg-success-500';
        },

        occupancyPercent(room) {
            return room.capacity > 0 ? Math.round((room.occupantsCount / room.capacity) * 100) : 0;
        }
    }));
});
```
(La modale d'affectation — recherche, badge, transfert, confirmation — est ajoutée à ce même composant au Task 16, pas dans un fichier séparé.)

- [ ] **Step 3: Créer `Views/Internat/Index.cshtml`**

Créer `src/SamaEcole.Web/Views/Internat/Index.cshtml`, en reprenant le gabarit exact lu au Step 1 (layout, `@section Scripts` incluant `internat.js`, `[AllowAnonymous]` côté vue puisque `InternatController` garde l'accès réel). Structure du corps :
```html
<div x-data="internatPage" x-init="init()">
    <div x-show="loading" x-cloak class="py-12 text-center text-slate-400">Chargement…</div>
    <div x-show="error" x-cloak class="alert-error"><span x-text="error"></span></div>

    <template x-if="!loading && dashboard">
        <div>
            @* Barre KPI *@
            <div class="grid grid-cols-2 sm:grid-cols-4 gap-4 mb-6">
                <div class="kpi-tile">
                    <p class="kpi-label">Occupation</p>
                    <p class="kpi-value"><span x-text="dashboard.totalOccupied"></span> / <span x-text="dashboard.totalCapacity"></span></p>
                </div>
                <div class="kpi-tile">
                    <p class="kpi-label">Internes</p>
                    <p class="kpi-value" x-text="dashboard.interneCount"></p>
                </div>
                <div class="kpi-tile">
                    <p class="kpi-label">Demi-pensionnaires</p>
                    <p class="kpi-value" x-text="dashboard.demiPensionnaireCount"></p>
                </div>
                <div class="kpi-tile">
                    <p class="kpi-label">Chambres complètes</p>
                    <p class="kpi-value"><span x-text="dashboard.fullRoomsCount"></span> / <span x-text="dashboard.rooms.length"></span></p>
                </div>
            </div>

            @* Cartes par chambre *@
            <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
                <template x-for="room in dashboard.rooms" :key="room.roomId">
                    <div class="card p-4" x-data="{ expanded: false }">
                        <div class="flex items-center justify-between">
                            <div>
                                <p class="font-semibold text-slate-800" x-text="room.roomName"></p>
                                <p class="text-xs text-slate-400" x-text="room.buildingName"></p>
                            </div>
                            <span class="text-sm font-medium tabular-nums" x-text="room.occupantsCount + ' / ' + room.capacity"></span>
                        </div>
                        <div class="mt-2 h-2 rounded-full bg-slate-100 overflow-hidden">
                            <div class="h-full rounded-full transition-all" :class="occupancyColor(room)" :style="'width: ' + occupancyPercent(room) + '%'"></div>
                        </div>
                        <button type="button" class="mt-3 text-xs font-medium text-primary-600" x-on:click="expanded = !expanded" x-show="room.occupants.length > 0">
                            <span x-text="expanded ? 'Masquer les occupants' : 'Voir les occupants (' + room.occupants.length + ')'"></span>
                        </button>
                        <ul x-show="expanded" x-cloak class="mt-2 space-y-1 text-sm">
                            <template x-for="occupant in room.occupants" :key="occupant.enrollmentId">
                                <li class="flex items-center justify-between text-slate-600">
                                    <span x-text="occupant.fullName + ' — ' + occupant.classroomName"></span>
                                    <span class="text-xs text-slate-400" x-text="occupant.guardianPhone || ''"></span>
                                </li>
                            </template>
                        </ul>
                        <button type="button" class="btn-secondary mt-3 w-full text-sm"
                                :disabled="room.occupantsCount >= room.capacity"
                                x-on:click="openAssignModal(room)">
                            + Affecter un élève
                        </button>
                    </div>
                </template>
            </div>
        </div>
    </template>
</div>
```
Les classes `kpi-tile`/`kpi-label`/`kpi-value`/`card`/`btn-secondary`/`alert-error` doivent reprendre EXACTEMENT les classes utilitaires déjà utilisées par `Views/Inventory/Index.cshtml` ou `Views/Buildings/Index.cshtml` (vérifier leur nom réel au Step 1 — ce ne sont probablement pas des classes CSS nommées ainsi mais des combinaisons Tailwind directes ; remplacer par les classes RÉELLES observées, ce squelette n'est qu'indicatif de la structure).

`openAssignModal(room)` est implémenté au Task 16.

- [ ] **Step 4: Vérifier au navigateur**

Skill `run` : activer l'Internat, créer un Pavillon/Chambre (Dortoir) via `/infrastructures`, ouvrir `/internat`, vérifier l'affichage des KPIs (0/capacité) et de la carte de chambre.

- [ ] **Step 5: Commit**

```bash
git add src/SamaEcole.Web/Views/Internat/Index.cshtml src/SamaEcole.Web/wwwroot/js/internat.js
git commit -m "feat(internat): écran /internat — tableau de bord (KPIs, cartes par chambre)"
```

---

### Task 16: Web — modale d'affectation rapide

**Files:**
- Modify: `src/SamaEcole.Web/wwwroot/js/internat.js` (étendre `internatPage`)
- Modify: `src/SamaEcole.Web/Views/Internat/Index.cshtml` (+ `<modal-shell>`)

**Interfaces:**
- Consumes: `GET /api/v1/internat/students/search?term=` (Task 8), `POST /api/v1/internat/assignments/{enrollmentId}` (Task 8).

- [ ] **Step 1: Lire le pattern `modal-shell` existant**

Lire `src/SamaEcole.Web/Views/Students/Index.cshtml:1268-1300` (modale IEN) pour reprendre exactement la structure `<modal-shell open=... on-close=... size=... title=...>`.

- [ ] **Step 2: Étendre `internatPage` dans `internat.js`**

Ajouter à l'objet retourné par `Alpine.data('internatPage', () => ({...}))` :
```javascript
        assignModal: { open: false, room: null, searchTerm: '', results: [], selected: null, boardingStatus: 'Interne', includeBoardingFee: true, saving: false, error: null },
        searchDebounce: null,

        openAssignModal(room) {
            this.assignModal = { open: true, room, searchTerm: '', results: [], selected: null, boardingStatus: 'Interne', includeBoardingFee: true, saving: false, error: null };
        },

        closeAssignModal() {
            this.assignModal.open = false;
        },

        onSearchInput() {
            clearTimeout(this.searchDebounce);
            this.searchDebounce = setTimeout(() => this.searchStudents(), 300);
        },

        async searchStudents() {
            const term = this.assignModal.searchTerm.trim();
            if (term.length < 2) { this.assignModal.results = []; return; }
            try {
                this.assignModal.results = await window.api.get('/internat/students/search?term=' + encodeURIComponent(term));
            } catch {
                this.assignModal.results = [];
            }
        },

        selectStudent(student) {
            this.assignModal.selected = student;
            this.assignModal.results = [];
            this.assignModal.searchTerm = student.fullName;
        },

        isTransfer() {
            const s = this.assignModal.selected;
            return !!(s && s.currentRoomId && s.currentRoomId !== this.assignModal.room.roomId);
        },

        async confirmAssignment() {
            const modal = this.assignModal;
            if (!modal.selected) return;

            modal.saving = true;
            modal.error = null;
            try {
                await window.api.post(`/internat/assignments/${modal.selected.enrollmentId}`, {
                    roomId: modal.room.roomId,
                    boardingStatus: modal.boardingStatus,
                    includeBoardingFee: modal.includeBoardingFee,
                    rowVersion: modal.selected.rowVersion || 0
                });
                this.closeAssignModal();
                await this.loadDashboard();
            } catch (e) {
                const status = e && e.status;
                if (status === 422) {
                    modal.error = "Cette chambre a atteint sa capacité maximale.";
                } else if (status === 409) {
                    modal.error = "Le dossier de cet élève a été modifié par un autre utilisateur.";
                } else {
                    modal.error = window.api.toMessage ? window.api.toMessage(e) : "Une erreur est survenue.";
                }
            } finally {
                modal.saving = false;
            }
        }
```
**Point à vérifier pendant l'implémentation :** `SearchBoardableStudentsQuery` (Task 6) ne renvoie pas de `rowVersion` dans `BoardableStudentDto` — c'est nécessaire pour poser `SetOriginalConcurrencyToken` côté serveur. Avant d'écrire ce Step, revenir sur `BoardableStudentDto` (Task 6) et lui ajouter `uint RowVersion` (lu via `EF.Property<uint>(e, "xmin")` dans la projection, comme `AcademicHistoryEntryDto.RowVersion`), et utiliser `modal.selected.rowVersion` (sans repli à `0`) dans l'appel ci-dessus — un jeton à `0` provoquerait un 409 systématique dès qu'une inscription a déjà été modifiée une fois.

- [ ] **Step 3: Ajouter la modale dans `Views/Internat/Index.cshtml`**

Ajouter en fin de fichier (avant la fermeture du `<div x-data="internatPage">`) :
```html
<modal-shell open="assignModal.open" on-close="closeAssignModal()" size="md" title="Affecter un élève">
    <div class="space-y-4">
        <p class="text-sm text-slate-500">Chambre : <strong x-text="assignModal.room?.roomName"></strong> (<span x-text="assignModal.room?.buildingName"></span>)</p>

        <div class="relative">
            <label class="form-label">Élève</label>
            <input type="text" x-model="assignModal.searchTerm" x-on:input="onSearchInput()"
                   placeholder="Nom, prénom ou matricule" class="input-field mt-1" autocomplete="off" />
            <ul x-show="assignModal.results.length > 0" x-cloak class="absolute z-10 w-full bg-white border border-slate-200 rounded-lg mt-1 shadow-lg max-h-48 overflow-auto">
                <template x-for="student in assignModal.results" :key="student.enrollmentId">
                    <li class="px-3 py-2 hover:bg-slate-50 cursor-pointer text-sm" x-on:click="selectStudent(student)">
                        <span x-text="student.fullName"></span>
                        <span class="text-xs text-slate-400" x-text="' — ' + student.classroomName"></span>
                    </li>
                </template>
            </ul>
        </div>

        <template x-if="assignModal.selected">
            <div class="space-y-3">
                <badge :variant="assignModal.selected.boardingStatus === 'Externe' ? 'neutral' : 'info'"
                       x-text="assignModal.selected.boardingStatus + (assignModal.selected.currentRoomName ? ' — ' + assignModal.selected.currentRoomName : '')"></badge>

                <p x-show="isTransfer()" x-cloak class="text-sm text-warning-600">
                    L'élève <span x-text="assignModal.selected.fullName"></span> sera transféré depuis
                    <span x-text="assignModal.selected.currentRoomName"></span> vers cette chambre.
                </p>

                <div>
                    <label class="form-label">Régime</label>
                    <select-field model="assignModal.boardingStatus"
                        options-expr="[{value:'Interne', label:'Interne'},{value:'DemiPensionnaire', label:'Demi-pensionnaire'}]" />
                </div>

                <div class="flex items-start gap-2">
                    <input id="assign-boarding-fee" type="checkbox" x-model="assignModal.includeBoardingFee" class="checkbox-field mt-1">
                    <label for="assign-boarding-fee" class="text-sm text-slate-700">
                        Proposer l'ajout du frais de pension au dossier de l'élève
                    </label>
                </div>
            </div>
        </template>

        <p x-show="assignModal.error" x-cloak class="field-error" x-text="assignModal.error"></p>

        <div class="flex justify-end gap-2 pt-2">
            <button type="button" class="btn-secondary" x-on:click="closeAssignModal()">Annuler</button>
            <button type="button" class="btn-primary" :disabled="!assignModal.selected || assignModal.saving" x-on:click="confirmAssignment()">
                Confirmer l'affectation
            </button>
        </div>
    </div>
</modal-shell>
```
Remplacer les noms de classes utilitaires (`form-label`, `input-field`, `field-error`, `btn-secondary`, `btn-primary`, `checkbox-field`, `badge`) par ceux RÉELLEMENT utilisés dans `Views/Students/Index.cshtml` (déjà observés : `form-label`, `checkbox-field`, `field-error`, `input-field` existent bel et bien — `btn-primary`/`btn-secondary` à confirmer par lecture du fichier de référence au Step 1 de Task 15).

- [ ] **Step 4: Ajouter le champ « Libérer / Changer »**

Sur chaque occupant listé dans l'accordéon (Task 15, Step 3), ajouter un bouton :
```html
<button type="button" class="text-xs text-danger-600" x-on:click="openAssignModal(room); assignModal.searchTerm = occupant.fullName; searchStudents();">
    Changer / Libérer
</button>
```
(Réutilise la même modale : sélectionner l'élève déjà affecté puis choisir une autre chambre, ou régime Externe pour libérer — cohérent avec la spec §6.2 qui ne distingue pas les deux actions au niveau UI, seulement au niveau du payload envoyé.)

Pour la LIBÉRATION spécifiquement (régime → Externe, `RoomId = null`), ajouter une option dans le select de régime de la modale : `{value:'Externe', label:'Externe (libérer)'}`, et dans `confirmAssignment()`, poser `roomId: modal.boardingStatus === 'Externe' ? null : modal.room.roomId`.

- [ ] **Step 5: Vérifier au navigateur — parcours complet**

Skill `run` :
1. Affecter un élève Externe à une chambre (régime Interne, case pension cochée) → vérifier KPI mis à jour, ligne de pension visible sur la fiche financière de l'élève.
2. Réaffecter le même élève à une autre chambre → vérifier l'avertissement de transfert, vérifier qu'AUCUNE seconde ligne de pension n'apparaît.
3. Remplir une chambre à sa capacité, tenter une troisième affectation → message 422 explicite.
4. Libérer l'élève (régime Externe) → vérifier que la chambre se libère dans le KPI et que la ligne de pension reste sur sa fiche financière.

- [ ] **Step 6: Commit**

```bash
git add src/SamaEcole.Web/wwwroot/js/internat.js src/SamaEcole.Web/Views/Internat/Index.cshtml src/SamaEcole.Application/Internat/Queries/SearchBoardableStudents/
git commit -m "feat(internat): modale d'affectation rapide (recherche, transfert, pension, 422/409)"
```

---

### Task 17: Web — Régime & Hébergement dans le formulaire d'inscription

**Files:**
- Modify: `src/SamaEcole.Web/wwwroot/js/enrollments.js`
- Modify: `src/SamaEcole.Web/Views/Enrollments/Index.cshtml:174-195`

**Interfaces:**
- Consumes: `CreateEnrollmentCommand.BoardingStatus`/`RoomId`/`IncludeBoardingFee` (Task 4), `GET /api/v1/internat/dashboard` (Task 8, pour lister les chambres avec places libres — ou une query plus légère si le dashboard complet est jugé disproportionné pour ce seul besoin ; réutiliser le dashboard reste le plus simple et évite une route supplémentaire).

- [ ] **Step 1: Étendre l'état du formulaire dans `enrollments.js`**

Localiser les lignes exactes (déjà repérées : ligne 42 `isRepeating: false`, ligne 345 un second bloc de remise à zéro du formulaire, lignes 276/282 construction du payload) et y ajouter, à CHAQUE endroit où `isRepeating` apparaît dans un objet `form`/payload :
```javascript
boardingStatus: 'Externe',
roomId: null,
includeBoardingFee: true,
```
Ajouter une méthode pour charger les chambres avec places libres (réutilise le dashboard Internat, filtré côté client) :
```javascript
availableRooms: [],

async loadAvailableRooms() {
    try {
        const dashboard = await window.api.get('/internat/dashboard');
        this.availableRooms = dashboard.rooms.filter(r => r.occupantsCount < r.capacity);
    } catch {
        this.availableRooms = []; // Module désactivé (403) ou aucune chambre : le sélecteur reste vide.
    }
}
```
Appeler `this.loadAvailableRooms()` dans `init()` UNIQUEMENT si le module est actif — vérifier `window.api.get('/schools/current/settings')` (déjà probablement chargé ailleurs sur cette page) avant l'appel, pour éviter un 403 bruyant en console sur une école qui n'a pas activé l'Internat.

- [ ] **Step 2: Inclure les champs dans le payload d'inscription**

Aux lignes 276 et 282 (les deux branches NewEnrollment/ReEnrollment du payload envoyé à `POST /enrollments`), ajouter :
```javascript
boardingStatus: this.form.boardingStatus,
roomId: this.form.boardingStatus === 'Externe' ? null : this.form.roomId,
includeBoardingFee: this.form.boardingStatus === 'Externe' ? false : this.form.includeBoardingFee,
```

- [ ] **Step 3: Ajouter la section dans `Views/Enrollments/Index.cshtml`**

Après le bloc `IsRepeating` (lignes ~186-195), ajouter, VISIBLE seulement si le module est actif côté client (`internatEnabled` — réutiliser le composant `sidebarNav` n'est pas approprié ici ; charger l'info directement sur cette page, comme le fait déjà `loadAvailableRooms()` implicitement) :
```html
<div x-show="internatEnabled" x-cloak class="border-t border-slate-100 pt-4 mt-4">
    <h3 class="text-xs font-semibold text-slate-500 uppercase tracking-wider mb-3">Régime &amp; Hébergement</h3>

    <div>
        <label class="form-label">Régime</label>
        <select-field model="form.boardingStatus" x-on:change="form.boardingStatus !== 'Externe' && loadAvailableRooms()"
            options-expr="[{value:'Externe', label:'Externe'},{value:'DemiPensionnaire', label:'Demi-pensionnaire'},{value:'Interne', label:'Interne'}]" />
    </div>

    <div x-show="form.boardingStatus !== 'Externe'" x-cloak class="mt-3">
        <label class="form-label form-label-required">Chambre</label>
        <select-field model="form.roomId" required="true"
            options-expr="availableRooms.map(r => ({value: r.roomId, label: r.roomName + ' — ' + r.buildingName + ' (' + (r.capacity - r.occupantsCount) + ' place(s) libre(s))'}))" />
    </div>

    <div x-show="form.boardingStatus !== 'Externe'" x-cloak class="flex items-start gap-2 mt-3">
        <input id="enr-boarding-fee" type="checkbox" x-model="form.includeBoardingFee" class="checkbox-field mt-1">
        <label for="enr-boarding-fee" class="text-sm text-slate-700">Ajouter le frais de pension à la fiche financière</label>
    </div>
</div>
```
Définir `internatEnabled` dans le composant Alpine de cette page (charger `GET /schools/current/settings` si ce n'est pas déjà fait ailleurs sur cette page — vérifier avant de dupliquer l'appel).

- [ ] **Step 4: Vérifier au navigateur — parcours complet**

Skill `run` : avec le module Internat désactivé, vérifier que la section n'apparaît pas et qu'une inscription se comporte comme avant (non-régression). Avec le module activé : inscrire un nouvel élève en régime Interne avec une chambre et la case pension cochée, ouvrir le reçu généré, vérifier la ligne « Pension » dans le détail des frais.

- [ ] **Step 5: Commit**

```bash
git add src/SamaEcole.Web/wwwroot/js/enrollments.js src/SamaEcole.Web/Views/Enrollments/Index.cshtml
git commit -m "feat(internat): section Régime & Hébergement dans le formulaire d'inscription"
```

---

### Task 18: Vérification finale et documentation

**Files:**
- Modify: `ACTIVE_CONTEXT.md` (nouvelle section « Module Internat »)

**Interfaces:**
- Consumes: l'ensemble des tasks précédentes.

- [ ] **Step 1: Suite complète**

Run: `dotnet build`
Expected: 0 erreur.

Ne PAS lancer `dotnet test` sans que l'utilisateur ne le demande explicitement (mémoire projet : rejeté 3 fois le 17/09/2026) — proposer de le faire et attendre confirmation, ou le laisser à l'utilisateur.

- [ ] **Step 2: Vérification manuelle de bout en bout (skill `run`)**

Reprendre le parcours complet du Task 16 Step 5 et du Task 17 Step 4 sur une instance propre (PostgreSQL réel), en conditions réelles : activer Internat → créer Pavillon/Dortoir → inscrire un élève Interne avec pension → vérifier le reçu → réaffecter → libérer → vérifier la persistance de la ligne de pension.

- [ ] **Step 3: Mettre à jour `ACTIVE_CONTEXT.md`**

Ajouter, à la suite de la section « Inventaire » (même gabarit : ce qui est livré, arbitrages actés), une section « Module Internat (livré) » résumant : réutilisation de Building/Room et FeeCategory/ClassFee, comptage de capacité simple (pas d'entité Bed), régime porté par Enrollment (portée annuelle), pension via FeeCategory.IsBoardingFee sans retrait à la libération, écran `/internat`, rôles Directeur/Secretariat/Surveillant. Mettre à jour la ligne « Dernière mise à jour » en tête de fichier.

- [ ] **Step 4: Commit**

```bash
git add ACTIVE_CONTEXT.md
git commit -m "docs(internat): consigner la livraison du module Internat dans ACTIVE_CONTEXT.md"
```

---

## Self-Review

**Couverture de la spec :** §3 (modèle de données) → Tasks 1-2 ; §4 (sécurité) → Tasks 4, 7, 8 ; §5.1 (CreateEnrollment) → Task 4 ; §5.2 (ChangeBoardingAssignment) → Task 7 ; §5.3 (dashboard) → Task 5 ; §5.4 (recherche) → Task 6 ; §6.1 (navigation) → Tasks 10-11 ; §6.2 (écran + modale) → Tasks 15-16 ; §6.3 (fiche élève + inscription) → Tasks 14, 17 ; §7 (tests) → intégrés à chaque task (TDD) + Task 8 (gate fonctionnel) ; exposition de `FeeCategory.IsBoardingFee` (non explicite dans la spec, gap comblé pendant ce plan) → Task 9, 12.

**Cohérence des types :** `BoardingStatus` (Task 1) utilisé identiquement dans `Enrollment`, `CreateEnrollmentCommand`, `ChangeBoardingAssignmentCommand`, `BoardableStudentDto` (string sérialisé), `AcademicHistoryEntryDto`. `BoardingFeeLineBuilder.BuildMissingBoardingLinesAsync` (Task 3) a la même signature dans ses deux appelants (Tasks 4 et 7). `EnrollmentBoardingDto`/`InternatDashboardDto`/`BoardableStudentDto` (Tasks 5-7) sont consommés tels quels par `InternatController` (Task 8) sans transformation.

**Point corrigé pendant la revue :** `ChangeBoardingAssignmentCommandHandler` (Task 7) renvoyait initialement un `RowVersion` fixé à `0` — corrigé en note d'implémentation explicite dans le Step 3 du Task 7 (relire le xmin après `SaveChangesAsync`), nécessaire pour que Task 16 (modale, changements successifs) fonctionne sans faux 409. `BoardableStudentDto` (Task 6) ne portait initialement pas de `RowVersion` alors que Task 16 en a besoin pour appeler `ChangeBoardingAssignmentCommand` — signalé explicitement dans le Step 2 du Task 16 avec l'ajout à faire sur Task 6 avant de continuer.

**Aucun placeholder textuel** ("TBD", "gérer les erreurs", etc.) ne subsiste — les seuls points laissés à vérifier PENDANT l'implémentation (noms exacts de fixtures de test existantes, classes CSS utilitaires réelles) sont explicitement signalés comme « à lire d'abord dans le fichier de référence cité », jamais comme un flou de conception.
