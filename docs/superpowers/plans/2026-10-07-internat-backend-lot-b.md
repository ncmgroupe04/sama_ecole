# Internat — Lot B (Pavillons, chambres, lits en CQRS) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Exposer la gestion des pavillons (`Dormitory`), chambres (`DormitoryRoom`) et lits (`Bed`) : commandes, requêtes, DTO avec statut d'occupation calculé, endpoints `/api/v1/boarding/...`, corbeille/restauration, et masquage des anciennes salles `Dortoir` dans la gestion classique des salles.

**Architecture:** `BoardingController` mince (règle #8) au-dessus de handlers MediatR dans `SamaEcole.Application/Boarding/`. Le statut `Occupied` d'un lit est **projeté** (jamais stocké) depuis les séjours actifs. La suppression suit le contrat soft-delete existant (`SoftDeleteLifecycle`) et refuse en 409 `RESOURCE_IN_USE` tant qu'un enfant vivant existe.

**Tech Stack:** .NET 9 (`net9.0`), EF Core + Npgsql, MediatR, FluentValidation, xUnit + FluentAssertions + Testcontainers (PostgreSQL 16).

**Spec:** `docs/superpowers/specs/2026-10-06-internat-backend-and-profile-isolation-design.md` §2.2, §3.1–3.3, §3.7, §5, §6.1, §11 (Q1, Q2, Q7). **Dépend du lot A** (PR #61 : tables `dormitories`, `dormitory_rooms`, `beds`, `boarding_enrollments`).

## Global Constraints

- Contrôleur sans logique métier (AGENTS.md #8) ; `SchoolId` jamais dans le corps, lu via `ITenantProvider` (#10).
- Toute suppression est un soft delete (`SoftDelete(actorId)`), jamais physique (#6) ; verrou optimiste `xmin` via `SetOriginalConcurrencyToken` (#5) ; un doublon concurrent remonte en 409 par `SaveChangesAsync` (violation d'index unique → `ConcurrencyConflictException`).
- Identités réutilisables : `SoftDeleteLifecycle.EnsureNoArchivedIdentityAsync` à la création → 409 `ARCHIVED_ENTITY_EXISTS` ; `SoftDeleteLifecycle.RestoreAsync` à la restauration → 404 / 409 `ACTIVE_ENTITY_CONFLICT` ; parent supprimé → 409 `PARENT_ENTITY_ARCHIVED`.
- **Nouveau** code d'erreur 409 `RESOURCE_IN_USE` (`BusinessRuleException` + code), sur le modèle de `ACTIVE_ENTITY_CONFLICT`.
- `[RequireModule(SchoolModule.Internat)]` sur tout le contrôleur (403 `MODULE_DISABLED`).
- Rôles : lecture `Directeur,Secretariat,Surveillant` ; écriture `Directeur,Secretariat` (spec §5.1).
- `DormitoryGender.Mixte` est refusé à la création et à la modification (422) — il n'existe que pour la reprise de données.
- Aucune écriture sur `enrollments.BoardingStatus/RoomId` ni sur `boarding_enrollments` dans ce lot (lecture seule pour l'occupation).
- Prérequis d'exécution : Docker Desktop démarré ; **toujours `dotnet build` avant `dotnet test --no-build`** (binaires périmés → faux échecs `PendingModelChangesWarning`) ; ne pas lancer les trois projets de tests en parallèle (contention Npgsql).
- Un commit par tâche, message en français, terminé par `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.

## Points d'attention (à lire avant d'exécuter)

1. **Risque de séquencement A → C.** Après ce lot, un dortoir créé par la nouvelle API est **invisible** de l'ancien écran `/internat` (qui lit `Room` de type `Dortoir`), et — par la décision Q1 de cette tâche 8 — il devient impossible de créer un nouveau `Dortoir` par `/infrastructures`. Entre le lot B et le lot C (bascule des lectures), un internat en production ne peut donc plus créer de dortoir exploitable par l'ancien écran. **Ne pas livrer le lot B seul en production** : empiler les PR B et C et les fusionner dans la même fenêtre de livraison. Par ailleurs, la reprise du lot A est un instantané : les affectations faites avec l'ancien écran après la migration du lot A n'apparaissent pas dans les nouvelles tables ; le lot C devra embarquer une **réconciliation idempotente** (séjours manquants et lits déplacés).
2. **Suppression stricte (spec §3.7 simplifiée).** La spec dit « une chambre contenant un lit occupé ne se supprime pas ». Ce plan applique la règle plus stricte déjà utilisée par `DeleteBuilding` : on ne supprime un pavillon que sans chambre vivante, une chambre que sans lit vivant, un lit que s'il n'est pas occupé. Cela évite une cascade de suppression/restauration ambiguë. À assouplir plus tard si l'ergonomie l'exige.
3. **Surveillant en test fonctionnel.** `AuthApiFactory` ne sème pas de compte `Surveillant` et ses 4 comptes sont partagés par ~87 usages : le compte est créé dans la classe de test (tâche 7), sans élargir le jeu partagé.

## Carte des fichiers

| Fichier | Rôle |
|---|---|
| `src/SamaEcole.Application/Common/SoftDelete/SoftDeleteLifecycle.cs` (modifier) | Constante `ResourceInUse` |
| `src/SamaEcole.Application/Boarding/BoardingDtos.cs`, `BoardingOccupancy.cs` (créer) | DTO + projection d'occupation |
| `src/SamaEcole.Application/Boarding/Dormitories/*` (créer) | Create/Update/Delete/Restore, List, Get, Deleted |
| `src/SamaEcole.Application/Boarding/Rooms/*` (créer) | Create (+lits)/Update/Delete/Restore, Deleted |
| `src/SamaEcole.Application/Boarding/Beds/*` (créer) | Create/ChangeStatus/Delete/Restore, Deleted |
| `src/SamaEcole.Web/Controllers/BoardingController.cs` (créer) | Routes `/api/v1/boarding/...` |
| `src/SamaEcole.Application/Buildings/Queries/GetBuildingsWithRooms/GetBuildingsWithRoomsQueryHandler.cs`, `Rooms/Commands/{Create,Update}Room/*Validator.cs` (modifier) | Masquage Q1 |
| `tests/SamaEcole.UnitTests/Boarding/*`, `tests/SamaEcole.IntegrationTests/Boarding/*`, `tests/SamaEcole.FunctionalTests/Boarding/*` (créer) | Tests |
| `docs/Volume_4_API_Design.md`, `ACTIVE_CONTEXT.md` (modifier) | Documentation |

---

### Task 1 : Code d'erreur, DTO et projection d'occupation

**Files:** Modify `SoftDeleteLifecycle.cs` ; Create `Boarding/BoardingDtos.cs`, `Boarding/BoardingOccupancy.cs` ; Test `tests/SamaEcole.UnitTests/Boarding/BoardingOccupancyTests.cs`.

**Interfaces — Produces:** `SoftDeleteLifecycle.ResourceInUse`, les DTO ci-dessous, `BoardingOccupancy.StatusOf(BedStatus stored, bool hasActiveStay)`, `BoardingOccupancy.Rate(int occupied, int capacity, int maintenance)`, `BoardingOccupancy.ActiveBedIds(IApplicationDbContext)`.

- [ ] **Step 1 : test (échoue)**

```csharp
using FluentAssertions;
using SamaEcole.Application.Boarding;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Boarding;

public class BoardingOccupancyTests
{
    [Theory]
    [InlineData(BedStatus.Available, false, BedStatus.Available)]
    [InlineData(BedStatus.Available, true, BedStatus.Occupied)]
    [InlineData(BedStatus.Maintenance, false, BedStatus.Maintenance)]
    [InlineData(BedStatus.Maintenance, true, BedStatus.Maintenance)] // la maintenance prime, jamais deux états
    public void Status_Is_Projected_From_The_Stored_Status_And_The_Active_Stay(
        BedStatus stored, bool hasActiveStay, BedStatus expected)
        => BoardingOccupancy.StatusOf(stored, hasActiveStay).Should().Be(expected);

    [Theory]
    [InlineData(0, 0, 0, 0.0)]
    [InlineData(5, 10, 0, 0.5)]
    [InlineData(5, 10, 2, 0.625)]  // les lits en maintenance ne comptent pas dans la capacité utilisable
    [InlineData(0, 4, 4, 0.0)]     // tout en maintenance : jamais de division par zéro
    public void Rate_Excludes_Beds_In_Maintenance(int occupied, int capacity, int maintenance, double expected)
        => ((double)BoardingOccupancy.Rate(occupied, capacity, maintenance)).Should().BeApproximately(expected, 1e-9);
}
```

- [ ] **Step 2 :** `dotnet test tests/SamaEcole.UnitTests --filter "FullyQualifiedName~BoardingOccupancyTests"` — Expected: FAIL (type absent).

- [ ] **Step 3 : implémentation**

`SoftDeleteLifecycle.cs` : ajouter sous les deux constantes existantes
```csharp
    /// <summary>409 : la ressource a encore des éléments vivants dépendants (ex. un pavillon avec des chambres, un lit occupé).</summary>
    public const string ResourceInUse = "RESOURCE_IN_USE";
```

`Boarding/BoardingOccupancy.cs`
```csharp
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding;

/// <summary>
/// Occupation d'un lit : PROJETÉE depuis les séjours actifs, jamais stockée (spec N2). Une seule définition de
/// « lit occupé » pour toutes les lectures et gardes de suppression.
/// </summary>
public static class BoardingOccupancy
{
    /// <summary>Identifiants des lits tenus par un séjour actif (le Global Query Filter écarte déjà les supprimés).</summary>
    public static IQueryable<Guid> ActiveBedIds(IApplicationDbContext dbContext) =>
        dbContext.BoardingEnrollments
            .Where(b => b.IsActive && b.BedId != null)
            .Select(b => b.BedId!.Value);

    public static BedStatus StatusOf(BedStatus stored, bool hasActiveStay) =>
        stored == BedStatus.Maintenance ? BedStatus.Maintenance
        : hasActiveStay ? BedStatus.Occupied
        : BedStatus.Available;

    /// <summary>Taux d'occupation (0..1) sur les lits UTILISABLES (hors maintenance).</summary>
    public static decimal Rate(int occupied, int capacity, int maintenance)
    {
        var usable = capacity - maintenance;
        return usable <= 0 ? 0m : Math.Round((decimal)occupied / usable, 4);
    }
}
```

`Boarding/BoardingDtos.cs`
```csharp
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding;

/// <summary>Résultat d'écriture d'un pavillon. Le nom du surveillant est celui du compte lié s'il existe.</summary>
public record DormitoryDto(
    Guid Id, string Name, DormitoryGender Gender, string? SupervisorName, string? SupervisorPhone,
    Guid? SupervisorUserId, string? Notes, uint RowVersion);

/// <summary>Ligne de liste : capacités DÉRIVÉES des lits (spec N1), jamais saisies.</summary>
public record DormitorySummaryDto(
    Guid Id, string Name, DormitoryGender Gender, string? SupervisorName, string? SupervisorPhone,
    Guid? SupervisorUserId, int RoomCount, int Capacity, int OccupiedBeds, int MaintenanceBeds,
    decimal OccupancyRate, uint RowVersion);

public record BedDto(
    Guid Id, Guid DormitoryRoomId, int BedNumber, BedStatus Status,
    Guid? OccupantBoarderId, string? OccupantName, uint RowVersion);

public record DormitoryRoomDto(
    Guid Id, Guid DormitoryId, string Name, int Capacity, IReadOnlyList<BedDto> Beds, uint RowVersion);

public record DormitoryDetailDto(
    Guid Id, string Name, DormitoryGender Gender, string? SupervisorName, string? SupervisorPhone,
    Guid? SupervisorUserId, string? Notes, int Capacity, int OccupiedBeds, uint RowVersion,
    IReadOnlyList<DormitoryRoomDto> Rooms);

/// <summary>Résultat d'une chambre créée, avec ses lits générés.</summary>
public record DormitoryRoomResult(Guid Id, Guid DormitoryId, string Name, IReadOnlyList<BedDto> Beds, uint RowVersion);

public record DeletedBoardingItemDto(Guid Id, string Name, Guid? ParentId, DateTimeOffset? DeletedAt);
```

- [ ] **Step 4 :** relancer le test — Expected: PASS.
- [ ] **Step 5 : commit** `feat(internat): code RESOURCE_IN_USE, DTO et projection d'occupation des lits`.

---

### Task 2 : Pavillons — commandes

**Files:** Create `Boarding/Dormitories/{CreateDormitory,UpdateDormitory,DeleteDormitory,RestoreDormitory}Command*.cs` ; Test `tests/SamaEcole.UnitTests/Boarding/DormitoryCommandValidatorTests.cs`, `tests/SamaEcole.IntegrationTests/Boarding/BoardingTestKit.cs`, `DormitoryCommandsTests.cs`.

**Interfaces — Consumes:** Task 1. **Produces:**
```csharp
public record CreateDormitoryCommand : IRequest<DormitoryDto>
{ public required string Name {get;init;} public DormitoryGender Gender {get;init;} public string? SupervisorName {get;init;}
  public string? SupervisorPhone {get;init;} public Guid? SupervisorUserId {get;init;} public string? Notes {get;init;} }
public record UpdateDormitoryCommand(Guid Id, string Name, DormitoryGender Gender, string? SupervisorName,
    string? SupervisorPhone, Guid? SupervisorUserId, string? Notes, uint RowVersion) : IRequest<DormitoryDto>;
public record DeleteDormitoryCommand(Guid Id, uint RowVersion) : IRequest<Unit>;
public record RestoreDormitoryCommand(Guid Id) : IRequest<Unit>;
```

- [ ] **Step 1 : tests unitaires du validateur (échouent)**

```csharp
using FluentAssertions;
using SamaEcole.Application.Boarding.Dormitories.CreateDormitory;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Boarding;

public class DormitoryCommandValidatorTests
{
    private readonly CreateDormitoryCommandValidator _validator = new();

    private static CreateDormitoryCommand Valid() => new()
    {
        Name = "Pavillon Oustaz Ahmad", Gender = DormitoryGender.Garcons,
        SupervisorName = "M. Ba", SupervisorPhone = "77 123 45 67"
    };

    [Fact] public void Valid_Dormitory_Passes() => _validator.Validate(Valid()).IsValid.Should().BeTrue();

    [Fact] public void Empty_Name_Fails() => _validator.Validate(Valid() with { Name = "" }).IsValid.Should().BeFalse();

    [Fact] public void Name_Longer_Than_100_Fails() =>
        _validator.Validate(Valid() with { Name = new string('x', 101) }).IsValid.Should().BeFalse();

    [Fact] public void Mixte_Is_Refused_Because_It_Only_Exists_For_Data_Migration() =>
        _validator.Validate(Valid() with { Gender = DormitoryGender.Mixte }).IsValid.Should().BeFalse();

    [Fact] public void Unknown_Gender_Value_Fails() =>
        _validator.Validate(Valid() with { Gender = (DormitoryGender)99 }).IsValid.Should().BeFalse();

    [Fact] public void Invalid_Supervisor_Phone_Fails() =>
        _validator.Validate(Valid() with { SupervisorPhone = "abc" }).IsValid.Should().BeFalse();

    [Fact] public void Html_In_Name_Fails() =>
        _validator.Validate(Valid() with { Name = "<b>x</b>" }).IsValid.Should().BeFalse();
}
```

- [ ] **Step 2 : implémentation des validateurs.** `CreateDormitoryCommandValidator` (namespace `SamaEcole.Application.Boarding.Dormitories.CreateDormitory`) :

```csharp
using FluentValidation;
using SamaEcole.Application.Common.Validation;
using SamaEcole.Domain.Enums;

public class CreateDormitoryCommandValidator : AbstractValidator<CreateDormitoryCommand>
{
    public CreateDormitoryCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).NoHtml();
        RuleFor(x => x.Gender).IsInEnum().WithMessage("Genre de pavillon invalide.")
            .NotEqual(DormitoryGender.Mixte).WithMessage("Choisissez Garçons ou Filles : « Mixte » n'existe que pour les pavillons repris de l'ancien module.");
        RuleFor(x => x.SupervisorName).MaximumLength(150).NoHtml();
        RuleFor(x => x.SupervisorPhone).MaximumLength(30).NoHtml().MustBeValidSenegalPhone();
        RuleFor(x => x.Notes).MaximumLength(1000).NoHtml();
    }
}
```
`UpdateDormitoryCommandValidator` : mêmes règles sur `UpdateDormitoryCommand` + `RuleFor(x => x.Id).NotEmpty()`. `DeleteDormitoryCommandValidator` : `RuleFor(x => x.Id).NotEmpty()`.

> `MustBeValidSenegalPhone` accepte `null`/vide comme les autres validateurs (champ optionnel) — vérifier à l'exécution du test `Valid_Dormitory_Passes` avec `SupervisorPhone = null`.

- [ ] **Step 3 : kit de test d'intégration** `tests/SamaEcole.IntegrationTests/Boarding/BoardingTestKit.cs` :

```csharp
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.IntegrationTests.Boarding;

internal sealed class BoardingTenant(Guid? schoolId) : ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}
```

- [ ] **Step 4 : tests d'intégration des commandes (échouent)** `DormitoryCommandsTests.cs` — `[Trait("Category","MultiTenant")]`, `RlsTestDatabase _db`, deux écoles A/B semées (`School`), un utilisateur `Surveillant` actif A (`User { SchoolId = EcoleA, Role = Role.Surveillant, Status = EntityStatus.Active, Email, PasswordHash = "x", FullName = "Surv. A" }`) et un `Directeur` A. Tests :

```csharp
[Fact] public async Task Creating_A_Dormitory_Persists_It_For_The_Current_School_Only()
[Fact] public async Task Creating_A_Name_That_Only_Exists_Deleted_Returns_ARCHIVED_ENTITY_EXISTS()
[Fact] public async Task The_Same_Name_Can_Exist_In_Two_Schools()
[Fact] public async Task A_Supervisor_Account_Must_Be_An_Active_Surveillant_Of_The_Same_School()   // Directeur A → ValidationException ; Surveillant de l'école B → ValidationException ; Surveillant A → OK
[Fact] public async Task When_A_Supervisor_Account_Is_Linked_The_Free_Text_Name_Is_Not_Stored()    // SupervisorName du DTO = FullName du compte ; colonne SupervisorName = null
[Fact] public async Task Updating_With_A_Stale_RowVersion_Is_Rejected_With_A_Concurrency_Conflict()
[Fact] public async Task Changing_The_Gender_Of_A_Dormitory_That_Has_Active_Boarders_Is_Rejected() // séjour actif sur un lit du pavillon → ValidationException « Genre »
[Fact] public async Task Deleting_A_Dormitory_With_Live_Rooms_Returns_RESOURCE_IN_USE()
[Fact] public async Task Deleting_An_Empty_Dormitory_Then_Restoring_It_Makes_It_Visible_Again()
[Fact] public async Task Restoring_Fails_With_ACTIVE_ENTITY_CONFLICT_When_The_Name_Was_Taken()
[Fact] public async Task A_Dormitory_Of_Another_School_Is_Not_Found()                              // Update/Delete/Restore → KeyNotFoundException
```
Chaque test construit les handlers à la main : `new CreateDormitoryCommandHandler(ctx, new BoardingTenant(EcoleA))`, `new DeleteDormitoryCommandHandler(ctx, new TestCurrentUser(DirecteurId))`, avec `await using var ctx = _db.NewAppContext(EcoleA)` ; le `RowVersion` utilisé est celui retourné par le `DormitoryDto` précédent. Pour « séjour actif », semer un `Dormitory`→`DormitoryRoom`→`Bed`→`BoardingEnrollment` (`IsActive = true`, `BedId`) avec le contexte propriétaire (`StudentId`/`EnrollmentId` d'un `Student`/`Enrollment` semés comme dans `BoardingSchemaIsolationTests`).

- [ ] **Step 5 : lancer — doivent échouer** (`CreateDormitoryCommandHandler` absent).

- [ ] **Step 6 : implémentation des handlers**

`CreateDormitoryCommandHandler` :
```csharp
public class CreateDormitoryCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<CreateDormitoryCommand, DormitoryDto>
{
    public async Task<DormitoryDto> Handle(CreateDormitoryCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        await DormitorySupervisor.EnsureValidAsync(dbContext, schoolId, request.SupervisorUserId, cancellationToken);

        var name = request.Name.Trim();
        await SoftDeleteLifecycle.EnsureNoArchivedIdentityAsync(
            dbContext.Dormitories, schoolId, d => d.Name == name, $"Un pavillon « {name} »", cancellationToken);

        var dormitory = new Dormitory
        {
            SchoolId = schoolId, Name = name, Gender = request.Gender,
            // Compte lié → le nom vient du compte à la lecture : on ne stocke pas une copie qui divergerait.
            SupervisorName = request.SupervisorUserId is null ? request.SupervisorName?.Trim() : null,
            SupervisorPhone = request.SupervisorPhone?.Trim(), SupervisorUserId = request.SupervisorUserId,
            Notes = request.Notes?.Trim()
        };
        dbContext.Dormitories.Add(dormitory);
        await dbContext.SaveChangesAsync(cancellationToken);   // doublon concurrent → ConcurrencyConflictException (409)

        return await DormitoryReader.GetDtoAsync(dbContext, dormitory.Id, cancellationToken);
    }
}
```
`DormitorySupervisor` (même dossier) :
```csharp
internal static class DormitorySupervisor
{
    public static async Task EnsureValidAsync(IApplicationDbContext dbContext, Guid schoolId, Guid? userId, CancellationToken ct)
    {
        if (userId is null) return;
        var ok = await dbContext.Users.AsNoTracking().AnyAsync(u =>
            u.Id == userId && u.SchoolId == schoolId && u.Role == Role.Surveillant && u.Status == EntityStatus.Active, ct);
        if (!ok)
        {
            throw new ValidationException([new ValidationFailure(nameof(CreateDormitoryCommand.SupervisorUserId),
                "Le compte indiqué n'est pas un Surveillant actif de votre établissement.")]);
        }
    }
}
```
`DormitoryReader.GetDtoAsync` (même dossier) projette un `DormitoryDto` pour un `Id` : `SupervisorName = d.SupervisorUserId != null ? dbContext.Users.Where(u => u.Id == d.SupervisorUserId).Select(u => u.FullName).FirstOrDefault() : d.SupervisorName`, `RowVersion = EF.Property<uint>(d, "xmin")`, `AsNoTracking()`, `FirstAsync`.

`UpdateDormitoryCommandHandler` : charge `dbContext.Dormitories.FirstOrDefaultAsync(d => d.Id == request.Id)` (→ `KeyNotFoundException($"Pavillon {id} introuvable.")`), `SetOriginalConcurrencyToken(dormitory, request.RowVersion)`, `DormitorySupervisor.EnsureValidAsync`, **garde de genre** : si `request.Gender != dormitory.Gender` et qu'un séjour actif tient un lit du pavillon
```csharp
var hasBoarders = await (from b in dbContext.Beds
                         join r in dbContext.DormitoryRooms on b.DormitoryRoomId equals r.Id
                         where r.DormitoryId == dormitory.Id
                         select b.Id)
    .AnyAsync(id => BoardingOccupancy.ActiveBedIds(dbContext).Contains(id), cancellationToken);
```
→ `ValidationException` sur `Gender` (« Ce pavillon héberge des pensionnaires : changez-les de pavillon avant de modifier son genre. »). Puis mise à jour des champs (même règle `SupervisorName` null si compte lié), `SaveChangesAsync`, `DormitoryReader.GetDtoAsync`.

`DeleteDormitoryCommandHandler(IApplicationDbContext, ICurrentUserService)` : charge ou 404 ; `if (await dbContext.DormitoryRooms.AnyAsync(r => r.DormitoryId == request.Id, ct)) throw new BusinessRuleException("Impossible de supprimer : ce pavillon possède des chambres rattachées. Supprimez d'abord ses chambres.", SoftDeleteLifecycle.ResourceInUse);` ; `SetOriginalConcurrencyToken` ; `dormitory.SoftDelete(actorId.ToString())` ; `SaveChangesAsync`.

`RestoreDormitoryCommandHandler(IApplicationDbContext, ITenantProvider)` : `SoftDeleteLifecycle.RestoreAsync(dbContext, dbContext.Dormitories, schoolId, request.Id, "Un pavillon", d => other => other.Name == d.Name, null, ct)`.

- [ ] **Step 7 :** `dotnet build` puis `dotnet test` des deux classes — Expected: PASS.
- [ ] **Step 8 : commit** `feat(internat): commandes de gestion des pavillons`.

---

### Task 3 : Chambres — commandes

**Files:** Create `Boarding/Rooms/{CreateDormitoryRoom,UpdateDormitoryRoom,DeleteDormitoryRoom,RestoreDormitoryRoom}*.cs` ; Test `tests/SamaEcole.UnitTests/Boarding/DormitoryRoomCommandValidatorTests.cs`, `tests/SamaEcole.IntegrationTests/Boarding/DormitoryRoomCommandsTests.cs`.

**Interfaces — Produces:**
```csharp
public record CreateDormitoryRoomCommand : IRequest<DormitoryRoomResult>
{ public Guid DormitoryId {get;init;} public required string Name {get;init;} public int BedCount {get;init;} }
public record UpdateDormitoryRoomCommand(Guid Id, string Name, uint RowVersion) : IRequest<DormitoryRoomResult>;
public record DeleteDormitoryRoomCommand(Guid Id, uint RowVersion) : IRequest<Unit>;
public record RestoreDormitoryRoomCommand(Guid Id) : IRequest<Unit>;
```

- [ ] **Step 1 : tests unitaires (échouent)** — `CreateDormitoryRoomCommandValidator` : nom requis ≤ 100 sans HTML ; `BedCount` entre 1 et 40 (`[Theory]` 0, 41, -1 invalides ; 1, 40 valides) ; `DormitoryId` non vide.

- [ ] **Step 2 : tests d'intégration (échouent)** : création → chambre + `BedCount` lits numérotés 1..N, tous `Available` (`Status` du `BedDto`) ; pavillon inexistant/autre école → `ValidationException` sur `DormitoryId` ; nom archivé → `ARCHIVED_ENTITY_EXISTS` et **aucun lit créé** ; même nom dans deux pavillons autorisé ; renommage avec jeton périmé → conflit ; suppression d'une chambre avec lits vivants → `RESOURCE_IN_USE` ; suppression d'une chambre dont les lits ont été supprimés puis restauration OK ; restauration avec pavillon supprimé → `PARENT_ENTITY_ARCHIVED` ; restauration avec nom repris → `ACTIVE_ENTITY_CONFLICT`.

- [ ] **Step 3 : implémentation**

Validateur de création : `RuleFor(x => x.BedCount).InclusiveBetween(1, 40).WithMessage("Une chambre compte entre 1 et 40 lits.")`.

`CreateDormitoryRoomCommandHandler` :
```csharp
var schoolId = tenantProvider.CurrentSchoolId ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");
if (!await dbContext.Dormitories.AnyAsync(d => d.Id == request.DormitoryId, cancellationToken))
{
    throw new ValidationException([new ValidationFailure(nameof(request.DormitoryId), "Le pavillon indiqué n'existe pas dans votre établissement.")]);
}
var name = request.Name.Trim();
await SoftDeleteLifecycle.EnsureNoArchivedIdentityAsync(
    dbContext.DormitoryRooms, schoolId, r => r.DormitoryId == request.DormitoryId && r.Name == name,
    $"Une chambre « {name} » dans ce pavillon", cancellationToken);

var room = new DormitoryRoom { SchoolId = schoolId, DormitoryId = request.DormitoryId, Name = name };
dbContext.DormitoryRooms.Add(room);
for (var n = 1; n <= request.BedCount; n++)
{
    dbContext.Beds.Add(new Bed { SchoolId = schoolId, DormitoryRoomId = room.Id, BedNumber = n });
}
await dbContext.SaveChangesAsync(cancellationToken);   // une seule transaction : chambre + lits, tout ou rien
return await DormitoryRoomReader.GetResultAsync(dbContext, room.Id, cancellationToken);
```
`DormitoryRoomReader.GetResultAsync(db, roomId, ct)` (même dossier) lit la chambre (`Id`, `DormitoryId`, `Name`, `xmin`) puis ses lits par `BedReader.ListForRoomAsync` (défini juste en dessous : une seule définition de « lit + statut calculé »).

`BedReader.ListForRoomAsync(IApplicationDbContext db, Guid roomId, CancellationToken ct)` (créé ici, `Boarding/Beds/BedReader.cs`) :
```csharp
public static class BedReader
{
    public static async Task<IReadOnlyList<BedDto>> ListForRoomAsync(
        IApplicationDbContext db, Guid roomId, CancellationToken ct)
    {
        var activeBeds = BoardingOccupancy.ActiveBedIds(db);

        var rows = await (from b in db.Beds.AsNoTracking()
                          where b.DormitoryRoomId == roomId
                          orderby b.BedNumber
                          select new
                          {
                              b.Id, b.DormitoryRoomId, b.BedNumber, b.Status,
                              Occupied = activeBeds.Contains(b.Id),
                              BoarderId = db.BoardingEnrollments
                                  .Where(be => be.IsActive && be.BedId == b.Id)
                                  .Select(be => (Guid?)be.Id).FirstOrDefault(),
                              OccupantName = db.BoardingEnrollments
                                  .Where(be => be.IsActive && be.BedId == b.Id)
                                  .Select(be => db.Students.Where(s => s.Id == be.StudentId)
                                      .Select(s => s.FullName).FirstOrDefault())
                                  .FirstOrDefault(),
                              RowVersion = EF.Property<uint>(b, "xmin")
                          }).ToListAsync(ct);

        return rows.Select(x => new BedDto(
            x.Id, x.DormitoryRoomId, x.BedNumber, BoardingOccupancy.StatusOf(x.Status, x.Occupied),
            x.BoarderId, x.OccupantName, x.RowVersion)).ToList();
    }
}
```

`UpdateDormitoryRoomCommandHandler` : charge/404, `SetOriginalConcurrencyToken`, renomme, `SaveChangesAsync`, relit. `DeleteDormitoryRoomCommandHandler` : charge/404 ; `if (await dbContext.Beds.AnyAsync(b => b.DormitoryRoomId == request.Id, ct)) throw new BusinessRuleException("Impossible de supprimer : cette chambre possède des lits. Supprimez d'abord ses lits.", SoftDeleteLifecycle.ResourceInUse);` ; soft delete. `RestoreDormitoryRoomCommandHandler` : `SoftDeleteLifecycle.RestoreAsync(..., "Une chambre", r => other => other.DormitoryId == r.DormitoryId && other.Name == r.Name, beforeRestore: pavillon vivant sinon BusinessRuleException("Impossible de restaurer la chambre : son pavillon est supprimé. Restaurez d'abord le pavillon.", "PARENT_ENTITY_ARCHIVED"), ct)`.

- [ ] **Step 4 :** build + tests des deux classes — Expected: PASS.
- [ ] **Step 5 : commit** `feat(internat): commandes de gestion des chambres (création avec génération des lits)`.

---

### Task 4 : Lits — commandes

**Files:** Create `Boarding/Beds/{CreateBed,ChangeBedStatus,DeleteBed,RestoreBed}*.cs` ; Test `tests/SamaEcole.UnitTests/Boarding/BedCommandValidatorTests.cs`, `tests/SamaEcole.IntegrationTests/Boarding/BedCommandsTests.cs`.

**Interfaces — Produces:**
```csharp
public record CreateBedCommand : IRequest<BedDto> { public Guid DormitoryRoomId {get;init;} public int? BedNumber {get;init;} }
public record ChangeBedStatusCommand(Guid Id, BedStatus Status, uint RowVersion) : IRequest<BedDto>;
public record DeleteBedCommand(Guid Id, uint RowVersion) : IRequest<Unit>;
public record RestoreBedCommand(Guid Id) : IRequest<Unit>;
```

- [ ] **Step 1 : tests unitaires (échouent).** `CreateBedCommandValidator` : `DormitoryRoomId` non vide ; `BedNumber` nul ou 1..999. `ChangeBedStatusCommandValidator` : `Status` défini **et** différent de `Occupied` (« Un lit devient occupé par une affectation, pas par un changement de statut. »).

- [ ] **Step 2 : tests d'intégration (échouent)** : numéro automatique = max (lits vivants **et** supprimés de la chambre) + 1, jamais 0 sur chambre vide (→ 1) ; numéro explicite déjà pris → conflit ; numéro explicite pris par un lit supprimé → `ARCHIVED_ENTITY_EXISTS` ; passage en `Maintenance` d'un lit libre OK puis retour `Available` OK ; passage en `Maintenance` d'un lit occupé (séjour actif) → `RESOURCE_IN_USE` ; suppression d'un lit occupé → `RESOURCE_IN_USE` ; suppression d'un lit libre puis restauration OK ; restauration quand le numéro est repris → `ACTIVE_ENTITY_CONFLICT` ; restauration avec chambre supprimée → `PARENT_ENTITY_ARCHIVED` ; lit d'une autre école → `KeyNotFoundException` ; jeton périmé → conflit.

- [ ] **Step 3 : implémentation**

`CreateBedCommandHandler(IApplicationDbContext, ITenantProvider)` :
```csharp
if (!await dbContext.DormitoryRooms.AnyAsync(r => r.Id == request.DormitoryRoomId, ct))
    throw new ValidationException([new ValidationFailure(nameof(request.DormitoryRoomId), "La chambre indiquée n'existe pas dans votre établissement.")]);

int number;
if (request.BedNumber is { } explicitNumber)
{
    number = explicitNumber;
    await SoftDeleteLifecycle.EnsureNoArchivedIdentityAsync(
        dbContext.Beds, schoolId, b => b.DormitoryRoomId == request.DormitoryRoomId && b.BedNumber == number,
        $"Un lit n° {number} dans cette chambre", ct);
}
else
{
    // Max sur les lits vivants ET supprimés : un numéro automatique ne doit jamais retomber sur un lit archivé.
    number = 1 + await dbContext.Beds.IgnoreQueryFilters()
        .Where(b => b.SchoolId == schoolId && b.DormitoryRoomId == request.DormitoryRoomId)
        .Select(b => (int?)b.BedNumber).MaxAsync(ct) ?? 0;
}
var bed = new Bed { SchoolId = schoolId, DormitoryRoomId = request.DormitoryRoomId, BedNumber = number };
dbContext.Beds.Add(bed);
await dbContext.SaveChangesAsync(ct);
return (await BedReader.ListForRoomAsync(dbContext, bed.DormitoryRoomId, ct)).Single(b => b.Id == bed.Id);
```
(`1 + await ... ?? 0` : écrire en deux lignes — `var max = await ... MaxAsync(ct); number = (max ?? 0) + 1;`.)

`ChangeBedStatusCommandHandler` : charge/404 ; `SetOriginalConcurrencyToken` ; si `request.Status == Maintenance` et `await BoardingOccupancy.ActiveBedIds(dbContext).AnyAsync(id => id == bed.Id, ct)` → `BusinessRuleException("Impossible de mettre ce lit en maintenance : il est occupé.", SoftDeleteLifecycle.ResourceInUse)` ; `bed.Status = request.Status` ; `SaveChangesAsync` ; relit via `BedReader`.

`DeleteBedCommandHandler` : charge/404 ; lit occupé → `RESOURCE_IN_USE` (« Impossible de supprimer : ce lit est occupé. ») ; `SetOriginalConcurrencyToken` ; `SoftDelete`. `RestoreBedCommandHandler` : `SoftDeleteLifecycle.RestoreAsync(..., "Un lit", b => other => other.DormitoryRoomId == b.DormitoryRoomId && other.BedNumber == b.BedNumber, beforeRestore: chambre vivante sinon `PARENT_ENTITY_ARCHIVED`, ct)`.

- [ ] **Step 4 :** build + tests — Expected: PASS.
- [ ] **Step 5 : commit** `feat(internat): commandes de gestion des lits (statut, suppression, restauration)`.

---

### Task 5 : Requêtes — liste, détail et corbeilles

**Files:** Create `Boarding/Dormitories/Queries/{ListDormitories,GetDormitory,GetDeletedDormitories}*.cs`, `Boarding/Rooms/GetDeletedDormitoryRoomsQuery.cs`, `Boarding/Beds/GetDeletedBedsQuery.cs` ; Test `tests/SamaEcole.IntegrationTests/Boarding/BoardingQueriesTests.cs`.

**Interfaces — Produces:**
```csharp
public record ListDormitoriesQuery(DormitoryGender? Gender = null) : IRequest<IReadOnlyList<DormitorySummaryDto>>;
public record GetDormitoryQuery(Guid Id) : IRequest<DormitoryDetailDto>;
public record GetDeletedDormitoriesQuery : IRequest<IReadOnlyList<DeletedBoardingItemDto>>;
public record GetDeletedDormitoryRoomsQuery : IRequest<IReadOnlyList<DeletedBoardingItemDto>>;
public record GetDeletedBedsQuery : IRequest<IReadOnlyList<DeletedBoardingItemDto>>;
```

- [ ] **Step 1 : tests d'intégration (échouent).** Jeu de données (contexte propriétaire) : école A avec un pavillon « Garçons » (2 chambres : 101 avec 3 lits dont 1 occupé et 1 en maintenance ; 102 avec 2 lits libres), un pavillon « Filles » vide ; école B avec un pavillon. Assertions : `ListDormitoriesQuery()` pour A → 2 lignes triées par nom, « Garçons » : `RoomCount 2`, `Capacity 5`, `OccupiedBeds 1`, `MaintenanceBeds 1`, `OccupancyRate` = `1/4` = `0.25` ; « Filles » : tout à 0, taux 0 ; filtre `Gender = Filles` → 1 ligne ; **aucune ligne de l'école B** ; `GetDormitoryQuery` → chambres triées, lits triés, statuts `Available/Occupied/Maintenance` corrects, nom de l'occupant renseigné sur le lit occupé ; pavillon d'une autre école → `KeyNotFoundException` ; corbeilles : un élément supprimé de chaque type apparaît, un élément supprimé de l'école B n'apparaît jamais.

- [ ] **Step 2 : implémentation**

`ListDormitoriesQueryHandler` — une requête d'agrégation, pas de N+1 :
```csharp
var activeBeds = BoardingOccupancy.ActiveBedIds(dbContext);
var rows = await dbContext.Dormitories.AsNoTracking()
    .Where(d => request.Gender == null || d.Gender == request.Gender)
    .OrderBy(d => d.Name)
    .Select(d => new
    {
        d.Id, d.Name, d.Gender, d.SupervisorPhone, d.SupervisorUserId, d.SupervisorName,
        Supervisor = dbContext.Users.Where(u => u.Id == d.SupervisorUserId).Select(u => u.FullName).FirstOrDefault(),
        RoomCount = dbContext.DormitoryRooms.Count(r => r.DormitoryId == d.Id),
        Beds = (from b in dbContext.Beds
                join r in dbContext.DormitoryRooms on b.DormitoryRoomId equals r.Id
                where r.DormitoryId == d.Id select b),
        RowVersion = EF.Property<uint>(d, "xmin")
    })
    .Select(x => new
    {
        x.Id, x.Name, x.Gender, x.SupervisorPhone, x.SupervisorUserId, x.RoomCount, x.RowVersion,
        SupervisorName = x.SupervisorUserId != null ? x.Supervisor : x.SupervisorName,
        Capacity = x.Beds.Count(),
        Maintenance = x.Beds.Count(b => b.Status == BedStatus.Maintenance),
        Occupied = x.Beds.Count(b => b.Status != BedStatus.Maintenance && activeBeds.Contains(b.Id))
    })
    .ToListAsync(cancellationToken);
return rows.Select(r => new DormitorySummaryDto(r.Id, r.Name, r.Gender, r.SupervisorName, r.SupervisorPhone,
    r.SupervisorUserId, r.RoomCount, r.Capacity, r.Occupied, r.Maintenance,
    BoardingOccupancy.Rate(r.Occupied, r.Capacity, r.Maintenance), r.RowVersion)).ToList();
```
(Si EF refuse de traduire le double `Select`/le groupement de lits, scinder en trois requêtes simples — pavillons, comptes de chambres par pavillon, lits par pavillon — et assembler en mémoire ; la suite de tests ci-dessus est l'arbitre.)

`GetDormitoryQueryHandler` : `DormitoryReader`-style lecture du pavillon (404 sinon), ses chambres (`dbContext.DormitoryRooms` triées par nom), leurs lits via `BedReader.ListForRoomAsync` (une requête par chambre — une dizaine au plus, acceptable ; commenter pourquoi), `Capacity` = total des lits, `OccupiedBeds` = lits au statut `Occupied`.

Corbeilles : trois handlers qui délèguent à `SoftDeleteLifecycle.ListDeletedAsync(dbContext.Dormitories|DormitoryRooms|Beds, schoolId, e => new DeletedBoardingItemDto(e.Id, e.Name, parentId, e.DeletedAt), ct)`. Les lits n'ont pas de `Name` : projeter `"Lit " + e.BedNumber`.

- [ ] **Step 3 :** build + tests — Expected: PASS.
- [ ] **Step 4 : commit** `feat(internat): requêtes de liste, détail et corbeille des pavillons, chambres et lits`.

---

### Task 6 : `BoardingController`

**Files:** Create `src/SamaEcole.Web/Controllers/BoardingController.cs`.

**Interfaces — Consumes:** commandes et requêtes des tâches 2 à 5.

- [ ] **Step 1 : contrôleur**

```csharp
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SamaEcole.Application.Boarding;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.Authorization;
// + using des dossiers Boarding/Dormitories, Rooms, Beds

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Module Internat, modèle Pavillon/Lit (spec 2026-10-06 §6). Contrôleur mince : aucune logique métier ici
/// (AGENTS.md règle #8). Verrouillé par [RequireModule] — 403 MODULE_DISABLED si l'école n'a pas activé l'Internat.
/// Routes PLATES (/dormitories, /rooms, /beds), comme /buildings et /rooms (pas d'imbrication d'URL).
/// </summary>
[ApiController]
[Route("api/v1/boarding")]
[Authorize]
[RequireModule(SchoolModule.Internat)]
public class BoardingController(ISender mediator) : ControllerBase
{
    private const string ReadRoles = "Directeur,Secretariat,Surveillant";
    private const string ManageRoles = "Directeur,Secretariat";

    public record CreateDormitoryRequest(string Name, DormitoryGender Gender, string? SupervisorName,
        string? SupervisorPhone, Guid? SupervisorUserId, string? Notes);
    public record UpdateDormitoryRequest(string Name, DormitoryGender Gender, string? SupervisorName,
        string? SupervisorPhone, Guid? SupervisorUserId, string? Notes, uint RowVersion);
    public record CreateRoomRequest(Guid DormitoryId, string Name, int BedCount);
    public record UpdateRoomRequest(string Name, uint RowVersion);
    public record CreateBedRequest(Guid DormitoryRoomId, int? BedNumber);
    public record ChangeBedStatusRequest(BedStatus Status, uint RowVersion);

    // ----- Pavillons -----
    [HttpGet("dormitories")] [Authorize(Roles = ReadRoles)]
    public async Task<IActionResult> ListDormitories([FromQuery] DormitoryGender? gender, CancellationToken ct)
        => Ok(await mediator.Send(new ListDormitoriesQuery(gender), ct));

    [HttpGet("dormitories/{id:guid}")] [Authorize(Roles = ReadRoles)]
    public async Task<IActionResult> GetDormitory(Guid id, CancellationToken ct)
        => Ok(await mediator.Send(new GetDormitoryQuery(id), ct));

    [HttpPost("dormitories")] [Authorize(Roles = ManageRoles)]
    public async Task<IActionResult> CreateDormitory([FromBody] CreateDormitoryRequest r, CancellationToken ct)
    {
        var result = await mediator.Send(new CreateDormitoryCommand
        {
            Name = r.Name, Gender = r.Gender, SupervisorName = r.SupervisorName,
            SupervisorPhone = r.SupervisorPhone, SupervisorUserId = r.SupervisorUserId, Notes = r.Notes
        }, ct);
        return CreatedAtAction(nameof(GetDormitory), new { id = result.Id }, result);
    }

    [HttpPut("dormitories/{id:guid}")] [Authorize(Roles = ManageRoles)]
    public async Task<IActionResult> UpdateDormitory(Guid id, [FromBody] UpdateDormitoryRequest r, CancellationToken ct)
        => Ok(await mediator.Send(new UpdateDormitoryCommand(id, r.Name, r.Gender, r.SupervisorName,
            r.SupervisorPhone, r.SupervisorUserId, r.Notes, r.RowVersion), ct));

    [HttpDelete("dormitories/{id:guid}")] [Authorize(Roles = ManageRoles)]
    public async Task<IActionResult> DeleteDormitory(Guid id, [FromQuery] uint rowVersion, CancellationToken ct)
    { await mediator.Send(new DeleteDormitoryCommand(id, rowVersion), ct); return NoContent(); }

    [HttpGet("dormitories/deleted")] [Authorize(Roles = ManageRoles)]
    public async Task<IActionResult> ListDeletedDormitories(CancellationToken ct)
        => Ok(await mediator.Send(new GetDeletedDormitoriesQuery(), ct));

    [HttpPost("dormitories/{id:guid}/restore")] [Authorize(Roles = ManageRoles)]
    public async Task<IActionResult> RestoreDormitory(Guid id, CancellationToken ct)
    { await mediator.Send(new RestoreDormitoryCommand(id), ct); return NoContent(); }

    // ----- Chambres -----
    [HttpPost("rooms")] [Authorize(Roles = ManageRoles)]
    public async Task<IActionResult> CreateRoom([FromBody] CreateRoomRequest r, CancellationToken ct)
    {
        var result = await mediator.Send(new CreateDormitoryRoomCommand
        { DormitoryId = r.DormitoryId, Name = r.Name, BedCount = r.BedCount }, ct);
        return CreatedAtAction(nameof(GetDormitory), new { id = result.DormitoryId }, result);
    }

    [HttpPut("rooms/{id:guid}")] [Authorize(Roles = ManageRoles)]
    public async Task<IActionResult> UpdateRoom(Guid id, [FromBody] UpdateRoomRequest r, CancellationToken ct)
        => Ok(await mediator.Send(new UpdateDormitoryRoomCommand(id, r.Name, r.RowVersion), ct));

    [HttpDelete("rooms/{id:guid}")] [Authorize(Roles = ManageRoles)]
    public async Task<IActionResult> DeleteRoom(Guid id, [FromQuery] uint rowVersion, CancellationToken ct)
    { await mediator.Send(new DeleteDormitoryRoomCommand(id, rowVersion), ct); return NoContent(); }

    [HttpGet("rooms/deleted")] [Authorize(Roles = ManageRoles)]
    public async Task<IActionResult> ListDeletedRooms(CancellationToken ct)
        => Ok(await mediator.Send(new GetDeletedDormitoryRoomsQuery(), ct));

    [HttpPost("rooms/{id:guid}/restore")] [Authorize(Roles = ManageRoles)]
    public async Task<IActionResult> RestoreRoom(Guid id, CancellationToken ct)
    { await mediator.Send(new RestoreDormitoryRoomCommand(id), ct); return NoContent(); }

    // ----- Lits -----
    [HttpPost("beds")] [Authorize(Roles = ManageRoles)]
    public async Task<IActionResult> CreateBed([FromBody] CreateBedRequest r, CancellationToken ct)
    {
        var result = await mediator.Send(new CreateBedCommand
        { DormitoryRoomId = r.DormitoryRoomId, BedNumber = r.BedNumber }, ct);
        return CreatedAtAction(nameof(ListDormitories), new { }, result);
    }

    [HttpPut("beds/{id:guid}/status")] [Authorize(Roles = ManageRoles)]
    public async Task<IActionResult> ChangeBedStatus(Guid id, [FromBody] ChangeBedStatusRequest r, CancellationToken ct)
        => Ok(await mediator.Send(new ChangeBedStatusCommand(id, r.Status, r.RowVersion), ct));

    [HttpDelete("beds/{id:guid}")] [Authorize(Roles = ManageRoles)]
    public async Task<IActionResult> DeleteBed(Guid id, [FromQuery] uint rowVersion, CancellationToken ct)
    { await mediator.Send(new DeleteBedCommand(id, rowVersion), ct); return NoContent(); }

    [HttpGet("beds/deleted")] [Authorize(Roles = ManageRoles)]
    public async Task<IActionResult> ListDeletedBeds(CancellationToken ct)
        => Ok(await mediator.Send(new GetDeletedBedsQuery(), ct));

    [HttpPost("beds/{id:guid}/restore")] [Authorize(Roles = ManageRoles)]
    public async Task<IActionResult> RestoreBed(Guid id, CancellationToken ct)
    { await mediator.Send(new RestoreBedCommand(id), ct); return NoContent(); }
}
```
Ajouter sur chaque action les `[ProducesResponseType]` (200/201/204, 403, 404, 409, 422) comme `RoomsController`.

> Conflit de routage à vérifier : `GET dormitories/deleted` coexiste avec `GET dormitories/{id:guid}` grâce à la contrainte `:guid`.

- [ ] **Step 2 :** `dotnet build SamaEcole.sln` — Expected: 0 erreur, 0 avertissement.
- [ ] **Step 3 : commit** `feat(internat): BoardingController (pavillons, chambres, lits)`.

---

### Task 7 : Tests fonctionnels HTTP

**Files:** Create `tests/SamaEcole.FunctionalTests/Boarding/BoardingDormitoriesEndpointsTests.cs` (+ éventuellement rendre `NewOwnerContext()` public dans `AuthApiFactory`).

**Interfaces — Consumes:** `AuthApiFactory` (`DirecteurEmail/Password`, `SecretaireEmail/Password`, `EnseignantEmail/Password`, `FinanceEmail/Password`), helper `EnableInternatAsync` (copie de `HalqaEndpointsTests`).

- [ ] **Step 1 : squelette** — même structure que `BuildingsEndpointsTests` (`IClassFixture<AuthApiFactory>`, `IAsyncLifetime` avec `ResetTestUsersAsync`, `TokenAsync`, `SendAsync`). Dans `InitializeAsync`, après `ResetTestUsersAsync`, créer un compte **Surveillant** de l'école de test via le contexte propriétaire de la fabrique (`new User { SchoolId = AuthApiFactory.EcoleId, Role = Role.Surveillant, Status = EntityStatus.Active, Email = "surveillant@sama-ecole.sn", FullName = "Surveillant de test", PasswordHash = new IdentityPasswordHasher().Hash("SurveillantMotdepasse!2026") }`), sans élargir `SeedUsers`.

- [ ] **Step 2 : tests (échouent avant l'étape 6, passent après)**

```csharp
[Fact] public async Task Every_Route_Returns_403_MODULE_DISABLED_Until_The_Module_Is_Enabled()       // GET /api/v1/boarding/dormitories → 403, code MODULE_DISABLED
[Fact] public async Task The_Director_Can_Create_List_Update_And_Delete_A_Dormitory_With_Optimistic_Locking()
[Fact] public async Task A_Stale_RowVersion_On_Update_Returns_409()
[Fact] public async Task Creating_A_Room_Generates_Its_Beds_And_The_Dormitory_List_Reports_The_Capacity()
[Fact] public async Task A_Bed_Can_Go_To_Maintenance_And_Back_And_The_List_Reports_It()
[Fact] public async Task Deleting_A_Dormitory_That_Still_Has_Rooms_Returns_409_RESOURCE_IN_USE()
[Fact] public async Task A_Deleted_Dormitory_Appears_In_The_Trash_And_Can_Be_Restored()
[Fact] public async Task Mixte_Is_Refused_With_422()
[Fact] public async Task An_Unknown_Gender_Value_Is_Refused_With_422_Not_Persisted()                  // {"gender": 99}
[Fact] public async Task The_Surveillant_Can_Read_But_Not_Write()                                      // GET 200 ; POST/PUT/DELETE 403
[Fact] public async Task Teacher_And_Finance_Roles_Get_403_On_Every_Route()                           // [Theory] sur les verbes
```
`EnableInternatAsync(directeurToken)` appelé au début de chaque test hors `Every_Route_Returns_403...` ; les codes d'erreur se lisent dans le corps normalisé (`code`) comme dans `InternatModuleGateTests`.

- [ ] **Step 3 :** `dotnet build` puis `dotnet test tests/SamaEcole.FunctionalTests --filter "FullyQualifiedName~Boarding"` — Expected: PASS.
- [ ] **Step 4 : commit** `test(internat): tests fonctionnels des endpoints pavillons/chambres/lits`.

---

### Task 8 : Masquage des anciennes salles `Dortoir` (décision Q1)

**Files:** Modify `Buildings/Queries/GetBuildingsWithRooms/GetBuildingsWithRoomsQueryHandler.cs`, `Rooms/Commands/CreateRoom/CreateRoomCommandValidator.cs`, `Rooms/Commands/UpdateRoom/UpdateRoomCommandValidator.cs` ; Test `tests/SamaEcole.IntegrationTests/Boarding/LegacyDormitoryRoomsHiddenTests.cs`, `tests/SamaEcole.UnitTests/Buildings/CreateRoomCommandValidatorTests.cs` (ajout).

> **Voir « Points d'attention » n°1 : cette tâche coupe la création de dortoirs pour l'ancien écran `/internat`. À ne livrer qu'avec le lot C.** Elle reste en tête de PR séparée si l'équipe préfère l'isoler (commit dédié, facilement retirable).

- [ ] **Step 1 : tests (échouent)**

Unitaire (ajout dans `CreateRoomCommandValidatorTests`) :
```csharp
[Fact] public void Dortoir_Type_Is_Refused_Because_Dormitories_Are_Managed_In_Internat()
    => _validator.Validate(Valid() with { Type = RoomType.Dortoir }).IsValid.Should().BeFalse();
```
et le test miroir sur `UpdateRoomCommandValidator` (`UpdateRoomCommand(Guid.NewGuid(), "Salle", 30, RoomType.Dortoir, 0)` invalide).

Intégration `LegacyDormitoryRoomsHiddenTests` (école A) — données : bâtiment « Bloc classes » (Salle 1 `SalleDeClasse`), bâtiment « Pavillon repris » (Dortoir 101 `Dortoir` seulement), bâtiment « Mixte » (Salle 2 `SalleDeClasse` + Dortoir 201 `Dortoir`) :
```csharp
[Fact] public async Task The_Classic_Rooms_Screen_Hides_Dortoir_Rooms_And_Buildings_That_Only_Hold_Dortoirs()
// GetBuildingsWithRoomsQuery → « Bloc classes » (1 salle) et « Mixte » (1 salle : Salle 2, pas Dortoir 201) ; « Pavillon repris » absent.
[Fact] public async Task An_Empty_Building_Without_Any_Room_Is_Still_Listed()   // un bâtiment neuf, sans salle, ne doit pas disparaître
```

- [ ] **Step 2 : implémentation**

`CreateRoomCommandValidator` : après la règle `Type`,
```csharp
RuleFor(x => x.Type).NotEqual(RoomType.Dortoir)
    .WithMessage("Les dortoirs se gèrent dans Internat › Pavillons, plus dans Bâtiments et salles.");
```
Même règle dans `UpdateRoomCommandValidator`.

`GetBuildingsWithRoomsQueryHandler` : filtrer les salles et les bâtiments « uniquement dortoirs » **côté base** :
```csharp
var dormitoryOnlyBuildingIds = dbContext.Rooms.AsNoTracking()
    .GroupBy(r => r.BuildingId)
    .Where(g => g.Any(r => r.Type == RoomType.Dortoir) && g.All(r => r.Type == RoomType.Dortoir))
    .Select(g => g.Key);

// bâtiments :  .Where(b => !dormitoryOnlyBuildingIds.Contains(b.Id))
// salles    :  .Where(r => r.Type != RoomType.Dortoir)
```
Un bâtiment sans aucune salle reste listé (le `GroupBy` ne le produit pas).

- [ ] **Step 3 :** build + `dotnet test --filter "FullyQualifiedName~LegacyDormitoryRoomsHiddenTests|FullyQualifiedName~CreateRoomCommandValidatorTests|FullyQualifiedName~BuildingsEndpointsTests|FullyQualifiedName~BuildingsIsolationTests"` — Expected: PASS ; si un test existant utilise un `Dortoir` via `/rooms`, l'adapter (le comportement a volontairement changé) et le dire dans le message de commit.
- [ ] **Step 4 : commit** `feat(internat): masque les anciennes salles Dortoir de la gestion des salles (Q1)`.

---

### Task 9 : Documentation et vérification globale

**Files:** Modify `docs/Volume_4_API_Design.md`, `ACTIVE_CONTEXT.md`.

- [ ] **Step 1 :** `docs/Volume_4_API_Design.md` : section « Internat — Pavillons, chambres, lits » listant les 17 routes (verbe, rôle, corps, codes 200/201/204/403/404/409/422), le catalogue d'erreurs (`RESOURCE_IN_USE`, `ARCHIVED_ENTITY_EXISTS`, `ACTIVE_ENTITY_CONFLICT`, `PARENT_ENTITY_ARCHIVED`, `MODULE_DISABLED`) et la règle « statut `Occupied` calculé ».
- [ ] **Step 2 :** `ACTIVE_CONTEXT.md` : entrée « Internat — lot B livré (CQRS pavillons/chambres/lits), anciennes salles `Dortoir` masquées ; lot C = bascule des lectures + réconciliation » + rappel du risque de séquencement.
- [ ] **Step 3 : vérification** — `dotnet build SamaEcole.sln` (0 avertissement) ; puis, **séquentiellement** : `dotnet test tests/SamaEcole.UnitTests --no-build`, `dotnet test tests/SamaEcole.IntegrationTests --no-build`, `dotnet test tests/SamaEcole.FunctionalTests --no-build` ; `dotnet ef migrations has-pending-model-changes -p src/SamaEcole.Persistence -s src/SamaEcole.Web` (avec `ConnectionStrings__Migrations` factice) — Expected : aucun changement (ce lot n'ajoute **aucune** migration).
- [ ] **Step 4 : commit** `docs(internat): API pavillons/chambres/lits et état du lot B`, puis `git push -u origin feat/internat-dormitories-cqrs` et ouverture de la PR (gabarit `.github/PULL_REQUEST_TEMPLATE.md`, mention du risque de séquencement et de la tâche 8 isolable).

---

## Self-review

- **Couverture de la spec §6.1 et décisions** : `GET/POST/PUT dormitories` → tâches 2, 5, 6 ; `POST rooms` (+ `bedCount`) et `POST beds` → 3, 4 ; compléments validés (`PUT rooms/{id}`, `PUT beds/{id}/status`, `DELETE`/`restore`, corbeilles) → 3, 4, 5, 6 ; taux d'occupation et statut calculé → 1, 5 ; `Mixte` refusé (Q2) → 2 ; surveillant lié (Q7) → 2 ; masquage `Dortoir` (Q1) → 8 ; `RESOURCE_IN_USE` → 1 ; rôles §5.1 et garde de module → 6, 7. **Hors lot** : affectation/fin de séjour (C), sorties/pointage (D), PDF (E).
- **Écart assumé avec la spec §3.7** : suppression stricte (point d'attention n°2).
- **Placeholders** : aucun ; les 17 actions du contrôleur sont écrites (pavillons, chambres, lits). Deux points restent à trancher à l'exécution et sont signalés dans le texte : la traduction EF de la requête de liste (repli en trois requêtes prévu) et la tolérance de `MustBeValidSenegalPhone` à `null`.
- **Cohérence des noms** : commandes, DTO et `BedReader`/`BoardingOccupancy` identiques entre les tâches 1 à 7.
