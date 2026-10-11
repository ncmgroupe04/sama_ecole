# Internat — lot D : sorties et pointage de nuit — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal :** Exposer le registre des sorties (déclaration, retour, liste) et le pointage de nuit collectif d'un pavillon, sur les tables `boarding_leaves` et `boarding_attendances` déjà créées par le lot A.

**Architecture :** CQRS/MediatR dans `SamaEcole.Application/Boarding/Leaves` et `.../Attendance`, contrôleur mince `BoardingController` (déjà verrouillé par `[RequireModule(SchoolModule.Internat)]`). Le statut d'une sortie n'est jamais stocké : fonction pure `BoardingLeaveStatus.Of` + prédicat SQL équivalent. **Aucune migration** (schéma du lot A).

**Tech Stack :** .NET 9, EF Core + PostgreSQL (RLS), MediatR, FluentValidation, xUnit + FluentAssertions, `RlsTestDatabase` (intégration), `AuthApiFactory` (fonctionnels).

**Spec :** `docs/superpowers/specs/2026-10-06-internat-backend-and-profile-isolation-design.md` §3.5, §3.6, §5.1, §6.3, §6.4, §9 ; décisions Q4 (nuitées seulement) et Q5 (accompagnateur non habilité = avertissement, jamais un blocage).

## Global Constraints

- Branche `feat/internat-leaves-attendance`, **empilée sur `feat/internat-boarders-switch` (PR #63)** ; la PR cible cette branche tant que #62/#63 ne sont pas fusionnées.
- Rôles : lecture ET écriture des sorties/retours/pointage = `Directeur`, `Secretariat`, `Surveillant` (constante `ReadRoles` du contrôleur). `Enseignant`/`Finance` → 403.
- Jamais `DateTime.Now` : la date du jour = `DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime)` (même convention que `BoardingAssignmentService.Today()`).
- Verrouillage optimiste par `rowVersion` (xmin) sur le retour (règle #5). Soft delete uniquement (règle #6).
- Erreurs : `BusinessRuleException(message, code)` → 409 ; `FluentValidation.ValidationException` → 422 ; `KeyNotFoundException` → 404.
- Nouveaux codes d'erreur 409 : `LEAVE_ALREADY_OPEN`, `LEAVE_ALREADY_RETURNED`, `ATTENDANCE_CONFLICT`. Codes par ligne du pointage (pas des erreurs HTTP) : `NOT_IN_DORMITORY`, `DUPLICATE_ENTRY`, `BOARDER_ON_LEAVE`.
- Aucune donnée médicale dans ce lot (`MedicalNotes` n'apparaît dans aucun DTO de sortie ni de pointage).
- `dotnet build` avant tout `dotnet test --no-build` ; ne JAMAIS lancer plusieurs projets de tests en parallèle (contention Npgsql). Journaux redirigés vers un fichier.
- Écrire les fichiers avec l'outil Write (pas de gros heredocs bash : les apostrophes les cassent).

## Décisions de conception (à valider par le relecteur)

| # | Décision | Pourquoi |
|---|---|---|
| D1 | « Jour pointable » = date ≤ aujourd'hui **et** comprise dans l'année scolaire active. Pas de règle jour de semaine (un internat dort aussi le week-end). | La spec dit « jour pointable » sans le définir ; le plus simple et sans exclure le week-end. |
| D2 | Seuls les **séjours actifs** du pavillon sont pointables (interne avec lit dans ce pavillon). Un pointage rétroactif d'un séjour clos n'est pas géré en V1. | Évite de reconstituer l'historique des lits. |
| D3 | Un pensionnaire est « en sortie » la nuit `D` si `LeaveDate <= D` et (`ActualReturnDate IS NULL` ou `ActualReturnDate > D`). Une sortie dépassée mais non rentrée reste « en sortie ». | Il dort dehors tant qu'il n'est pas rentré ; le jour du retour il dort à l'internat. |
| D4 | Pointage collectif = **résultat par ligne** : les lignes valides sont enregistrées, les autres rapportées (`Rejected` + code). Les erreurs de forme globale (date future, pavillon inconnu) restent des erreurs HTTP. | Spec §6.3 « Réponse : résultat par ligne ». |
| D5 | Correspondance accompagnateur : `Trim` + espaces multiples réduits + insensible à la casse, comparée aux `Name` des `AllowedExitPersons`. | Souplesse terrain, sans normalisation d'accents (pas de faux positifs). |
| D6 | `GET /boarders/{id}` gagne un champ **additif** `leaves` (10 dernières sorties). `GET /boarders` gagne le filtre `status=onLeave`. | Promis par la spec §6.2 et par le commentaire de `ListBoardersQuery`. |

## Structure des fichiers

| Fichier | Rôle |
|---|---|
| `Domain/Enums/BoardingEnums.cs` (modif) | + `LeaveStatus` |
| `Domain/Enums/BoardingLeaveStatus.cs` (créer) | `Of(...)` pur + prédicat d'expression `Matches` + `OnNight` |
| `Application/Boarding/Leaves/CompanionMatcher.cs` (créer) | `IsListed(accompaniedBy, persons)` |
| `Application/Boarding/Leaves/LeaveDtos.cs` (créer) | `BoardingLeaveDto`, `PaginatedLeaves` |
| `Application/Boarding/Leaves/LeaveReader.cs` (créer) | projection unique d'une sortie en DTO |
| `Application/Boarding/Leaves/DeclareLeave/DeclareLeaveCommand.cs` (créer) | commande + validator + handler |
| `Application/Boarding/Leaves/RecordReturn/RecordReturnCommand.cs` (créer) | idem |
| `Application/Boarding/Leaves/ListLeaves/ListLeavesQuery.cs` (créer) | liste paginée filtrée |
| `Application/Boarding/Attendance/AttendanceDtos.cs` (créer) | feuille d'appel + résultat par ligne |
| `Application/Boarding/Attendance/GetAttendanceSheet/GetAttendanceSheetQuery.cs` (créer) | feuille d'appel du soir |
| `Application/Boarding/Attendance/RecordAttendance/RecordAttendanceCommand.cs` (créer) | pointage collectif |
| `Application/Boarding/Boarders/BoarderDtos.cs`, `BoarderDetailReader.cs`, `GetBoarder/…`, `UpdateBoarderProfile/…`, `ListBoarders/ListBoardersQuery.cs` (modif) | `leaves` + `onLeave` |
| `Web/Controllers/BoardingController.cs` (modif) | 5 routes |
| `docs/Volume_4_API_Design.md`, `openapi.yaml`, `ACTIVE_CONTEXT.md` (modif) | contrat et état |
| `tests/SamaEcole.UnitTests/Boarding/Leave*Tests.cs`, `tests/SamaEcole.IntegrationTests/Boarding/Leave*Tests.cs`, `Attendance*Tests.cs`, `tests/SamaEcole.FunctionalTests/Boarding/LeavesAndAttendanceEndpointsTests.cs` (créer) | tests |

---

### Task 1 : statut d'une sortie, prédicats SQL et correspondance de l'accompagnateur

**Files:**
- Modify: `src/SamaEcole.Domain/Enums/BoardingEnums.cs`
- Create: `src/SamaEcole.Domain/Enums/BoardingLeaveStatus.cs`
- Create: `src/SamaEcole.Application/Boarding/Leaves/CompanionMatcher.cs`
- Test: `tests/SamaEcole.UnitTests/Boarding/BoardingLeaveStatusTests.cs`, `tests/SamaEcole.UnitTests/Boarding/CompanionMatcherTests.cs`

**Interfaces:**
- Produces : `enum LeaveStatus { Pending, Active, Overdue, Returned }` ; `BoardingLeaveStatus.Of(DateOnly leaveDate, DateOnly expectedReturnDate, DateOnly? actualReturnDate, DateOnly today) : LeaveStatus` ; `BoardingLeaveStatus.Of(BoardingLeave leave, DateOnly today)` ; `Expression<Func<BoardingLeave,bool>> BoardingLeaveStatus.Matches(LeaveStatus status, DateOnly today)` ; `Expression<Func<BoardingLeave,bool>> BoardingLeaveStatus.OnNight(DateOnly night)` ; `bool CompanionMatcher.IsListed(string accompaniedBy, IEnumerable<AllowedExitPerson> persons)`.

- [ ] **Step 1 : tests unitaires (échouent)**

`BoardingLeaveStatusTests.cs` — `[Theory]` avec `leave = 10/10`, `expected = 12/10` :

| today | actual | attendu |
|---|---|---|
| 09/10 | null | `Pending` |
| 10/10 | null | `Active` (borne basse) |
| 12/10 | null | `Active` (borne haute) |
| 13/10 | null | `Overdue` |
| 13/10 | 11/10 | `Returned` (le retour prime sur le retard) |

Plus un test que `Matches(status, today)` compilé (`.Compile()`) rend le même verdict que `Of` pour chacun des cinq cas, et un test `OnNight(night)` : `leave=10/10, actual=null` → vrai pour la nuit du 10 et du 20 ; `actual=12/10` → vrai le 11, **faux le 12** (nuit du retour à l'internat).

`CompanionMatcherTests.cs` : `"  fatou   DIOP "` ↔ `Name = "Fatou Diop"` → vrai ; `"Moussa"` ↔ liste vide → faux ; `"Fatou"` ↔ `"Fatou Diop"` → faux (pas de correspondance partielle).

- [ ] **Step 2 : lancer — doit échouer** `dotnet test tests/SamaEcole.UnitTests --filter "FullyQualifiedName~BoardingLeaveStatus|FullyQualifiedName~CompanionMatcher"` → erreur de compilation (types absents).

- [ ] **Step 3 : implémenter**

`BoardingEnums.cs` : ajouter
```csharp
/// <summary>Statut CALCULÉ d'une sortie (jamais stocké, spec N6).</summary>
public enum LeaveStatus { Pending, Active, Overdue, Returned }
```

`BoardingLeaveStatus.cs` :
```csharp
using System.Linq.Expressions;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Domain.Enums;

public static class BoardingLeaveStatus
{
    public static LeaveStatus Of(DateOnly leaveDate, DateOnly expectedReturnDate, DateOnly? actualReturnDate, DateOnly today)
    {
        if (actualReturnDate is not null) return LeaveStatus.Returned;
        if (today < leaveDate) return LeaveStatus.Pending;
        if (today > expectedReturnDate) return LeaveStatus.Overdue;
        return LeaveStatus.Active;
    }

    public static LeaveStatus Of(BoardingLeave leave, DateOnly today) =>
        Of(leave.LeaveDate, leave.ExpectedReturnDate, leave.ActualReturnDate, today);

    /// <summary>Même verdict que <see cref="Of(BoardingLeave, DateOnly)"/>, traduisible en SQL pour le filtre de liste.</summary>
    public static Expression<Func<BoardingLeave, bool>> Matches(LeaveStatus status, DateOnly today) => status switch
    {
        LeaveStatus.Returned => l => l.ActualReturnDate != null,
        LeaveStatus.Pending => l => l.ActualReturnDate == null && today < l.LeaveDate,
        LeaveStatus.Overdue => l => l.ActualReturnDate == null && today >= l.LeaveDate && today > l.ExpectedReturnDate,
        _ => l => l.ActualReturnDate == null && today >= l.LeaveDate && today <= l.ExpectedReturnDate
    };

    /// <summary>Le pensionnaire dort dehors la nuit <paramref name="night"/> (décision D3) : parti, et pas encore rentré ce soir-là.</summary>
    public static Expression<Func<BoardingLeave, bool>> OnNight(DateOnly night) =>
        l => l.LeaveDate <= night && (l.ActualReturnDate == null || l.ActualReturnDate > night);
}
```

`CompanionMatcher.cs` :
```csharp
using System.Text.RegularExpressions;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Boarding.Leaves;

/// <summary>Décision D5 : trim, espaces réduits, insensible à la casse, correspondance EXACTE du nom.</summary>
public static partial class CompanionMatcher
{
    public static bool IsListed(string accompaniedBy, IEnumerable<AllowedExitPerson> persons)
    {
        var wanted = Normalize(accompaniedBy);
        return wanted.Length > 0 && persons.Any(p => string.Equals(Normalize(p.Name), wanted, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string value) => Spaces().Replace(value.Trim(), " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
```

- [ ] **Step 4 : relancer — doit passer** (même commande) → PASS.
- [ ] **Step 5 : commit** `feat(internat): statut d'une sortie, prédicats SQL et correspondance de l'accompagnateur`

---

### Task 2 : déclarer une sortie

**Files:**
- Create: `src/SamaEcole.Application/Boarding/Leaves/LeaveDtos.cs`, `LeaveReader.cs`, `DeclareLeave/DeclareLeaveCommand.cs`
- Test: `tests/SamaEcole.UnitTests/Boarding/DeclareLeaveValidatorTests.cs`, `tests/SamaEcole.IntegrationTests/Boarding/LeaveCommandsTests.cs`, `tests/SamaEcole.IntegrationTests/Boarding/LeaveTestSeed.cs`

**Interfaces:**
- Consumes : `CompanionMatcher.IsListed`, `BoardingLeaveStatus.Of`, `IApplicationDbContext.BoardingLeaves`, `BoardingConflicts`-style traduction d'index unique (`DuplicateRecordException.TechnicalDetail`).
- Produces :
```csharp
public record BoardingLeaveDto(
    Guid Id, Guid BoarderId, Guid StudentId, string StudentName, string Matricule, string? DormitoryName, string? RoomName,
    BoardingLeaveReason Reason, string? ReasonDetail, DateOnly LeaveDate, DateOnly ExpectedReturnDate, DateOnly? ActualReturnDate,
    string AccompaniedBy, bool IsCompanionUnlisted, LeaveStatus Status, uint RowVersion);
public record PaginatedLeaves(IReadOnlyList<BoardingLeaveDto> Items, int TotalCount, int Page, int PageSize);
public record DeclareLeaveCommand(Guid BoarderId, DateOnly LeaveDate, DateOnly ExpectedReturnDate,
    BoardingLeaveReason Reason, string? ReasonDetail, string AccompaniedBy) : IRequest<BoardingLeaveDto>, IAuditableRequest;
internal static class LeaveReader { IQueryable<LeaveRow> Project(...); Task<BoardingLeaveDto> GetAsync(IApplicationDbContext, Guid leaveId, DateOnly today, CancellationToken); BoardingLeaveDto ToDto(LeaveRow, DateOnly today); }
```

- [ ] **Step 1 : `LeaveTestSeed` (intégration)** — classe utilitaire `internal static` qui, sur un `ApplicationDbContext` propriétaire (`_db.NewOwnerContext()`), crée : une école `EcoleA` (+ `EcoleB` pour l'isolation), une année active `2026-2027` (01/09/2026 → 30/06/2027) et une année passée, une classe, un pavillon `Garçons`, deux chambres `101`/`102` de deux lits, et trois séjours actifs `Interne` (élèves `m1`, `m2`, `m3`; `m1` avec `AllowedExitPersons = [{ Name = "Fatou Diop", Relationship = "Mère", Phone = "770000000" }]`). Reprendre à l'identique la construction de `Student` + `Enrollment` + `BoardingEnrollment` de `BoarderCommandsTests.Add` (lot C) et exposer `Dictionary<string, Guid> Stays`. Les `TimeProvider` sont fixés avec `new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero))` (package `Microsoft.Extensions.Time.Testing` — vérifier qu'il est déjà référencé dans `SamaEcole.IntegrationTests.csproj`, sinon l'ajouter).

- [ ] **Step 2 : tests (échouent)** — `LeaveCommandsTests` (`[Trait("Category","MultiTenant")]`, `IAsyncLifetime`, handler instancié directement comme dans `BoarderCommandsTests`) :
  1. `Declaring_A_Leave_Persists_It_And_Computes_Status` : `m1`, `LeaveDate=2026-10-10`, `Expected=2026-10-14`, accompagnateur `"fatou  DIOP"` → `IsCompanionUnlisted == false`, `Status == Active` (aujourd'hui = 12/10), `ReturnDate` nulle, `StudentName == "Élève m1"`, pavillon/chambre renseignés.
  2. `An_Unlisted_Companion_Is_Accepted_And_Flagged` : `"Oncle Moussa"` → créée, `IsCompanionUnlisted == true` (décision Q5, jamais bloquée) ; ajouter `AllowedExitPersons` à `m1` ensuite ne change PAS la valeur persistée (relire en base : toujours `true`).
  3. `A_Second_Open_Leave_Is_Refused_With_409` : deux déclarations pour `m2` → la 2ᵉ lève `BusinessRuleException` code `LEAVE_ALREADY_OPEN`.
  4. `Concurrent_Declarations_Yield_One_Leave_And_One_Conflict` : deux `Task` simultanées sur `m3` avec deux contextes → exactement 1 succès et 1 `BusinessRuleException(LEAVE_ALREADY_OPEN)` (jamais une `DbUpdateException` brute — le pré-contrôle ne suffit pas, l'index `UX_boarding_leaves_open` tranche).
  5. `A_Boarder_Without_Active_Stay_Or_From_Another_School_Is_Not_Found` : séjour clos et séjour de `EcoleB` (handler en tenant `EcoleA`) → `KeyNotFoundException`.
  6. `Validator` (unitaire, `DeclareLeaveValidatorTests`) : `Expected < LeaveDate` → échec ; `AccompaniedBy` vide ou > 150 → échec ; `ReasonDetail` > 500 → échec ; `Reason` hors enum → échec ; `BoarderId` vide → échec.

- [ ] **Step 3 : lancer — doivent échouer** `dotnet build SamaEcole.sln` puis `dotnet test tests/SamaEcole.IntegrationTests --no-build --filter "FullyQualifiedName~LeaveCommandsTests"` → échec de compilation.

- [ ] **Step 4 : implémenter**

`DeclareLeaveCommand.cs` :
```csharp
public class DeclareLeaveCommandValidator : AbstractValidator<DeclareLeaveCommand>
{
    public DeclareLeaveCommandValidator()
    {
        RuleFor(x => x.BoarderId).NotEmpty();
        RuleFor(x => x.Reason).IsInEnum().WithMessage("Motif de sortie invalide.");
        RuleFor(x => x.ExpectedReturnDate).GreaterThanOrEqualTo(x => x.LeaveDate)
            .WithMessage("La date de retour prévue ne peut pas précéder la date de sortie.");
        RuleFor(x => x.AccompaniedBy).NotEmpty().WithMessage("Indiquez qui accompagne le pensionnaire.").MaximumLength(150);
        RuleFor(x => x.ReasonDetail).MaximumLength(500);
    }
}

public class DeclareLeaveCommandHandler(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<DeclareLeaveCommand, BoardingLeaveDto>
{
    private const string AlreadyOpen = "LEAVE_ALREADY_OPEN";

    public async Task<BoardingLeaveDto> Handle(DeclareLeaveCommand request, CancellationToken cancellationToken)
    {
        var stay = await dbContext.BoardingEnrollments.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == request.BoarderId && b.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException($"Séjour actif {request.BoarderId} introuvable.");

        if (await dbContext.BoardingLeaves.AnyAsync(
                l => l.BoardingEnrollmentId == stay.Id && l.ActualReturnDate == null, cancellationToken))
        {
            throw new BusinessRuleException("Ce pensionnaire a déjà une sortie en cours : enregistrez d'abord son retour.", AlreadyOpen);
        }

        var leave = new BoardingLeave
        {
            SchoolId = stay.SchoolId,
            BoardingEnrollmentId = stay.Id,
            LeaveDate = request.LeaveDate,
            ExpectedReturnDate = request.ExpectedReturnDate,
            Reason = request.Reason,
            ReasonDetail = request.ReasonDetail?.Trim(),
            AccompaniedBy = request.AccompaniedBy.Trim(),
            // FIGÉ ici (Q5) : la traçabilité ne change pas si la liste des personnes habilitées est modifiée plus tard.
            IsCompanionUnlisted = !CompanionMatcher.IsListed(request.AccompaniedBy, stay.AllowedExitPersons)
        };
        dbContext.BoardingLeaves.Add(leave);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateRecordException ex)
            when (ex.TechnicalDetail.Contains("UX_boarding_leaves_open", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException("Ce pensionnaire a déjà une sortie en cours : enregistrez d'abord son retour.", AlreadyOpen);
        }

        return await LeaveReader.GetAsync(dbContext, leave.Id, Today(), cancellationToken);
    }

    private DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
}
```

`LeaveReader.cs` : `LeaveRow` = record interne reprenant les champs de `BoardingLeaveDto` sauf `Status`, avec `RowVersion = EF.Property<uint>(l, "xmin")`. `Project` joint `BoardingEnrollments` (par `BoardingEnrollmentId`), `Students`, puis `Beds → DormitoryRooms → Dormitories` en `DefaultIfEmpty` (même schéma de jointures que `BoarderReader.Project`, un séjour clos n'a plus de lit). `ToDto(row, today)` appelle `BoardingLeaveStatus.Of(row.LeaveDate, row.ExpectedReturnDate, row.ActualReturnDate, today)`. `GetAsync` : `Project(dbContext, dbContext.BoardingLeaves.AsNoTracking().Where(l => l.Id == id)).FirstOrDefaultAsync(...) ?? throw new KeyNotFoundException(...)`. Tri : `orderby l.LeaveDate descending, l.Id` DANS la projection (EF ne traduit pas un `OrderBy` après la construction du record).

- [ ] **Step 5 : relancer — doit passer** (même commande) → PASS ; relancer aussi `dotnet test tests/SamaEcole.UnitTests --filter "FullyQualifiedName~DeclareLeave"`.
- [ ] **Step 6 : commit** `feat(internat): déclaration d'une sortie (accompagnateur non habilité accepté et tracé)`

---

### Task 3 : enregistrer un retour

**Files:**
- Create: `src/SamaEcole.Application/Boarding/Leaves/RecordReturn/RecordReturnCommand.cs`
- Test: `tests/SamaEcole.IntegrationTests/Boarding/LeaveReturnTests.cs`

**Interfaces:**
- Consumes : `LeaveReader.GetAsync`, `IApplicationDbContext.SetOriginalConcurrencyToken`.
- Produces : `record RecordReturnCommand(Guid LeaveId, uint RowVersion, DateOnly? ActualReturnDate) : IRequest<BoardingLeaveDto>, IAuditableRequest;`

- [ ] **Step 1 : tests (échouent)** — `LeaveReturnTests` (même seed) :
  1. `Returning_Sets_The_Date_To_Today_By_Default_And_Closes_The_Leave` → `Status == Returned`, `ActualReturnDate == 2026-10-12`.
  2. `An_Explicit_Past_Date_Is_Accepted_But_Not_Before_The_Leave_Date_Nor_In_The_Future` : `LeaveDate=10/10`, retour `11/10` accepté ; `09/10` et `13/10` (futur) → `FluentValidation.ValidationException` (422).
  3. `Returning_Twice_Is_Refused_With_409` → `BusinessRuleException` code `LEAVE_ALREADY_RETURNED` (le contrôle d'état passe AVANT le jeton, pour un message clair).
  4. `A_Stale_RowVersion_Is_Rejected` : deux retours simultanés avec le même `rowVersion` → l'un réussit, l'autre lève `ConcurrencyConflictException` (ou `LEAVE_ALREADY_RETURNED` s'il arrive après la validation de l'autre — accepter les deux codes, jamais une 500).
  5. `A_Leave_From_Another_School_Is_Not_Found` → `KeyNotFoundException`.
  6. `After_The_Return_The_Stay_Can_Be_Ended` : `EndBoardingCommand` sur le même pensionnaire réussit (le garde `LEAVE_IN_PROGRESS` du lot C tombe) — vérifie l'intégration avec le lot C.

- [ ] **Step 2 : lancer — échec de compilation.**

- [ ] **Step 3 : implémenter**
```csharp
public record RecordReturnCommand(Guid LeaveId, uint RowVersion, DateOnly? ActualReturnDate)
    : IRequest<BoardingLeaveDto>, IAuditableRequest;

public class RecordReturnCommandValidator : AbstractValidator<RecordReturnCommand>
{
    public RecordReturnCommandValidator() => RuleFor(x => x.LeaveId).NotEmpty();
}

public class RecordReturnCommandHandler(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<RecordReturnCommand, BoardingLeaveDto>
{
    public async Task<BoardingLeaveDto> Handle(RecordReturnCommand request, CancellationToken cancellationToken)
    {
        var leave = await dbContext.BoardingLeaves.FirstOrDefaultAsync(l => l.Id == request.LeaveId, cancellationToken)
            ?? throw new KeyNotFoundException($"Sortie {request.LeaveId} introuvable.");

        if (leave.ActualReturnDate is not null)
        {
            throw new BusinessRuleException("Le retour de cette sortie est déjà enregistré.", "LEAVE_ALREADY_RETURNED");
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var returnDate = request.ActualReturnDate ?? today;

        if (returnDate > today)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.ActualReturnDate), "La date de retour ne peut pas être dans le futur.")]);
        }

        if (returnDate < leave.LeaveDate)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.ActualReturnDate), "La date de retour ne peut pas précéder la date de sortie.")]);
        }

        dbContext.SetOriginalConcurrencyToken(leave, request.RowVersion);
        leave.ActualReturnDate = returnDate;
        await dbContext.SaveChangesAsync(cancellationToken);

        return await LeaveReader.GetAsync(dbContext, leave.Id, today, cancellationToken);
    }
}
```
(`using FluentValidation; using FluentValidation.Results;`)

- [ ] **Step 4 : relancer — PASS.**
- [ ] **Step 5 : commit** `feat(internat): enregistrement du retour d'une sortie`

---

### Task 4 : liste des sorties, fiche pensionnaire et filtre « en permission »

**Files:**
- Create: `src/SamaEcole.Application/Boarding/Leaves/ListLeaves/ListLeavesQuery.cs`
- Modify: `src/SamaEcole.Application/Boarding/Boarders/BoarderDtos.cs`, `BoarderDetailReader.cs`, `GetBoarder/GetBoarderQuery.cs`, `UpdateBoarderProfile/UpdateBoarderProfileCommand.cs`, `ListBoarders/ListBoardersQuery.cs`
- Test: `tests/SamaEcole.IntegrationTests/Boarding/LeaveQueriesTests.cs`

**Interfaces:**
- Consumes : `LeaveReader`, `BoardingLeaveStatus.Matches`.
- Produces :
```csharp
public record ListLeavesQuery : IRequest<PaginatedLeaves>
{ Guid? BoarderId; Guid? DormitoryId; LeaveStatus? Status; DateOnly? From; DateOnly? To; bool UnlistedOnly; int Page = 1; int PageSize = 20; }
// BoarderDetailDto gagne un dernier paramètre : IReadOnlyList<BoardingLeaveDto> Leaves
// ListBoarders : Status accepte aussi "onLeave"
```

- [ ] **Step 1 : tests (échouent)** — `LeaveQueriesTests` : jeu de sorties pour `m1..m3` (une `Pending` 15/10→16/10, une `Active` 10/10→14/10, une `Overdue` 05/10→08/10 non rentrée, une `Returned`), « aujourd'hui » = 12/10 :
  1. filtre `Status` : chacun des 4 états ne renvoie QUE ses sorties (la même vérité que `BoardingLeaveStatus.Of` — comparer à un calcul en mémoire sur la liste complète) ;
  2. `DormitoryId`, `BoarderId`, intervalle `From/To` (chevauchement de `[LeaveDate, ExpectedReturnDate]`), `UnlistedOnly` ;
  3. pagination (`PageSize` borné à 100) et tri par `LeaveDate` décroissant ;
  4. **isolation** : un handler en tenant `EcoleB` ne voit aucune sortie de `EcoleA` ;
  5. `GetBoarderQuery` renvoie `Leaves` (10 dernières, la plus récente d'abord) et ne contient aucun champ médical hors des règles du lot C ;
  6. `ListBoardersQuery { Status = "onLeave" }` : renvoie exactement les pensionnaires actifs dont une sortie ouverte couvre aujourd'hui (`LeaveDate <= today` et non rentré — `Pending` et `Returned` exclus, `Overdue` inclus).

- [ ] **Step 2 : lancer — échec.**

- [ ] **Step 3 : implémenter**
  - `ListLeavesQuery` : validator (`Status` `IsInEnum` si présent ; `From <= To` si les deux) ; handler : `leaves = dbContext.BoardingLeaves.AsNoTracking()` ; appliquer `BoardingLeaveStatus.Matches(status, today)` ; pour `DormitoryId` : `leaves.Where(l => (from be in dbContext.BoardingEnrollments join bed in dbContext.Beds on be.BedId equals bed.Id join r in dbContext.DormitoryRooms on bed.DormitoryRoomId equals r.Id where be.Id == l.BoardingEnrollmentId && r.DormitoryId == dormitoryId select be.Id).Any())` (même sous-requête que `ListBoardersQuery`) ; `From/To` : `l.ExpectedReturnDate >= from` et `l.LeaveDate <= to` ; `Count` puis `Skip/Take` sur `LeaveReader.Project`, puis `ToDto(row, today)` en mémoire.
  - `BoarderDetailDto` : ajouter `IReadOnlyList<BoardingLeaveDto> Leaves` en dernier paramètre ; `BoarderDetailReader.GetAsync` reçoit `TimeProvider` et lit `LeaveReader.Project(dbContext, dbContext.BoardingLeaves.AsNoTracking().Where(l => l.BoardingEnrollmentId == id)).Take(10)` ; mettre à jour `GetBoarderQueryHandler` et `UpdateBoarderProfileCommandHandler` (injecter `TimeProvider`). Corriger les appels de tests existants qui construisent `BoarderDetailDto` ou ces handlers (compilation).
  - `ListBoardersQueryValidator` : ajouter `"onLeave"` à `Statuses` ; handler : `"onleave" => stays.Where(b => b.IsActive && dbContext.BoardingLeaves.Any(l => l.BoardingEnrollmentId == b.Id && l.ActualReturnDate == null && l.LeaveDate <= today))` (injecter `TimeProvider` ; mettre à jour le commentaire de la classe : le filtre existe désormais).

- [ ] **Step 4 : relancer** `LeaveQueriesTests` + `BoarderQueriesTests` + `BoarderCommandsTests` (non-régression lot C) → PASS.
- [ ] **Step 5 : commit** `feat(internat): liste des sorties, sorties sur la fiche pensionnaire et filtre « en permission »`

---

### Task 5 : feuille d'appel et pointage collectif

**Files:**
- Create: `src/SamaEcole.Application/Boarding/Attendance/AttendanceDtos.cs`, `GetAttendanceSheet/GetAttendanceSheetQuery.cs`, `RecordAttendance/RecordAttendanceCommand.cs`
- Test: `tests/SamaEcole.IntegrationTests/Boarding/AttendanceTests.cs`, `tests/SamaEcole.UnitTests/Boarding/RecordAttendanceValidatorTests.cs`

**Interfaces:**
- Consumes : `BoardingLeaveStatus.OnNight`, `IApplicationDbContext.BoardingAttendances`.
- Produces :
```csharp
public record AttendanceEntryInput(Guid BoarderId, bool IsPresent, string? Note);
public record RecordAttendanceCommand(DateOnly Date, Guid DormitoryId, IReadOnlyList<AttendanceEntryInput> Entries)
    : IRequest<RecordAttendanceResult>, IAuditableRequest;
public record AttendanceLineResult(Guid BoarderId, string Outcome, string? Code, string? Message);   // Outcome : "Saved" | "Rejected"
public record RecordAttendanceResult(DateOnly Date, Guid DormitoryId, int Saved, int Rejected, IReadOnlyList<AttendanceLineResult> Lines);
public record GetAttendanceSheetQuery(Guid DormitoryId, DateOnly Date) : IRequest<AttendanceSheetDto>;
public record AttendanceSheetLineDto(Guid BoarderId, string StudentName, string Matricule, string RoomName, int BedNumber,
    bool OnLeave, bool Recorded, bool? IsPresent, string? Note);
public record AttendanceSheetDto(DateOnly Date, Guid DormitoryId, string DormitoryName, IReadOnlyList<AttendanceSheetLineDto> Lines);
```

- [ ] **Step 1 : tests (échouent)** — `AttendanceTests`, « aujourd'hui » = 12/10/2026 :
  1. `Recording_Creates_One_Row_Per_Boarder_And_Upserts_On_Replay` : pointer `m1` présent, `m2` absent avec note ; rejouer avec `m1` absent → toujours UNE ligne par `(séjour, date)` (compter en base), valeur mise à jour, `Saved == 2`.
  2. `A_Future_Date_Or_A_Date_Outside_The_Active_Year_Is_A_422` : `13/10/2026` et `31/08/2026` → `ValidationException` ; pavillon inconnu / d'une autre école → `KeyNotFoundException`.
  3. `A_Boarder_On_Leave_Is_Accepted_Only_As_Absent` : `m1` avec sortie ouverte couvrant le 11/10 : `isPresent=true` → ligne `Rejected / BOARDER_ON_LEAVE` ; `isPresent=false` sans note → `Saved` avec `Note == "En permission"` ; avec note → la note fournie est conservée.
  4. `Per_Line_Rejections_Do_Not_Block_Valid_Lines` : une ligne pour un séjour d'un AUTRE pavillon (`NOT_IN_DORMITORY`), un `boarderId` doublon (`DUPLICATE_ENTRY`, la 1ʳᵉ occurrence l'emporte), un séjour inconnu (`NOT_IN_DORMITORY`), plus deux lignes valides → `Saved == 2`, `Rejected == 3`, lignes valides bien enregistrées.
  5. `The_Night_Of_The_Return_Counts_As_Present_Allowed` : sortie rentrée le 11/10 → pointage du 11/10 en `isPresent=true` accepté ; du 10/10 refusé.
  6. `Two_Concurrent_First_Recordings_Never_Surface_A_Raw_500` : deux commandes simultanées créant la même ligne → au plus un `BusinessRuleException(ATTENDANCE_CONFLICT)`, jamais une `DbUpdateException`.
  7. `The_Sheet_Prefills_Leave_Absentees_And_Existing_Rows` : feuille du 12/10 : `m1` en sortie → `OnLeave=true, Recorded=false, IsPresent=false, Note="En permission"` ; `m2` déjà pointé → `Recorded=true` avec ses valeurs ; `m3` → `IsPresent=null` ; tri par chambre puis numéro de lit ; **les `Interne` sans lit et les `DemiPensionnaire` n'y figurent pas**.
  8. Isolation : tenant `EcoleB` ne peut ni lire la feuille ni pointer le pavillon de `EcoleA` (404).
  9. Validator (unitaire) : `Entries` vide → échec ; plus de 200 lignes → échec ; `Note` > 300 → échec ; `DormitoryId` vide → échec.

- [ ] **Step 2 : lancer — échec de compilation.**

- [ ] **Step 3 : implémenter**

Règle commune (classe interne `AttendanceRules` dans `AttendanceDtos.cs` ou fichier voisin) :
```csharp
internal static class AttendanceRules
{
    public const string OnLeaveNote = "En permission";

    /// Séjours actifs, internes, dont le lit est dans le pavillon (décision D2).
    public static IQueryable<BoardingEnrollment> DormitoryStays(IApplicationDbContext db, Guid dormitoryId) =>
        from be in db.BoardingEnrollments
        join bed in db.Beds on be.BedId equals bed.Id
        join room in db.DormitoryRooms on bed.DormitoryRoomId equals room.Id
        where be.IsActive && be.Regime == BoardingRegime.Interne && room.DormitoryId == dormitoryId
        select be;

    /// Décision D1 : ≤ aujourd'hui et dans l'année scolaire active.
    public static async Task EnsurePointableAsync(IApplicationDbContext db, DateOnly date, DateOnly today, CancellationToken ct)
    {
        if (date > today)
            throw new ValidationException([new ValidationFailure("Date", "Le pointage ne peut pas porter sur une date future.")]);

        var inActiveYear = await db.SchoolYears.AsNoTracking()
            .AnyAsync(y => y.IsActive && y.StartDate <= date && y.EndDate >= date, ct);
        if (!inActiveYear)
            throw new ValidationException([new ValidationFailure("Date", "La date doit appartenir à l'année scolaire active.")]);
    }
}
```
`RecordAttendanceCommandHandler` : (1) `EnsurePointableAsync` ; (2) charger le pavillon (`KeyNotFoundException` sinon) ; (3) `stayIds = DormitoryStays(...)` sous forme de `HashSet<Guid>` des `Id` ; (4) `onLeaveIds` = `BoardingLeaves.Where(OnNight(date)).Select(l => l.BoardingEnrollmentId)` filtré sur `stayIds` ; (5) charger en un seul passage les lignes existantes `BoardingAttendances.Where(a => a.Date == date && ids.Contains(a.BoardingEnrollmentId))` ; (6) boucle sur `Entries` en ordre : doublon → `DUPLICATE_ENTRY`, hors pavillon → `NOT_IN_DORMITORY`, présent + en sortie → `BOARDER_ON_LEAVE`, sinon upsert (note = `Trim()`, vide → null ; absent + en sortie + note vide → `OnLeaveNote`) ; (7) `SaveChangesAsync` dans un `try/catch (DuplicateRecordException ex) when (ex.TechnicalDetail.Contains("UX_boarding_attendances_night"))` → `BusinessRuleException("Ce pointage vient d'être saisi par quelqu'un d'autre. Rechargez la feuille d'appel.", "ATTENDANCE_CONFLICT")`.

Validator : `Entries` non vide, `<= 200`, chaque `Note <= 300`, `DormitoryId` non vide, `Date` non vide (`DateOnly.MinValue` refusé).

`GetAttendanceSheetQuery` : mêmes `EnsurePointableAsync` (date future refusée aussi pour la lecture ? **non** : la feuille d'une date passée ou du jour est lisible ; une date future → 422, cohérent avec l'écriture) ; projection via `BoarderReader`-like (élève, chambre, lit) triée `RoomName, BedNumber` ; `OnLeave` = présence dans `onLeaveIds` ; pré-remplissage : ligne existante → ses valeurs, `Recorded=true` ; sinon si `OnLeave` → `IsPresent=false`, `Note="En permission"`, `Recorded=false` ; sinon `IsPresent=null`.

- [ ] **Step 4 : relancer** `AttendanceTests` + `RecordAttendanceValidatorTests` → PASS.
- [ ] **Step 5 : commit** `feat(internat): feuille d'appel et pointage de nuit collectif`

---

### Task 6 : endpoints HTTP et tests fonctionnels

**Files:**
- Modify: `src/SamaEcole.Web/Controllers/BoardingController.cs`
- Test: `tests/SamaEcole.FunctionalTests/Boarding/LeavesAndAttendanceEndpointsTests.cs`

**Interfaces:**
- Consumes : toutes les commandes/requêtes des tâches 2 à 5.
- Produces : `POST /api/v1/boarding/leaves` (201, `Location` absent), `PUT /api/v1/boarding/leaves/{id}/return`, `GET /api/v1/boarding/leaves`, `POST /api/v1/boarding/attendance`, `GET /api/v1/boarding/attendance?dormitoryId=&date=`.

- [ ] **Step 1 : tests fonctionnels (échouent)** — s'inspirer de `BoardersEndpointsTests` (même `AuthApiFactory`, `SurveillantEmail`, helpers de jeton, noms uniques par test, assertions par identifiant). Cas :
  1. cycle complet : affecter un pensionnaire (réutiliser l'amorçage du lot C), `POST /leaves` → 201 avec `status`, `isCompanionUnlisted`; `GET /boarders/{id}` contient la sortie ; `PUT /leaves/{id}/return` → 200 `Returned`.
  2. **409** : seconde sortie ouverte → `LEAVE_ALREADY_OPEN` ; second retour → `LEAVE_ALREADY_RETURNED` ; `DELETE /unassign-bed` pendant une sortie ouverte → `LEAVE_IN_PROGRESS` (lot C), puis 204 après retour.
  3. **422** : `expectedReturnDate < leaveDate` ; pointage futur.
  4. pointage : `POST /attendance` renvoie le résultat par ligne ; `GET /attendance` pré-remplit l'absent en permission.
  5. **Rôles** : `Directeur`, `Secretariat`, `Surveillant` → succès sur les 5 routes ; `Enseignant` → 403 sur chacune ; sans jeton → 401.
  6. **Garde de module** : école avec `IsInternatEnabled = false` → 403 `MODULE_DISABLED` sur les 5 routes (réutiliser le mécanisme du test du lot C / B).
  7. **Isolation multi-tenant** (`[Trait("Category","MultiTenant")]`) : la sortie d'une école n'est ni listée ni retournable depuis une autre (404).
  8. **Aucune fuite médicale** : le JSON des sorties/pointage ne contient pas `medicalNotes`.

- [ ] **Step 2 : lancer — 404 (routes absentes).**

- [ ] **Step 3 : implémenter** — dans `BoardingController`, ajouter les `using` des nouveaux namespaces, les requêtes :
```csharp
public record DeclareLeaveRequest(Guid BoarderId, DateOnly LeaveDate, DateOnly ExpectedReturnDate,
    BoardingLeaveReason Reason, string? ReasonDetail, string AccompaniedBy);
public record ReturnLeaveRequest(uint RowVersion, DateOnly? ActualReturnDate);
public record RecordAttendanceRequest(DateOnly Date, Guid DormitoryId, List<AttendanceEntryInput> Entries);
```
et une section « Sorties et pointage » avec 5 actions minces (`[Authorize(Roles = ReadRoles)]`, `ProducesResponseType` 200/201, 403, 404, 409, 422 comme leurs voisines). `POST /leaves` → `StatusCode(201, result)`. `GET /leaves` : paramètres `boarderId, dormitoryId, status, from, to, unlistedOnly, page, pageSize`. `GET /attendance` : `[FromQuery] Guid dormitoryId, [FromQuery] DateOnly date`. Aucune logique métier dans le contrôleur (règle #8).

- [ ] **Step 4 : relancer** `dotnet test tests/SamaEcole.FunctionalTests --no-build --filter "FullyQualifiedName~LeavesAndAttendanceEndpointsTests|FullyQualifiedName~BoardersEndpointsTests"` (après `dotnet build`) → PASS.
- [ ] **Step 5 : commit** `feat(internat): endpoints des sorties et du pointage de nuit`

---

### Task 7 : documentation, couverture RLS et suite complète

**Files:**
- Modify: `docs/Volume_4_API_Design.md` (§31 : sous-section 31.3 « Sorties et pointage » + codes d'erreur `LEAVE_ALREADY_OPEN`, `LEAVE_ALREADY_RETURNED`, `ATTENDANCE_CONFLICT` au catalogue ~ligne 95), `openapi.yaml`, `ACTIVE_CONTEXT.md` (entrée « lot D » + mise à jour du « Reste »), `docs/Volume_3_DDS.md` si les index `UX_boarding_leaves_open`/`UX_boarding_attendances_night` n'y figurent pas encore.
- Test : `tests/SamaEcole.FunctionalTests` (RLS) existants.

- [ ] **Step 1 : vérifier la couverture RLS** : `dotnet test tests/SamaEcole.FunctionalTests --no-build --filter "FullyQualifiedName~RlsCoverage"` → PASS (les deux tables sont déjà couvertes par le lot A ; aucune migration n'est ajoutée : `git diff origin/feat/internat-boarders-switch --stat -- src/SamaEcole.Persistence/Migrations` doit être vide).
- [ ] **Step 2 : documenter** les 5 routes (corps, réponses, règles D1-D5, codes), le champ additif `leaves` de `GET /boarders/{id}` et le filtre `status=onLeave`. **Aucune consigne de déploiement ni SQL post-déploiement : ce lot n'ajoute aucune migration** (le dire explicitement dans `ACTIVE_CONTEXT.md`).
- [ ] **Step 3 : suite complète, SÉQUENTIELLE, journaux dans un fichier** (`dotnet build SamaEcole.sln` puis unitaires, `npm test` dans `src/SamaEcole.Web`, fonctionnels, intégration — ~1 h 15). Consigner les chiffres exacts ; tout échec est analysé (rejoué seul) avant d'être qualifié de transitoire.
- [ ] **Step 4 : commit** `docs(internat): API des sorties et du pointage de nuit et état du lot D`
- [ ] **Step 5 : publication** : pousser `feat/internat-leaves-attendance`, ouvrir la PR du lot D **en brouillon**, base `feat/internat-boarders-switch` (retargeter sur `main` quand #62 puis #63 seront fusionnées). Description : résumé, décisions D1-D6, résultats de tests, « aucune migration ».

---

## Auto-revue

- **Couverture de la spec §6.3/§6.4/§9** : déclaration (Q5, une sortie ouverte, dates) → T2 ; retour (idempotence refusée, `rowVersion`, ≤ aujourd'hui) → T3 ; pointage collectif (upsert, date ≤ aujourd'hui + année active, absent en permission, résultat par ligne) → T5 ; `GET attendance` complément → T5 ; statut N6 + prédicat SQL → T1/T4 ; fiche avec sorties → T4 ; rôles, garde de module, isolation, course → T2/T5/T6. **Hors lot D** (assumé) : PDF fiche et registre (lot E), IHM (lot G).
- **Écart spec assumé** : « jour pointable » défini par D1 ; à confirmer par le relecteur.
- **Cohérence des noms** : `LeaveStatus`, `BoardingLeaveStatus.Of/Matches/OnNight`, `CompanionMatcher.IsListed`, `BoardingLeaveDto`, `LeaveReader.GetAsync/Project/ToDto`, `AttendanceRules.DormitoryStays/EnsurePointableAsync/OnLeaveNote` — identiques dans toutes les tâches.
- **Risque connu** : l'ajout de `Leaves` à `BoarderDetailDto` casse la compilation des tests du lot C qui construisent ce record — à corriger dans la même tâche (T4).

## Écarts constatés à l'exécution

- L'exception de validation de l'application est `SamaEcole.Application.Common.Exceptions.ValidationException` (422), pas celle de FluentValidation.
- Pas de `FakeTimeProvider` référencé dans les tests d'intégration : horloge fixe locale `FixedTimeProvider` (`LeaveTestBase.cs`), « aujourd'hui » = 2026-10-12.
- `ListBoardersQueryHandler`, `GetBoarderQueryHandler` et `UpdateBoarderProfileCommandHandler` reçoivent désormais un `TimeProvider` (les 4 appels de `BoarderQueriesTests` sont mis à jour).
- Un statut inconnu dans `GET /leaves?status=` renvoie **400** (binding de l'énumération), pas 422 comme `GET /boarders?status=` (chaîne validée par FluentValidation).
- L'isolation entre écoles est prouvée en intégration (rôle SQL bridé + RLS), pas en fonctionnel : la fabrique fonctionnelle n'a qu'une école.
- `openapi.yaml` ne contient aucune route `boarding` depuis le lot A : non complété ici.
