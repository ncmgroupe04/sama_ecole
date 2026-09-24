# CQRS et API du module Franco-Arabe/Daara (Phase 2) — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Construire le CQRS complet (Commands/Queries MediatR, DTOs, validateurs FluentValidation) et le `QuranController` REST pour `QuranProgress`/`QuranEvaluation`, sur le socle de données déjà posé en Phase 1.

**Architecture:** Suivre à l'identique le patron `GradesController`/`CreateGradeCommand`/`UpdateGradeCommand` (mêmes idiomes de vérification d'existence, verrou optimiste xmin, exceptions 404/409/422) et `GetClassGradesQuery` (jointure roster de classe). Un seul contrôleur mince, `[RequireModule(SchoolModule.Coran)]` en garde unique.

**Tech Stack:** ASP.NET Core 9, MediatR, FluentValidation, EF Core/Npgsql, xUnit + FluentAssertions + Testcontainers.

**Spec:** `docs/superpowers/specs/2026-09-20-franco-arabic-cqrs-api-design.md`

## Global Constraints

- Aucune logique métier dans le contrôleur (règle #8) — `QuranController` ne fait que traduire HTTP ↔ MediatR.
- `SchoolId` ne vient jamais d'un paramètre client (règle #10) — toujours `ITenantProvider.CurrentSchoolId`.
- CQRS strict (règle #7) : Commands pour l'écriture, Queries pour la lecture, jamais mélangés dans un même Handler.
- Concurrence optimiste (règle #5) sur `QuranProgress`/`QuranEvaluation` : toute correction transporte `RowVersion` (xmin), 409 sinon.
- Toute erreur suit le format normalisé existant (`ValidationException` → 422, `KeyNotFoundException` → 404, `ConcurrencyConflictException` → 409, `UnauthorizedAccessException`/`RequireModule` → 403) — jamais une exception brute.
- Enregistrement DI automatique : `AddMediatR`/`AddValidatorsFromAssembly` scannent déjà l'assembly Application — AUCUNE inscription manuelle à ajouter dans `DependencyInjection.cs` pour les Handlers/Validators de ce lot.
- Rôles : écriture `Directeur,Enseignant` ; lecture `Directeur,Enseignant,Secretariat` (décisions #1/#2 de la spec).
- Champs texte libres (`Notes`) : toujours `.NoHtml()` (`SamaEcole.Application.Common.Validation.SafeTextValidation`), même défense que tout le reste du projet.
- Commits : Conventional Commits, un commit par tâche.

---

### Task 1 : DTOs QuranProgress + `CreateQuranProgressCommand`

**Files:**
- Create: `src/SamaEcole.Application/Quran/QuranProgressDto.cs`
- Create: `src/SamaEcole.Application/Quran/Commands/CreateQuranProgress/CreateQuranProgressCommand.cs`
- Create: `src/SamaEcole.Application/Quran/Commands/CreateQuranProgress/CreateQuranProgressCommandHandler.cs`
- Create: `src/SamaEcole.Application/Quran/Commands/CreateQuranProgress/CreateQuranProgressCommandValidator.cs`
- Test: `tests/SamaEcole.UnitTests/Quran/CreateQuranProgressCommandValidatorTests.cs`
- Test: `tests/SamaEcole.IntegrationTests/Quran/CreateQuranProgressCommandTests.cs`

**Interfaces:**
- Produces: `QuranProgressDto(Guid Id, Guid StudentId, int JuzNumber, int HizbNumber, int SurahNumber, QuranMemorizationStatus Status, DateOnly? EvaluationDate, string? Notes, uint RowVersion)` — consommé par Tasks 2, 3, 4, 9.
- Produces: `CreateQuranProgressCommand(Guid StudentId, int JuzNumber, int HizbNumber, int SurahNumber, QuranMemorizationStatus Status, DateOnly? EvaluationDate, string? Notes) : IRequest<QuranProgressDto>, IAuditableRequest` — consommé par Task 9 (contrôleur).

- [ ] **Step 1 : Écrire le test de validateur (échoue — le validateur n'existe pas)**

```csharp
// tests/SamaEcole.UnitTests/Quran/CreateQuranProgressCommandValidatorTests.cs
using SamaEcole.Application.Quran.Commands.CreateQuranProgress;
using SamaEcole.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace SamaEcole.UnitTests.Quran;

public class CreateQuranProgressCommandValidatorTests
{
    private readonly CreateQuranProgressCommandValidator _validator = new();

    private static CreateQuranProgressCommand Valid() => new(
        Guid.NewGuid(), JuzNumber: 1, HizbNumber: 1, SurahNumber: 1,
        Status: QuranMemorizationStatus.InProcess, EvaluationDate: null, Notes: null);

    [Fact]
    public void Valid_Command_Should_Pass()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_StudentId_Should_Fail()
    {
        _validator.Validate(Valid() with { StudentId = Guid.Empty }).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void JuzNumber_Out_Of_Range_Should_Fail(int juz)
    {
        _validator.Validate(Valid() with { JuzNumber = juz }).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public void HizbNumber_Out_Of_Range_Should_Fail(int hizb)
    {
        _validator.Validate(Valid() with { HizbNumber = hizb }).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(115)]
    public void SurahNumber_Out_Of_Range_Should_Fail(int surah)
    {
        _validator.Validate(Valid() with { SurahNumber = surah }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Notes_With_Html_Should_Fail()
    {
        _validator.Validate(Valid() with { Notes = "<script>alert(1)</script>" }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Notes_Too_Long_Should_Fail()
    {
        _validator.Validate(Valid() with { Notes = new string('a', 2001) }).IsValid.Should().BeFalse();
    }
}
```

- [ ] **Step 2 : Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test tests/SamaEcole.UnitTests --filter FullyQualifiedName~CreateQuranProgressCommandValidatorTests`
Expected: FAIL (compilation error — le namespace `SamaEcole.Application.Quran.Commands.CreateQuranProgress` n'existe pas)

- [ ] **Step 3 : Créer le DTO**

```csharp
// src/SamaEcole.Application/Quran/QuranProgressDto.cs
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Quran;

/// <summary>
/// Une observation de suivi de mémorisation coranique. Sert À LA FOIS de résultat de
/// Create/UpdateQuranProgressCommand et d'élément de liste (Get*QuranProgressQuery) — pas de type
/// Result séparé (YAGNI, voir spec §3.4).
/// </summary>
public record QuranProgressDto(
    Guid Id,
    Guid StudentId,
    int JuzNumber,
    int HizbNumber,
    int SurahNumber,
    QuranMemorizationStatus Status,
    DateOnly? EvaluationDate,
    string? Notes,
    uint RowVersion);
```

- [ ] **Step 4 : Créer la commande**

```csharp
// src/SamaEcole.Application/Quran/Commands/CreateQuranProgress/CreateQuranProgressCommand.cs
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Quran.Commands.CreateQuranProgress;

/// <summary>
/// POST /quran/progress — module Coran/Franco-Arabe (spec Phase 2 §3.1). Réservé au Directeur et à
/// l'Enseignant, comme CreateGradeCommand — aucune vérification d'affectation enseignant/matière
/// (décision #1 de la spec, il n'existe aucun sous-rôle "Enseignant Coran").
///
/// Aucune contrainte d'unicité : plusieurs observations pour le même (StudentId, SurahNumber) sont
/// légitimes au fil du temps (décision Phase 1 §3.3).
///
/// IAuditableRequest (JGK-H01) : comme la saisie de notes (décision #6 de la spec).
/// </summary>
public record CreateQuranProgressCommand(
    Guid StudentId,
    int JuzNumber,
    int HizbNumber,
    int SurahNumber,
    QuranMemorizationStatus Status,
    DateOnly? EvaluationDate,
    string? Notes)
    : IRequest<QuranProgressDto>, IAuditableRequest;
```

- [ ] **Step 5 : Créer le validateur**

```csharp
// src/SamaEcole.Application/Quran/Commands/CreateQuranProgress/CreateQuranProgressCommandValidator.cs
using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Quran.Commands.CreateQuranProgress;

public class CreateQuranProgressCommandValidator : AbstractValidator<CreateQuranProgressCommand>
{
    public CreateQuranProgressCommandValidator()
    {
        RuleFor(c => c.StudentId).NotEmpty();
        RuleFor(c => c.JuzNumber).InclusiveBetween(1, 30);
        RuleFor(c => c.HizbNumber).InclusiveBetween(1, 60);
        RuleFor(c => c.SurahNumber).InclusiveBetween(1, 114);
        RuleFor(c => c.Status).IsInEnum().WithMessage("Statut de mémorisation invalide.");
        RuleFor(c => c.Notes).MaximumLength(2000).NoHtml();
    }
}
```

- [ ] **Step 6 : Lancer le test de validateur pour vérifier qu'il passe**

Run: `dotnet test tests/SamaEcole.UnitTests --filter FullyQualifiedName~CreateQuranProgressCommandValidatorTests`
Expected: PASS (7/7)

- [ ] **Step 7 : Écrire le test d'intégration du Handler (échoue — le Handler n'existe pas)**

```csharp
// tests/SamaEcole.IntegrationTests/Quran/CreateQuranProgressCommandTests.cs
using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Quran.Commands.CreateQuranProgress;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Quran;

public class CreateQuranProgressCommandTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ClasseA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid EleveA = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid ClasseB = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");
    private static readonly Guid EleveB = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });
        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.Students.AddRange(
            new Student { Id = EleveA, SchoolId = EcoleA, Matricule = "ELEV-0001", FullName = "Élève A", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = ClasseA },
            new Student { Id = EleveB, SchoolId = EcoleB, Matricule = "ELEV-0001", FullName = "Élève B", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Thiès", Gender = "F", ClassroomId = ClasseB });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Creates_A_Progress_Entry_For_A_Student_Of_The_Current_School()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var handler = new CreateQuranProgressCommandHandler(db, new StubTenantProvider(EcoleA));

        var result = await handler.Handle(
            new CreateQuranProgressCommand(EleveA, 1, 1, 1, QuranMemorizationStatus.InProcess, null, "Bon début"),
            CancellationToken.None);

        result.StudentId.Should().Be(EleveA);
        result.Status.Should().Be(QuranMemorizationStatus.InProcess);
        result.RowVersion.Should().NotBe((uint)0);
    }

    [Fact]
    public async Task Rejects_A_Student_From_Another_School()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var handler = new CreateQuranProgressCommandHandler(db, new StubTenantProvider(EcoleA));

        var act = async () => await handler.Handle(
            new CreateQuranProgressCommand(EleveB, 1, 1, 1, QuranMemorizationStatus.InProcess, null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }
}
```

- [ ] **Step 8 : Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter FullyQualifiedName~CreateQuranProgressCommandTests`
Expected: FAIL (compilation error — `CreateQuranProgressCommandHandler` n'existe pas)

- [ ] **Step 9 : Créer le Handler**

```csharp
// src/SamaEcole.Application/Quran/Commands/CreateQuranProgress/CreateQuranProgressCommandHandler.cs
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Quran.Commands.CreateQuranProgress;

public class CreateQuranProgressCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<CreateQuranProgressCommand, QuranProgressDto>
{
    public async Task<QuranProgressDto> Handle(CreateQuranProgressCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Le Global Query Filter restreint déjà la recherche à l'école courante : un élève d'une
        // autre école y est structurellement introuvable (même idiome que CreateGradeCommandHandler).
        if (!await dbContext.Students.AnyAsync(s => s.Id == request.StudentId, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.StudentId), "L'élève indiqué n'existe pas dans votre établissement.")
            ]);
        }

        var entry = new QuranProgress
        {
            SchoolId = schoolId,
            StudentId = request.StudentId,
            JuzNumber = request.JuzNumber,
            HizbNumber = request.HizbNumber,
            SurahNumber = request.SurahNumber,
            Status = request.Status,
            EvaluationDate = request.EvaluationDate,
            Notes = request.Notes
        };

        dbContext.QuranProgresses.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);

        var rowVersion = await dbContext.QuranProgresses.AsNoTracking()
            .Where(p => p.Id == entry.Id)
            .Select(p => EF.Property<uint>(p, "xmin"))
            .FirstAsync(cancellationToken);

        return new QuranProgressDto(
            entry.Id, entry.StudentId, entry.JuzNumber, entry.HizbNumber, entry.SurahNumber,
            entry.Status, entry.EvaluationDate, entry.Notes, rowVersion);
    }
}
```

- [ ] **Step 10 : Lancer le test d'intégration pour vérifier qu'il passe**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter FullyQualifiedName~CreateQuranProgressCommandTests`
Expected: PASS (2/2). Nécessite Docker (Testcontainers).

- [ ] **Step 11 : Commit**

```bash
git add src/SamaEcole.Application/Quran/QuranProgressDto.cs src/SamaEcole.Application/Quran/Commands/CreateQuranProgress/ tests/SamaEcole.UnitTests/Quran/CreateQuranProgressCommandValidatorTests.cs tests/SamaEcole.IntegrationTests/Quran/CreateQuranProgressCommandTests.cs
git commit -m "feat(quran): ajoute CreateQuranProgressCommand"
```

---

### Task 2 : `UpdateQuranProgressCommand`

**Files:**
- Create: `src/SamaEcole.Application/Quran/Commands/UpdateQuranProgress/UpdateQuranProgressCommand.cs`
- Create: `src/SamaEcole.Application/Quran/Commands/UpdateQuranProgress/UpdateQuranProgressCommandHandler.cs`
- Create: `src/SamaEcole.Application/Quran/Commands/UpdateQuranProgress/UpdateQuranProgressCommandValidator.cs`
- Test: `tests/SamaEcole.UnitTests/Quran/UpdateQuranProgressCommandValidatorTests.cs`
- Test: `tests/SamaEcole.IntegrationTests/Quran/UpdateQuranProgressCommandTests.cs`

**Interfaces:**
- Consumes: `QuranProgressDto`, `CreateQuranProgressCommandHandler` (Task 1, pour semer les données de test).
- Produces: `UpdateQuranProgressCommand(Guid Id, QuranMemorizationStatus Status, DateOnly? EvaluationDate, string? Notes, uint RowVersion) : IRequest<QuranProgressDto>, IAuditableRequest` — consommé par Task 9.

- [ ] **Step 1 : Écrire le test de validateur (échoue)**

```csharp
// tests/SamaEcole.UnitTests/Quran/UpdateQuranProgressCommandValidatorTests.cs
using SamaEcole.Application.Quran.Commands.UpdateQuranProgress;
using SamaEcole.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace SamaEcole.UnitTests.Quran;

public class UpdateQuranProgressCommandValidatorTests
{
    private readonly UpdateQuranProgressCommandValidator _validator = new();

    private static UpdateQuranProgressCommand Valid() => new(
        Guid.NewGuid(), QuranMemorizationStatus.Memorized, null, null, RowVersion: 1);

    [Fact]
    public void Valid_Command_Should_Pass()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_Id_Should_Fail()
    {
        _validator.Validate(Valid() with { Id = Guid.Empty }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Notes_With_Html_Should_Fail()
    {
        _validator.Validate(Valid() with { Notes = "<script>alert(1)</script>" }).IsValid.Should().BeFalse();
    }
}
```

- [ ] **Step 2 : Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test tests/SamaEcole.UnitTests --filter FullyQualifiedName~UpdateQuranProgressCommandValidatorTests`
Expected: FAIL (compilation error)

- [ ] **Step 3 : Créer la commande**

```csharp
// src/SamaEcole.Application/Quran/Commands/UpdateQuranProgress/UpdateQuranProgressCommand.cs
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Quran.Commands.UpdateQuranProgress;

/// <summary>
/// PUT /quran/progress/{id} — corrige une observation déjà saisie. Ne touche JAMAIS Juz/Hizb/
/// Sourate/Élève (identité de la ligne, immuable — décision #8 de la spec, même philosophie que
/// UpdateGradeCommand qui ne touche que Value).
///
/// <see cref="RowVersion"/> est le jeton xmin lu à la dernière consultation (AGENTS.md règle #5) :
/// un jeton périmé fait échouer SaveChangesAsync en 409.
/// </summary>
public record UpdateQuranProgressCommand(
    Guid Id,
    QuranMemorizationStatus Status,
    DateOnly? EvaluationDate,
    string? Notes,
    uint RowVersion)
    : IRequest<QuranProgressDto>, IAuditableRequest;
```

- [ ] **Step 4 : Créer le validateur**

```csharp
// src/SamaEcole.Application/Quran/Commands/UpdateQuranProgress/UpdateQuranProgressCommandValidator.cs
using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Quran.Commands.UpdateQuranProgress;

public class UpdateQuranProgressCommandValidator : AbstractValidator<UpdateQuranProgressCommand>
{
    public UpdateQuranProgressCommandValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Status).IsInEnum().WithMessage("Statut de mémorisation invalide.");
        RuleFor(c => c.Notes).MaximumLength(2000).NoHtml();
    }
}
```

- [ ] **Step 5 : Lancer le test de validateur pour vérifier qu'il passe**

Run: `dotnet test tests/SamaEcole.UnitTests --filter FullyQualifiedName~UpdateQuranProgressCommandValidatorTests`
Expected: PASS (3/3)

- [ ] **Step 6 : Écrire le test d'intégration (échoue)**

```csharp
// tests/SamaEcole.IntegrationTests/Quran/UpdateQuranProgressCommandTests.cs
using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Quran.Commands.CreateQuranProgress;
using SamaEcole.Application.Quran.Commands.UpdateQuranProgress;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Quran;

public class UpdateQuranProgressCommandTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid Eleve = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.Students.Add(new Student { Id = Eleve, SchoolId = Ecole, Matricule = "ELEV-0001", FullName = "Élève de test", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = Classe });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static async Task<QuranProgressDto> SeedEntryAsync(SamaEcole.Persistence.ApplicationDbContext db) =>
        await new CreateQuranProgressCommandHandler(db, new StubTenantProvider(Ecole)).Handle(
            new CreateQuranProgressCommand(Eleve, 1, 1, 1, QuranMemorizationStatus.InProcess, null, null),
            CancellationToken.None);

    [Fact]
    public async Task Corrects_Status_And_Notes()
    {
        await using var seed = _db.NewAppContext(Ecole);
        var created = await SeedEntryAsync(seed);

        await using var db = _db.NewAppContext(Ecole);
        var handler = new UpdateQuranProgressCommandHandler(db);

        var result = await handler.Handle(
            new UpdateQuranProgressCommand(created.Id, QuranMemorizationStatus.Memorized, new DateOnly(2026, 9, 20), "Mémorisé", created.RowVersion),
            CancellationToken.None);

        result.Status.Should().Be(QuranMemorizationStatus.Memorized);
        result.Notes.Should().Be("Mémorisé");
        // L'identité de la ligne ne bouge jamais (décision #8).
        result.JuzNumber.Should().Be(1);
        result.StudentId.Should().Be(Eleve);
    }

    [Fact]
    public async Task Unknown_Id_Is_Not_Found()
    {
        await using var db = _db.NewAppContext(Ecole);
        var handler = new UpdateQuranProgressCommandHandler(db);

        var act = async () => await handler.Handle(
            new UpdateQuranProgressCommand(Guid.NewGuid(), QuranMemorizationStatus.Memorized, null, null, RowVersion: 1),
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Stale_RowVersion_Is_Refused_With_A_Conflict()
    {
        await using var seed = _db.NewAppContext(Ecole);
        var created = await SeedEntryAsync(seed);

        await using var db1 = _db.NewAppContext(Ecole);
        await new UpdateQuranProgressCommandHandler(db1).Handle(
            new UpdateQuranProgressCommand(created.Id, QuranMemorizationStatus.Memorized, null, "Première correction", created.RowVersion),
            CancellationToken.None);

        await using var db2 = _db.NewAppContext(Ecole);
        var handler2 = new UpdateQuranProgressCommandHandler(db2);

        var act = async () => await handler2.Handle(
            new UpdateQuranProgressCommand(created.Id, QuranMemorizationStatus.Revised, null, "Seconde correction", created.RowVersion),
            CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }
}
```

- [ ] **Step 7 : Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter FullyQualifiedName~UpdateQuranProgressCommandTests`
Expected: FAIL (compilation error — `UpdateQuranProgressCommandHandler` n'existe pas)

- [ ] **Step 8 : Créer le Handler**

```csharp
// src/SamaEcole.Application/Quran/Commands/UpdateQuranProgress/UpdateQuranProgressCommandHandler.cs
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Quran.Commands.UpdateQuranProgress;

public class UpdateQuranProgressCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<UpdateQuranProgressCommand, QuranProgressDto>
{
    public async Task<QuranProgressDto> Handle(UpdateQuranProgressCommand request, CancellationToken cancellationToken)
    {
        // Global Query Filter + policy RLS bornent déjà à l'école courante : viser une ligne d'une
        // autre école renvoie 404, jamais une modification silencieuse.
        var entry = await dbContext.QuranProgresses
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Suivi coranique {request.Id} introuvable.");

        // Cœur du verrou optimiste (AGENTS.md règle #5) : le jeton lu par le client devient la valeur
        // d'origine imposée à EF. Périmé → SaveChangesAsync refuse en 409.
        dbContext.SetOriginalConcurrencyToken(entry, request.RowVersion);

        entry.Status = request.Status;
        entry.EvaluationDate = request.EvaluationDate;
        entry.Notes = request.Notes;

        await dbContext.SaveChangesAsync(cancellationToken);

        var rowVersion = await dbContext.QuranProgresses.AsNoTracking()
            .Where(p => p.Id == entry.Id)
            .Select(p => EF.Property<uint>(p, "xmin"))
            .FirstAsync(cancellationToken);

        return new QuranProgressDto(
            entry.Id, entry.StudentId, entry.JuzNumber, entry.HizbNumber, entry.SurahNumber,
            entry.Status, entry.EvaluationDate, entry.Notes, rowVersion);
    }
}
```

- [ ] **Step 9 : Lancer le test d'intégration pour vérifier qu'il passe**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter FullyQualifiedName~UpdateQuranProgressCommandTests`
Expected: PASS (3/3)

- [ ] **Step 10 : Commit**

```bash
git add src/SamaEcole.Application/Quran/Commands/UpdateQuranProgress/ tests/SamaEcole.UnitTests/Quran/UpdateQuranProgressCommandValidatorTests.cs tests/SamaEcole.IntegrationTests/Quran/UpdateQuranProgressCommandTests.cs
git commit -m "feat(quran): ajoute UpdateQuranProgressCommand"
```

---

### Task 3 : `GetStudentQuranProgressQuery`

**Files:**
- Create: `src/SamaEcole.Application/Quran/Queries/GetStudentQuranProgress/GetStudentQuranProgressQuery.cs`
- Create: `src/SamaEcole.Application/Quran/Queries/GetStudentQuranProgress/GetStudentQuranProgressQueryHandler.cs`
- Create: `src/SamaEcole.Application/Quran/Queries/GetStudentQuranProgress/GetStudentQuranProgressQueryValidator.cs`
- Test: `tests/SamaEcole.IntegrationTests/Quran/GetStudentQuranProgressQueryTests.cs`

**Interfaces:**
- Consumes: `QuranProgressDto`, `CreateQuranProgressCommandHandler` (Task 1, pour semer).
- Produces: `GetStudentQuranProgressQuery(Guid StudentId) : IRequest<IReadOnlyList<QuranProgressDto>>` — consommé par Task 9.

- [ ] **Step 1 : Écrire le test d'intégration (échoue)**

```csharp
// tests/SamaEcole.IntegrationTests/Quran/GetStudentQuranProgressQueryTests.cs
using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Quran.Commands.CreateQuranProgress;
using SamaEcole.Application.Quran.Queries.GetStudentQuranProgress;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Quran;

public class GetStudentQuranProgressQueryTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid EleveA = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid EleveB = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.Students.AddRange(
            new Student { Id = EleveA, SchoolId = Ecole, Matricule = "ELEV-0001", FullName = "Élève A", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = Classe },
            new Student { Id = EleveB, SchoolId = Ecole, Matricule = "ELEV-0002", FullName = "Élève B", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Returns_Only_The_Requested_Students_Entries()
    {
        await using var seed = _db.NewAppContext(Ecole);
        var createHandler = new CreateQuranProgressCommandHandler(seed, new StubTenantProvider(Ecole));
        await createHandler.Handle(new CreateQuranProgressCommand(EleveA, 1, 1, 1, QuranMemorizationStatus.InProcess, null, null), CancellationToken.None);
        await createHandler.Handle(new CreateQuranProgressCommand(EleveA, 2, 3, 10, QuranMemorizationStatus.Memorized, null, null), CancellationToken.None);
        await createHandler.Handle(new CreateQuranProgressCommand(EleveB, 1, 1, 1, QuranMemorizationStatus.InProcess, null, null), CancellationToken.None);

        await using var db = _db.NewAppContext(Ecole);
        var handler = new GetStudentQuranProgressQueryHandler(db);

        var result = await handler.Handle(new GetStudentQuranProgressQuery(EleveA), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(p => p.StudentId == EleveA);
    }

    [Fact]
    public async Task Returns_Empty_List_When_No_Entry_Exists()
    {
        await using var db = _db.NewAppContext(Ecole);
        var handler = new GetStudentQuranProgressQueryHandler(db);

        var result = await handler.Handle(new GetStudentQuranProgressQuery(EleveB), CancellationToken.None);

        result.Should().BeEmpty();
    }

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }
}
```

- [ ] **Step 2 : Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter FullyQualifiedName~GetStudentQuranProgressQueryTests`
Expected: FAIL (compilation error)

- [ ] **Step 3 : Créer la requête et son validateur**

```csharp
// src/SamaEcole.Application/Quran/Queries/GetStudentQuranProgress/GetStudentQuranProgressQuery.cs
using MediatR;

namespace SamaEcole.Application.Quran.Queries.GetStudentQuranProgress;

/// <summary>GET /quran/progress?studentId= — historique complet de suivi d'un élève.</summary>
public record GetStudentQuranProgressQuery(Guid StudentId) : IRequest<IReadOnlyList<QuranProgressDto>>;
```

```csharp
// src/SamaEcole.Application/Quran/Queries/GetStudentQuranProgress/GetStudentQuranProgressQueryValidator.cs
using FluentValidation;

namespace SamaEcole.Application.Quran.Queries.GetStudentQuranProgress;

public class GetStudentQuranProgressQueryValidator : AbstractValidator<GetStudentQuranProgressQuery>
{
    public GetStudentQuranProgressQueryValidator()
    {
        RuleFor(q => q.StudentId).NotEmpty();
    }
}
```

- [ ] **Step 4 : Créer le Handler**

```csharp
// src/SamaEcole.Application/Quran/Queries/GetStudentQuranProgress/GetStudentQuranProgressQueryHandler.cs
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Quran.Queries.GetStudentQuranProgress;

public class GetStudentQuranProgressQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetStudentQuranProgressQuery, IReadOnlyList<QuranProgressDto>>
{
    public async Task<IReadOnlyList<QuranProgressDto>> Handle(
        GetStudentQuranProgressQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.QuranProgresses.AsNoTracking()
            .Where(p => p.StudentId == request.StudentId)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new QuranProgressDto(
                p.Id, p.StudentId, p.JuzNumber, p.HizbNumber, p.SurahNumber,
                p.Status, p.EvaluationDate, p.Notes, EF.Property<uint>(p, "xmin")))
            .ToListAsync(cancellationToken);
    }
}
```

- [ ] **Step 5 : Lancer le test pour vérifier qu'il passe**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter FullyQualifiedName~GetStudentQuranProgressQueryTests`
Expected: PASS (2/2)

- [ ] **Step 6 : Commit**

```bash
git add src/SamaEcole.Application/Quran/Queries/GetStudentQuranProgress/ tests/SamaEcole.IntegrationTests/Quran/GetStudentQuranProgressQueryTests.cs
git commit -m "feat(quran): ajoute GetStudentQuranProgressQuery"
```

---

### Task 4 : `GetClassQuranProgressQuery`

**Files:**
- Create: `src/SamaEcole.Application/Quran/Queries/GetClassQuranProgress/GetClassQuranProgressQuery.cs`
- Create: `src/SamaEcole.Application/Quran/Queries/GetClassQuranProgress/GetClassQuranProgressQueryHandler.cs`
- Create: `src/SamaEcole.Application/Quran/Queries/GetClassQuranProgress/GetClassQuranProgressQueryValidator.cs`
- Test: `tests/SamaEcole.IntegrationTests/Quran/GetClassQuranProgressQueryTests.cs`

**Interfaces:**
- Consumes: `QuranProgressDto`, `CreateQuranProgressCommandHandler` (Task 1).
- Produces: `ClassQuranProgressRowDto(Guid StudentId, string Matricule, string FullName, IReadOnlyList<QuranProgressDto> Entries)`, `GetClassQuranProgressQuery(Guid ClassroomId) : IRequest<IReadOnlyList<ClassQuranProgressRowDto>>` — consommés par Task 9.

- [ ] **Step 1 : Écrire le test d'intégration (échoue)**

```csharp
// tests/SamaEcole.IntegrationTests/Quran/GetClassQuranProgressQueryTests.cs
using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Quran.Commands.CreateQuranProgress;
using SamaEcole.Application.Quran.Queries.GetClassQuranProgress;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Quran;

public class GetClassQuranProgressQueryTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ClasseA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid ClasseB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid EleveA = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");
    private static readonly Guid EleveB = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");
    private static readonly Guid EleveAutreEcole = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000e");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });
        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.Students.AddRange(
            new Student { Id = EleveA, SchoolId = EcoleA, Matricule = "ELEV-0001", FullName = "Awa Fall", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA },
            new Student { Id = EleveB, SchoolId = EcoleA, Matricule = "ELEV-0002", FullName = "Modou Diop", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = ClasseA },
            new Student { Id = EleveAutreEcole, SchoolId = EcoleB, Matricule = "ELEV-0001", FullName = "Élève B", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Thiès", Gender = "M", ClassroomId = ClasseB });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Returns_One_Row_Per_Student_With_Their_Entries_Sorted_By_FullName()
    {
        await using var seed = _db.NewAppContext(EcoleA);
        var createHandler = new CreateQuranProgressCommandHandler(seed, new StubTenantProvider(EcoleA));
        await createHandler.Handle(new CreateQuranProgressCommand(EleveA, 1, 1, 1, QuranMemorizationStatus.InProcess, null, null), CancellationToken.None);
        await createHandler.Handle(new CreateQuranProgressCommand(EleveB, 30, 60, 114, QuranMemorizationStatus.Memorized, null, null), CancellationToken.None);

        await using var db = _db.NewAppContext(EcoleA);
        var handler = new GetClassQuranProgressQueryHandler(db);

        var result = await handler.Handle(new GetClassQuranProgressQuery(ClasseA), CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].FullName.Should().Be("Awa Fall");
        result[0].Entries.Should().ContainSingle();
        result[1].FullName.Should().Be("Modou Diop");
        result[1].Entries.Should().ContainSingle();
    }

    [Fact]
    public async Task Never_Includes_A_Student_From_Another_School()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var handler = new GetClassQuranProgressQueryHandler(db);

        var act = async () => await handler.Handle(new GetClassQuranProgressQuery(ClasseB), CancellationToken.None);

        // ClasseB appartient à l'École B : le Global Query Filter la rend introuvable pour l'École A.
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }
}
```

- [ ] **Step 2 : Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter FullyQualifiedName~GetClassQuranProgressQueryTests`
Expected: FAIL (compilation error)

- [ ] **Step 3 : Créer la requête, le DTO de ligne et le validateur**

```csharp
// src/SamaEcole.Application/Quran/Queries/GetClassQuranProgress/GetClassQuranProgressQuery.cs
using MediatR;

namespace SamaEcole.Application.Quran.Queries.GetClassQuranProgress;

/// <summary>
/// GET /quran/progress/classroom/{classroomId} — un élève, sa liste d'observations. Pas de forme
/// fixe (contrairement à StudentGradeRowDto/Devoir1/2/Composition) : le nombre d'observations par
/// élève est libre (décision Phase 1 §3.3).
/// </summary>
public record GetClassQuranProgressQuery(Guid ClassroomId) : IRequest<IReadOnlyList<ClassQuranProgressRowDto>>;

public record ClassQuranProgressRowDto(
    Guid StudentId, string Matricule, string FullName, IReadOnlyList<QuranProgressDto> Entries);
```

```csharp
// src/SamaEcole.Application/Quran/Queries/GetClassQuranProgress/GetClassQuranProgressQueryValidator.cs
using FluentValidation;

namespace SamaEcole.Application.Quran.Queries.GetClassQuranProgress;

public class GetClassQuranProgressQueryValidator : AbstractValidator<GetClassQuranProgressQuery>
{
    public GetClassQuranProgressQueryValidator()
    {
        RuleFor(q => q.ClassroomId).NotEmpty();
    }
}
```

- [ ] **Step 4 : Créer le Handler**

```csharp
// src/SamaEcole.Application/Quran/Queries/GetClassQuranProgress/GetClassQuranProgressQueryHandler.cs
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Quran.Queries.GetClassQuranProgress;

public class GetClassQuranProgressQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetClassQuranProgressQuery, IReadOnlyList<ClassQuranProgressRowDto>>
{
    public async Task<IReadOnlyList<ClassQuranProgressRowDto>> Handle(
        GetClassQuranProgressQuery request, CancellationToken cancellationToken)
    {
        // Global Query Filter + policy RLS : une classe d'une autre école y est introuvable
        // (même idiome que GetClassGradesQueryHandler).
        if (!await dbContext.Classrooms.AnyAsync(c => c.Id == request.ClassroomId, cancellationToken))
        {
            throw new KeyNotFoundException($"Classe {request.ClassroomId} introuvable dans votre établissement.");
        }

        var students = await dbContext.Students.AsNoTracking()
            .Where(s => s.ClassroomId == request.ClassroomId)
            .OrderBy(s => s.FullName)
            .Select(s => new { s.Id, s.Matricule, s.FullName })
            .ToListAsync(cancellationToken);

        var studentIds = students.Select(s => s.Id).ToList();

        // xmin est une colonne système : EF.Property s'applique à QuranProgress directement, jamais
        // à un type anonyme projeté (même contrainte que GetClassGradesQueryHandler).
        var entries = await dbContext.QuranProgresses.AsNoTracking()
            .Where(p => studentIds.Contains(p.StudentId))
            .Select(p => new QuranProgressDto(
                p.Id, p.StudentId, p.JuzNumber, p.HizbNumber, p.SurahNumber,
                p.Status, p.EvaluationDate, p.Notes, EF.Property<uint>(p, "xmin")))
            .ToListAsync(cancellationToken);

        var byStudent = entries.ToLookup(e => e.StudentId);

        return students
            .Select(s => new ClassQuranProgressRowDto(s.Id, s.Matricule, s.FullName, byStudent[s.Id].ToList()))
            .ToList();
    }
}
```

- [ ] **Step 5 : Lancer le test pour vérifier qu'il passe**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter FullyQualifiedName~GetClassQuranProgressQueryTests`
Expected: PASS (2/2)

- [ ] **Step 6 : Commit**

```bash
git add src/SamaEcole.Application/Quran/Queries/GetClassQuranProgress/ tests/SamaEcole.IntegrationTests/Quran/GetClassQuranProgressQueryTests.cs
git commit -m "feat(quran): ajoute GetClassQuranProgressQuery"
```

---

### Task 5 : DTOs QuranEvaluation + `CreateQuranEvaluationCommand`

**Files:**
- Create: `src/SamaEcole.Application/Quran/QuranEvaluationDto.cs`
- Create: `src/SamaEcole.Application/Quran/Commands/CreateQuranEvaluation/CreateQuranEvaluationCommand.cs`
- Create: `src/SamaEcole.Application/Quran/Commands/CreateQuranEvaluation/CreateQuranEvaluationCommandHandler.cs`
- Create: `src/SamaEcole.Application/Quran/Commands/CreateQuranEvaluation/CreateQuranEvaluationCommandValidator.cs`
- Test: `tests/SamaEcole.UnitTests/Quran/CreateQuranEvaluationCommandValidatorTests.cs`
- Test: `tests/SamaEcole.IntegrationTests/Quran/CreateQuranEvaluationCommandTests.cs`

**Interfaces:**
- Produces: `QuranEvaluationDto(Guid Id, Guid StudentId, DateOnly EvaluationDate, int MemoryMistakes, int TajwidMistakes, int Hesitations, decimal FinalScore, uint RowVersion)` — consommé par Tasks 6, 7, 8, 9.
- Produces: `CreateQuranEvaluationCommand(Guid StudentId, DateOnly EvaluationDate, int MemoryMistakes, int TajwidMistakes, int Hesitations, decimal FinalScore) : IRequest<QuranEvaluationDto>, IAuditableRequest`.

- [ ] **Step 1 : Écrire le test de validateur (échoue)**

```csharp
// tests/SamaEcole.UnitTests/Quran/CreateQuranEvaluationCommandValidatorTests.cs
using SamaEcole.Application.Quran.Commands.CreateQuranEvaluation;
using FluentAssertions;
using Xunit;

namespace SamaEcole.UnitTests.Quran;

public class CreateQuranEvaluationCommandValidatorTests
{
    private readonly CreateQuranEvaluationCommandValidator _validator = new();

    private static CreateQuranEvaluationCommand Valid() => new(
        Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), MemoryMistakes: 0, TajwidMistakes: 0, Hesitations: 0, FinalScore: 18);

    [Fact]
    public void Valid_Command_Should_Pass()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_StudentId_Should_Fail()
    {
        _validator.Validate(Valid() with { StudentId = Guid.Empty }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Future_Evaluation_Date_Should_Fail()
    {
        _validator.Validate(Valid() with { EvaluationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)) }).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void Negative_Mistake_Counts_Should_Fail(int memory, int tajwid, int hesitations)
    {
        _validator.Validate(Valid() with { MemoryMistakes = memory, TajwidMistakes = tajwid, Hesitations = hesitations }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Negative_FinalScore_Should_Fail()
    {
        _validator.Validate(Valid() with { FinalScore = -1 }).IsValid.Should().BeFalse();
    }
}
```

- [ ] **Step 2 : Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test tests/SamaEcole.UnitTests --filter FullyQualifiedName~CreateQuranEvaluationCommandValidatorTests`
Expected: FAIL (compilation error)

- [ ] **Step 3 : Créer le DTO**

```csharp
// src/SamaEcole.Application/Quran/QuranEvaluationDto.cs
namespace SamaEcole.Application.Quran;

/// <summary>
/// Une évaluation orale de récitation coranique. Sert de résultat de Create/UpdateQuranEvaluationCommand
/// ET d'élément de liste — pas de type Result séparé (YAGNI, voir spec §3.4).
/// </summary>
public record QuranEvaluationDto(
    Guid Id,
    Guid StudentId,
    DateOnly EvaluationDate,
    int MemoryMistakes,
    int TajwidMistakes,
    int Hesitations,
    decimal FinalScore,
    uint RowVersion);
```

- [ ] **Step 4 : Créer la commande**

```csharp
// src/SamaEcole.Application/Quran/Commands/CreateQuranEvaluation/CreateQuranEvaluationCommand.cs
using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.Quran.Commands.CreateQuranEvaluation;

/// <summary>
/// POST /quran/evaluations — module Coran/Franco-Arabe (spec Phase 2 §3.2). Réservé au Directeur et
/// à l'Enseignant, comme CreateGradeCommand. Aucune contrainte d'unicité, aucun plafond métier sur
/// FinalScore (décision #7 — aucun barème spécifié pour l'examen oral).
///
/// IAuditableRequest (JGK-H01), comme la saisie de notes.
/// </summary>
public record CreateQuranEvaluationCommand(
    Guid StudentId,
    DateOnly EvaluationDate,
    int MemoryMistakes,
    int TajwidMistakes,
    int Hesitations,
    decimal FinalScore)
    : IRequest<QuranEvaluationDto>, IAuditableRequest;
```

- [ ] **Step 5 : Créer le validateur**

```csharp
// src/SamaEcole.Application/Quran/Commands/CreateQuranEvaluation/CreateQuranEvaluationCommandValidator.cs
using FluentValidation;

namespace SamaEcole.Application.Quran.Commands.CreateQuranEvaluation;

public class CreateQuranEvaluationCommandValidator : AbstractValidator<CreateQuranEvaluationCommand>
{
    public CreateQuranEvaluationCommandValidator()
    {
        RuleFor(c => c.StudentId).NotEmpty();

        RuleFor(c => c.EvaluationDate)
            .NotEmpty()
            .LessThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("La date d'évaluation ne peut pas être future.");

        RuleFor(c => c.MemoryMistakes).GreaterThanOrEqualTo(0);
        RuleFor(c => c.TajwidMistakes).GreaterThanOrEqualTo(0);
        RuleFor(c => c.Hesitations).GreaterThanOrEqualTo(0);
        RuleFor(c => c.FinalScore).GreaterThanOrEqualTo(0).WithMessage("Une note ne peut pas être négative.");
    }
}
```

- [ ] **Step 6 : Lancer le test de validateur pour vérifier qu'il passe**

Run: `dotnet test tests/SamaEcole.UnitTests --filter FullyQualifiedName~CreateQuranEvaluationCommandValidatorTests`
Expected: PASS (6/6)

- [ ] **Step 7 : Écrire le test d'intégration (échoue)**

```csharp
// tests/SamaEcole.IntegrationTests/Quran/CreateQuranEvaluationCommandTests.cs
using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Quran.Commands.CreateQuranEvaluation;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Quran;

public class CreateQuranEvaluationCommandTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ClasseA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid EleveA = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid ClasseB = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");
    private static readonly Guid EleveB = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });
        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.Students.AddRange(
            new Student { Id = EleveA, SchoolId = EcoleA, Matricule = "ELEV-0001", FullName = "Élève A", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = ClasseA },
            new Student { Id = EleveB, SchoolId = EcoleB, Matricule = "ELEV-0001", FullName = "Élève B", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Thiès", Gender = "F", ClassroomId = ClasseB });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Creates_An_Evaluation_For_A_Student_Of_The_Current_School()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var handler = new CreateQuranEvaluationCommandHandler(db, new StubTenantProvider(EcoleA));

        var result = await handler.Handle(
            new CreateQuranEvaluationCommand(EleveA, new DateOnly(2026, 9, 20), 1, 2, 0, 17),
            CancellationToken.None);

        result.StudentId.Should().Be(EleveA);
        result.FinalScore.Should().Be(17);
        result.RowVersion.Should().NotBe((uint)0);
    }

    [Fact]
    public async Task Rejects_A_Student_From_Another_School()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var handler = new CreateQuranEvaluationCommandHandler(db, new StubTenantProvider(EcoleA));

        var act = async () => await handler.Handle(
            new CreateQuranEvaluationCommand(EleveB, new DateOnly(2026, 9, 20), 0, 0, 0, 20),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }
}
```

- [ ] **Step 8 : Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter FullyQualifiedName~CreateQuranEvaluationCommandTests`
Expected: FAIL (compilation error)

- [ ] **Step 9 : Créer le Handler**

```csharp
// src/SamaEcole.Application/Quran/Commands/CreateQuranEvaluation/CreateQuranEvaluationCommandHandler.cs
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Quran.Commands.CreateQuranEvaluation;

public class CreateQuranEvaluationCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<CreateQuranEvaluationCommand, QuranEvaluationDto>
{
    public async Task<QuranEvaluationDto> Handle(CreateQuranEvaluationCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        if (!await dbContext.Students.AnyAsync(s => s.Id == request.StudentId, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.StudentId), "L'élève indiqué n'existe pas dans votre établissement.")
            ]);
        }

        var evaluation = new QuranEvaluation
        {
            SchoolId = schoolId,
            StudentId = request.StudentId,
            EvaluationDate = request.EvaluationDate,
            MemoryMistakes = request.MemoryMistakes,
            TajwidMistakes = request.TajwidMistakes,
            Hesitations = request.Hesitations,
            FinalScore = request.FinalScore
        };

        dbContext.QuranEvaluations.Add(evaluation);
        await dbContext.SaveChangesAsync(cancellationToken);

        var rowVersion = await dbContext.QuranEvaluations.AsNoTracking()
            .Where(e => e.Id == evaluation.Id)
            .Select(e => EF.Property<uint>(e, "xmin"))
            .FirstAsync(cancellationToken);

        return new QuranEvaluationDto(
            evaluation.Id, evaluation.StudentId, evaluation.EvaluationDate, evaluation.MemoryMistakes,
            evaluation.TajwidMistakes, evaluation.Hesitations, evaluation.FinalScore, rowVersion);
    }
}
```

- [ ] **Step 10 : Lancer le test d'intégration pour vérifier qu'il passe**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter FullyQualifiedName~CreateQuranEvaluationCommandTests`
Expected: PASS (2/2)

- [ ] **Step 11 : Commit**

```bash
git add src/SamaEcole.Application/Quran/QuranEvaluationDto.cs src/SamaEcole.Application/Quran/Commands/CreateQuranEvaluation/ tests/SamaEcole.UnitTests/Quran/CreateQuranEvaluationCommandValidatorTests.cs tests/SamaEcole.IntegrationTests/Quran/CreateQuranEvaluationCommandTests.cs
git commit -m "feat(quran): ajoute CreateQuranEvaluationCommand"
```

---

### Task 6 : `UpdateQuranEvaluationCommand`

**Files:**
- Create: `src/SamaEcole.Application/Quran/Commands/UpdateQuranEvaluation/UpdateQuranEvaluationCommand.cs`
- Create: `src/SamaEcole.Application/Quran/Commands/UpdateQuranEvaluation/UpdateQuranEvaluationCommandHandler.cs`
- Create: `src/SamaEcole.Application/Quran/Commands/UpdateQuranEvaluation/UpdateQuranEvaluationCommandValidator.cs`
- Test: `tests/SamaEcole.UnitTests/Quran/UpdateQuranEvaluationCommandValidatorTests.cs`
- Test: `tests/SamaEcole.IntegrationTests/Quran/UpdateQuranEvaluationCommandTests.cs`

**Interfaces:**
- Consumes: `QuranEvaluationDto`, `CreateQuranEvaluationCommandHandler` (Task 5, pour semer).
- Produces: `UpdateQuranEvaluationCommand(Guid Id, DateOnly EvaluationDate, int MemoryMistakes, int TajwidMistakes, int Hesitations, decimal FinalScore, uint RowVersion) : IRequest<QuranEvaluationDto>, IAuditableRequest`.

- [ ] **Step 1 : Écrire le test de validateur (échoue)**

```csharp
// tests/SamaEcole.UnitTests/Quran/UpdateQuranEvaluationCommandValidatorTests.cs
using SamaEcole.Application.Quran.Commands.UpdateQuranEvaluation;
using FluentAssertions;
using Xunit;

namespace SamaEcole.UnitTests.Quran;

public class UpdateQuranEvaluationCommandValidatorTests
{
    private readonly UpdateQuranEvaluationCommandValidator _validator = new();

    private static UpdateQuranEvaluationCommand Valid() => new(
        Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), 0, 0, 0, 18, RowVersion: 1);

    [Fact]
    public void Valid_Command_Should_Pass()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_Id_Should_Fail()
    {
        _validator.Validate(Valid() with { Id = Guid.Empty }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Negative_FinalScore_Should_Fail()
    {
        _validator.Validate(Valid() with { FinalScore = -1 }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Future_Evaluation_Date_Should_Fail()
    {
        _validator.Validate(Valid() with { EvaluationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)) }).IsValid.Should().BeFalse();
    }
}
```

- [ ] **Step 2 : Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test tests/SamaEcole.UnitTests --filter FullyQualifiedName~UpdateQuranEvaluationCommandValidatorTests`
Expected: FAIL (compilation error)

- [ ] **Step 3 : Créer la commande et son validateur**

```csharp
// src/SamaEcole.Application/Quran/Commands/UpdateQuranEvaluation/UpdateQuranEvaluationCommand.cs
using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.Quran.Commands.UpdateQuranEvaluation;

/// <summary>
/// PUT /quran/evaluations/{id} — corrige une évaluation déjà saisie. Ne touche jamais l'Élève
/// (identité de la ligne, immuable — décision #8 de la spec). RowVersion : verrou optimiste (règle #5).
/// </summary>
public record UpdateQuranEvaluationCommand(
    Guid Id,
    DateOnly EvaluationDate,
    int MemoryMistakes,
    int TajwidMistakes,
    int Hesitations,
    decimal FinalScore,
    uint RowVersion)
    : IRequest<QuranEvaluationDto>, IAuditableRequest;
```

```csharp
// src/SamaEcole.Application/Quran/Commands/UpdateQuranEvaluation/UpdateQuranEvaluationCommandValidator.cs
using FluentValidation;

namespace SamaEcole.Application.Quran.Commands.UpdateQuranEvaluation;

public class UpdateQuranEvaluationCommandValidator : AbstractValidator<UpdateQuranEvaluationCommand>
{
    public UpdateQuranEvaluationCommandValidator()
    {
        RuleFor(c => c.Id).NotEmpty();

        RuleFor(c => c.EvaluationDate)
            .NotEmpty()
            .LessThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("La date d'évaluation ne peut pas être future.");

        RuleFor(c => c.MemoryMistakes).GreaterThanOrEqualTo(0);
        RuleFor(c => c.TajwidMistakes).GreaterThanOrEqualTo(0);
        RuleFor(c => c.Hesitations).GreaterThanOrEqualTo(0);
        RuleFor(c => c.FinalScore).GreaterThanOrEqualTo(0).WithMessage("Une note ne peut pas être négative.");
    }
}
```

- [ ] **Step 4 : Lancer le test de validateur pour vérifier qu'il passe**

Run: `dotnet test tests/SamaEcole.UnitTests --filter FullyQualifiedName~UpdateQuranEvaluationCommandValidatorTests`
Expected: PASS (4/4)

- [ ] **Step 5 : Écrire le test d'intégration (échoue)**

```csharp
// tests/SamaEcole.IntegrationTests/Quran/UpdateQuranEvaluationCommandTests.cs
using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Quran.Commands.CreateQuranEvaluation;
using SamaEcole.Application.Quran.Commands.UpdateQuranEvaluation;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Quran;

public class UpdateQuranEvaluationCommandTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid Eleve = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.Students.Add(new Student { Id = Eleve, SchoolId = Ecole, Matricule = "ELEV-0001", FullName = "Élève de test", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = Classe });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static async Task<QuranEvaluationDto> SeedEvaluationAsync(SamaEcole.Persistence.ApplicationDbContext db) =>
        await new CreateQuranEvaluationCommandHandler(db, new StubTenantProvider(Ecole)).Handle(
            new CreateQuranEvaluationCommand(Eleve, new DateOnly(2026, 9, 20), 1, 1, 1, 15),
            CancellationToken.None);

    [Fact]
    public async Task Corrects_The_Final_Score()
    {
        await using var seed = _db.NewAppContext(Ecole);
        var created = await SeedEvaluationAsync(seed);

        await using var db = _db.NewAppContext(Ecole);
        var handler = new UpdateQuranEvaluationCommandHandler(db);

        var result = await handler.Handle(
            new UpdateQuranEvaluationCommand(created.Id, created.EvaluationDate, 0, 0, 0, 19, created.RowVersion),
            CancellationToken.None);

        result.FinalScore.Should().Be(19);
        result.StudentId.Should().Be(Eleve);
    }

    [Fact]
    public async Task Unknown_Id_Is_Not_Found()
    {
        await using var db = _db.NewAppContext(Ecole);
        var handler = new UpdateQuranEvaluationCommandHandler(db);

        var act = async () => await handler.Handle(
            new UpdateQuranEvaluationCommand(Guid.NewGuid(), new DateOnly(2026, 9, 20), 0, 0, 0, 15, RowVersion: 1),
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Stale_RowVersion_Is_Refused_With_A_Conflict()
    {
        await using var seed = _db.NewAppContext(Ecole);
        var created = await SeedEvaluationAsync(seed);

        await using var db1 = _db.NewAppContext(Ecole);
        await new UpdateQuranEvaluationCommandHandler(db1).Handle(
            new UpdateQuranEvaluationCommand(created.Id, created.EvaluationDate, 0, 0, 0, 19, created.RowVersion),
            CancellationToken.None);

        await using var db2 = _db.NewAppContext(Ecole);
        var handler2 = new UpdateQuranEvaluationCommandHandler(db2);

        var act = async () => await handler2.Handle(
            new UpdateQuranEvaluationCommand(created.Id, created.EvaluationDate, 2, 2, 2, 12, created.RowVersion),
            CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }
}
```

- [ ] **Step 6 : Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter FullyQualifiedName~UpdateQuranEvaluationCommandTests`
Expected: FAIL (compilation error)

- [ ] **Step 7 : Créer le Handler**

```csharp
// src/SamaEcole.Application/Quran/Commands/UpdateQuranEvaluation/UpdateQuranEvaluationCommandHandler.cs
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Quran.Commands.UpdateQuranEvaluation;

public class UpdateQuranEvaluationCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<UpdateQuranEvaluationCommand, QuranEvaluationDto>
{
    public async Task<QuranEvaluationDto> Handle(UpdateQuranEvaluationCommand request, CancellationToken cancellationToken)
    {
        var evaluation = await dbContext.QuranEvaluations
            .FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Évaluation coranique {request.Id} introuvable.");

        dbContext.SetOriginalConcurrencyToken(evaluation, request.RowVersion);

        evaluation.EvaluationDate = request.EvaluationDate;
        evaluation.MemoryMistakes = request.MemoryMistakes;
        evaluation.TajwidMistakes = request.TajwidMistakes;
        evaluation.Hesitations = request.Hesitations;
        evaluation.FinalScore = request.FinalScore;

        await dbContext.SaveChangesAsync(cancellationToken);

        var rowVersion = await dbContext.QuranEvaluations.AsNoTracking()
            .Where(e => e.Id == evaluation.Id)
            .Select(e => EF.Property<uint>(e, "xmin"))
            .FirstAsync(cancellationToken);

        return new QuranEvaluationDto(
            evaluation.Id, evaluation.StudentId, evaluation.EvaluationDate, evaluation.MemoryMistakes,
            evaluation.TajwidMistakes, evaluation.Hesitations, evaluation.FinalScore, rowVersion);
    }
}
```

- [ ] **Step 8 : Lancer le test d'intégration pour vérifier qu'il passe**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter FullyQualifiedName~UpdateQuranEvaluationCommandTests`
Expected: PASS (3/3)

- [ ] **Step 9 : Commit**

```bash
git add src/SamaEcole.Application/Quran/Commands/UpdateQuranEvaluation/ tests/SamaEcole.UnitTests/Quran/UpdateQuranEvaluationCommandValidatorTests.cs tests/SamaEcole.IntegrationTests/Quran/UpdateQuranEvaluationCommandTests.cs
git commit -m "feat(quran): ajoute UpdateQuranEvaluationCommand"
```

---

### Task 7 : `GetStudentQuranEvaluationsQuery`

**Files:**
- Create: `src/SamaEcole.Application/Quran/Queries/GetStudentQuranEvaluations/GetStudentQuranEvaluationsQuery.cs`
- Create: `src/SamaEcole.Application/Quran/Queries/GetStudentQuranEvaluations/GetStudentQuranEvaluationsQueryHandler.cs`
- Create: `src/SamaEcole.Application/Quran/Queries/GetStudentQuranEvaluations/GetStudentQuranEvaluationsQueryValidator.cs`
- Test: `tests/SamaEcole.IntegrationTests/Quran/GetStudentQuranEvaluationsQueryTests.cs`

**Interfaces:**
- Consumes: `QuranEvaluationDto`, `CreateQuranEvaluationCommandHandler` (Task 5).
- Produces: `GetStudentQuranEvaluationsQuery(Guid StudentId) : IRequest<IReadOnlyList<QuranEvaluationDto>>`.

- [ ] **Step 1 : Écrire le test d'intégration (échoue)**

```csharp
// tests/SamaEcole.IntegrationTests/Quran/GetStudentQuranEvaluationsQueryTests.cs
using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Quran.Commands.CreateQuranEvaluation;
using SamaEcole.Application.Quran.Queries.GetStudentQuranEvaluations;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Quran;

public class GetStudentQuranEvaluationsQueryTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid EleveA = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid EleveB = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.Students.AddRange(
            new Student { Id = EleveA, SchoolId = Ecole, Matricule = "ELEV-0001", FullName = "Élève A", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = Classe },
            new Student { Id = EleveB, SchoolId = Ecole, Matricule = "ELEV-0002", FullName = "Élève B", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Returns_Only_The_Requested_Students_Evaluations()
    {
        await using var seed = _db.NewAppContext(Ecole);
        var createHandler = new CreateQuranEvaluationCommandHandler(seed, new StubTenantProvider(Ecole));
        await createHandler.Handle(new CreateQuranEvaluationCommand(EleveA, new DateOnly(2026, 9, 1), 1, 1, 1, 15), CancellationToken.None);
        await createHandler.Handle(new CreateQuranEvaluationCommand(EleveA, new DateOnly(2026, 9, 15), 0, 0, 0, 18), CancellationToken.None);
        await createHandler.Handle(new CreateQuranEvaluationCommand(EleveB, new DateOnly(2026, 9, 1), 2, 2, 2, 12), CancellationToken.None);

        await using var db = _db.NewAppContext(Ecole);
        var handler = new GetStudentQuranEvaluationsQueryHandler(db);

        var result = await handler.Handle(new GetStudentQuranEvaluationsQuery(EleveA), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(e => e.StudentId == EleveA);
    }

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }
}
```

- [ ] **Step 2 : Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter FullyQualifiedName~GetStudentQuranEvaluationsQueryTests`
Expected: FAIL (compilation error)

- [ ] **Step 3 : Créer la requête, le validateur et le Handler**

```csharp
// src/SamaEcole.Application/Quran/Queries/GetStudentQuranEvaluations/GetStudentQuranEvaluationsQuery.cs
using MediatR;

namespace SamaEcole.Application.Quran.Queries.GetStudentQuranEvaluations;

/// <summary>GET /quran/evaluations?studentId= — historique complet des évaluations orales d'un élève.</summary>
public record GetStudentQuranEvaluationsQuery(Guid StudentId) : IRequest<IReadOnlyList<QuranEvaluationDto>>;
```

```csharp
// src/SamaEcole.Application/Quran/Queries/GetStudentQuranEvaluations/GetStudentQuranEvaluationsQueryValidator.cs
using FluentValidation;

namespace SamaEcole.Application.Quran.Queries.GetStudentQuranEvaluations;

public class GetStudentQuranEvaluationsQueryValidator : AbstractValidator<GetStudentQuranEvaluationsQuery>
{
    public GetStudentQuranEvaluationsQueryValidator()
    {
        RuleFor(q => q.StudentId).NotEmpty();
    }
}
```

```csharp
// src/SamaEcole.Application/Quran/Queries/GetStudentQuranEvaluations/GetStudentQuranEvaluationsQueryHandler.cs
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Quran.Queries.GetStudentQuranEvaluations;

public class GetStudentQuranEvaluationsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetStudentQuranEvaluationsQuery, IReadOnlyList<QuranEvaluationDto>>
{
    public async Task<IReadOnlyList<QuranEvaluationDto>> Handle(
        GetStudentQuranEvaluationsQuery request, CancellationToken cancellationToken)
    {
        return await dbContext.QuranEvaluations.AsNoTracking()
            .Where(e => e.StudentId == request.StudentId)
            .OrderBy(e => e.EvaluationDate)
            .Select(e => new QuranEvaluationDto(
                e.Id, e.StudentId, e.EvaluationDate, e.MemoryMistakes, e.TajwidMistakes,
                e.Hesitations, e.FinalScore, EF.Property<uint>(e, "xmin")))
            .ToListAsync(cancellationToken);
    }
}
```

- [ ] **Step 4 : Lancer le test pour vérifier qu'il passe**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter FullyQualifiedName~GetStudentQuranEvaluationsQueryTests`
Expected: PASS (1/1)

- [ ] **Step 5 : Commit**

```bash
git add src/SamaEcole.Application/Quran/Queries/GetStudentQuranEvaluations/ tests/SamaEcole.IntegrationTests/Quran/GetStudentQuranEvaluationsQueryTests.cs
git commit -m "feat(quran): ajoute GetStudentQuranEvaluationsQuery"
```

---

### Task 8 : `GetClassQuranEvaluationsQuery`

**Files:**
- Create: `src/SamaEcole.Application/Quran/Queries/GetClassQuranEvaluations/GetClassQuranEvaluationsQuery.cs`
- Create: `src/SamaEcole.Application/Quran/Queries/GetClassQuranEvaluations/GetClassQuranEvaluationsQueryHandler.cs`
- Create: `src/SamaEcole.Application/Quran/Queries/GetClassQuranEvaluations/GetClassQuranEvaluationsQueryValidator.cs`
- Test: `tests/SamaEcole.IntegrationTests/Quran/GetClassQuranEvaluationsQueryTests.cs`

**Interfaces:**
- Consumes: `QuranEvaluationDto`, `CreateQuranEvaluationCommandHandler` (Task 5).
- Produces: `ClassQuranEvaluationRowDto(Guid StudentId, string Matricule, string FullName, IReadOnlyList<QuranEvaluationDto> Entries)`, `GetClassQuranEvaluationsQuery(Guid ClassroomId) : IRequest<IReadOnlyList<ClassQuranEvaluationRowDto>>`.

- [ ] **Step 1 : Écrire le test d'intégration (échoue)**

```csharp
// tests/SamaEcole.IntegrationTests/Quran/GetClassQuranEvaluationsQueryTests.cs
using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Quran.Commands.CreateQuranEvaluation;
using SamaEcole.Application.Quran.Queries.GetClassQuranEvaluations;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Quran;

public class GetClassQuranEvaluationsQueryTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ClasseA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid ClasseB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid EleveA = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });
        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "CM2", Level = "Primaire", Capacity = 40 });
        owner.Students.Add(
            new Student { Id = EleveA, SchoolId = EcoleA, Matricule = "ELEV-0001", FullName = "Awa Fall", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Returns_One_Row_Per_Student_With_Their_Evaluations()
    {
        await using var seed = _db.NewAppContext(EcoleA);
        await new CreateQuranEvaluationCommandHandler(seed, new StubTenantProvider(EcoleA)).Handle(
            new CreateQuranEvaluationCommand(EleveA, new DateOnly(2026, 9, 1), 1, 1, 1, 15), CancellationToken.None);

        await using var db = _db.NewAppContext(EcoleA);
        var handler = new GetClassQuranEvaluationsQueryHandler(db);

        var result = await handler.Handle(new GetClassQuranEvaluationsQuery(ClasseA), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].FullName.Should().Be("Awa Fall");
        result[0].Entries.Should().ContainSingle();
    }

    [Fact]
    public async Task Never_Includes_A_Classroom_From_Another_School()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var handler = new GetClassQuranEvaluationsQueryHandler(db);

        var act = async () => await handler.Handle(new GetClassQuranEvaluationsQuery(ClasseB), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }
}
```

- [ ] **Step 2 : Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter FullyQualifiedName~GetClassQuranEvaluationsQueryTests`
Expected: FAIL (compilation error)

- [ ] **Step 3 : Créer la requête, le DTO de ligne, le validateur et le Handler**

```csharp
// src/SamaEcole.Application/Quran/Queries/GetClassQuranEvaluations/GetClassQuranEvaluationsQuery.cs
using MediatR;

namespace SamaEcole.Application.Quran.Queries.GetClassQuranEvaluations;

public record GetClassQuranEvaluationsQuery(Guid ClassroomId) : IRequest<IReadOnlyList<ClassQuranEvaluationRowDto>>;

public record ClassQuranEvaluationRowDto(
    Guid StudentId, string Matricule, string FullName, IReadOnlyList<QuranEvaluationDto> Entries);
```

```csharp
// src/SamaEcole.Application/Quran/Queries/GetClassQuranEvaluations/GetClassQuranEvaluationsQueryValidator.cs
using FluentValidation;

namespace SamaEcole.Application.Quran.Queries.GetClassQuranEvaluations;

public class GetClassQuranEvaluationsQueryValidator : AbstractValidator<GetClassQuranEvaluationsQuery>
{
    public GetClassQuranEvaluationsQueryValidator()
    {
        RuleFor(q => q.ClassroomId).NotEmpty();
    }
}
```

```csharp
// src/SamaEcole.Application/Quran/Queries/GetClassQuranEvaluations/GetClassQuranEvaluationsQueryHandler.cs
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Quran.Queries.GetClassQuranEvaluations;

public class GetClassQuranEvaluationsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetClassQuranEvaluationsQuery, IReadOnlyList<ClassQuranEvaluationRowDto>>
{
    public async Task<IReadOnlyList<ClassQuranEvaluationRowDto>> Handle(
        GetClassQuranEvaluationsQuery request, CancellationToken cancellationToken)
    {
        if (!await dbContext.Classrooms.AnyAsync(c => c.Id == request.ClassroomId, cancellationToken))
        {
            throw new KeyNotFoundException($"Classe {request.ClassroomId} introuvable dans votre établissement.");
        }

        var students = await dbContext.Students.AsNoTracking()
            .Where(s => s.ClassroomId == request.ClassroomId)
            .OrderBy(s => s.FullName)
            .Select(s => new { s.Id, s.Matricule, s.FullName })
            .ToListAsync(cancellationToken);

        var studentIds = students.Select(s => s.Id).ToList();

        var evaluations = await dbContext.QuranEvaluations.AsNoTracking()
            .Where(e => studentIds.Contains(e.StudentId))
            .Select(e => new QuranEvaluationDto(
                e.Id, e.StudentId, e.EvaluationDate, e.MemoryMistakes, e.TajwidMistakes,
                e.Hesitations, e.FinalScore, EF.Property<uint>(e, "xmin")))
            .ToListAsync(cancellationToken);

        var byStudent = evaluations.ToLookup(e => e.StudentId);

        return students
            .Select(s => new ClassQuranEvaluationRowDto(s.Id, s.Matricule, s.FullName, byStudent[s.Id].ToList()))
            .ToList();
    }
}
```

- [ ] **Step 4 : Lancer le test pour vérifier qu'il passe**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter FullyQualifiedName~GetClassQuranEvaluationsQueryTests`
Expected: PASS (2/2)

- [ ] **Step 5 : Commit**

```bash
git add src/SamaEcole.Application/Quran/Queries/GetClassQuranEvaluations/ tests/SamaEcole.IntegrationTests/Quran/GetClassQuranEvaluationsQueryTests.cs
git commit -m "feat(quran): ajoute GetClassQuranEvaluationsQuery"
```

---

### Task 9 : `QuranController` + tests fonctionnels

**Files:**
- Create: `src/SamaEcole.Web/Controllers/QuranController.cs`
- Test: `tests/SamaEcole.FunctionalTests/Quran/QuranModuleGateTests.cs`
- Test: `tests/SamaEcole.FunctionalTests/Quran/QuranEndpointsTests.cs`

**Interfaces:**
- Consumes : les 4 Commands et 4 Queries des Tasks 1-8, `RequireModuleAttribute`/`SchoolModule.Coran` (déjà existants).

- [ ] **Step 1 : Écrire le test de garde de module (échoue — le contrôleur n'existe pas)**

```csharp
// tests/SamaEcole.FunctionalTests/Quran/QuranModuleGateTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Quran;

/// <summary>
/// Le module Coran est DÉSACTIVÉ par défaut (SchoolSettingsDefaults.IsCoranModuleEnabled = false) —
/// même patron que InternatModuleGateTests : preuve du refus PAR DÉFAUT, pas seulement après
/// désactivation explicite.
/// </summary>
public class QuranModuleGateTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);
    private record ApiError(string Code, string Message);

    private async Task<Tokens> LoginAsDirecteurAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = AuthApiFactory.DirecteurEmail, password = AuthApiFactory.DirecteurPassword });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!;
    }

    private async Task<HttpResponseMessage> GetAsync(string path, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _client.SendAsync(request);
    }

    private static object ValidBody(bool isCoranModuleEnabled) => new
    {
        gradingScale = "20",
        studentMatriculeFormat = "ELEV-{YEAR}-{SEQ:4}",
        teacherMatriculeFormat = "ENS-{YEAR}-{SEQ:3}",
        autoLogoutMinutes = 10,
        dateFormat = "dd/MM/yyyy",
        tuitionMonthsPerYear = 9,
        allowSecretaryToManageGrading = false,
        isPedagogyEnabled = true,
        isFinanceEnabled = true,
        isInternatEnabled = false,
        isCoranModuleEnabled
    };

    private async Task<HttpResponseMessage> PutSettingsAsync(string accessToken, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/schools/current/settings")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Progress_Endpoint_Is_Forbidden_By_Default_With_MODULE_DISABLED()
    {
        var directeur = await LoginAsDirecteurAsync();

        var response = await GetAsync($"/api/v1/quran/progress?studentId={Guid.NewGuid()}", directeur.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var error = (await response.Content.ReadFromJsonAsync<ApiError>())!;
        error.Code.Should().Be("MODULE_DISABLED");
    }

    [Fact]
    public async Task Enabling_The_Module_Grants_Access()
    {
        var directeur = await LoginAsDirecteurAsync();

        (await PutSettingsAsync(directeur.AccessToken, ValidBody(isCoranModuleEnabled: true)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await GetAsync($"/api/v1/quran/progress?studentId={Guid.NewGuid()}", directeur.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

- [ ] **Step 2 : Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test tests/SamaEcole.FunctionalTests --filter FullyQualifiedName~QuranModuleGateTests`
Expected: FAIL (404 — la route `/api/v1/quran/progress` n'existe pas encore)

- [ ] **Step 3 : Créer `QuranController`**

```csharp
// src/SamaEcole.Web/Controllers/QuranController.cs
using SamaEcole.Application.Quran.Commands.CreateQuranEvaluation;
using SamaEcole.Application.Quran.Commands.CreateQuranProgress;
using SamaEcole.Application.Quran.Commands.UpdateQuranEvaluation;
using SamaEcole.Application.Quran.Commands.UpdateQuranProgress;
using SamaEcole.Application.Quran.Queries.GetClassQuranEvaluations;
using SamaEcole.Application.Quran.Queries.GetClassQuranProgress;
using SamaEcole.Application.Quran.Queries.GetStudentQuranEvaluations;
using SamaEcole.Application.Quran.Queries.GetStudentQuranProgress;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Module Coran/Franco-Arabe (spec Phase 2, docs/superpowers/specs/2026-09-20-franco-arabic-cqrs-api-design.md).
/// Contrôleur mince, aucune logique métier (AGENTS.md règle #8). `[RequireModule(SchoolModule.Coran)]`
/// est la SEULE garde nécessaire (décision #5 de la spec) : contrairement à Internat, aucune donnée
/// Coran ne vit sur une entité partagée accessible par un autre chemin.
/// </summary>
[ApiController]
[Route("api/v1/quran")]
[Authorize]
[RequireModule(SchoolModule.Coran)]
public class QuranController(ISender mediator) : ControllerBase
{
    public record CreateProgressRequest(
        Guid StudentId, int JuzNumber, int HizbNumber, int SurahNumber,
        QuranMemorizationStatus Status, DateOnly? EvaluationDate, string? Notes);

    public record UpdateProgressRequest(
        QuranMemorizationStatus Status, DateOnly? EvaluationDate, string? Notes, uint RowVersion);

    public record CreateEvaluationRequest(
        Guid StudentId, DateOnly EvaluationDate, int MemoryMistakes, int TajwidMistakes, int Hesitations, decimal FinalScore);

    public record UpdateEvaluationRequest(
        DateOnly EvaluationDate, int MemoryMistakes, int TajwidMistakes, int Hesitations, decimal FinalScore, uint RowVersion);

    /// <summary>Écriture (Create/Update) : Directeur + Enseignant, comme GradesController.GradingRoles (décision #1).</summary>
    private const string WriteRoles = $"{nameof(Role.Directeur)},{nameof(Role.Enseignant)}";

    /// <summary>Lecture : Directeur + Enseignant + Secrétariat, comme GradesController.ViewGradesRoles (décision #2).</summary>
    private const string ReadRoles = $"{nameof(Role.Directeur)},{nameof(Role.Enseignant)},{nameof(Role.Secretariat)}";

    // ---------------------------------------------------------------- QuranProgress

    [HttpGet("progress")]
    [Authorize(Roles = ReadRoles)]
    [ProducesResponseType<IReadOnlyList<QuranProgressDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStudentProgress([FromQuery] Guid studentId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetStudentQuranProgressQuery(studentId), cancellationToken));

    [HttpGet("progress/classroom/{classroomId:guid}")]
    [Authorize(Roles = ReadRoles)]
    [ProducesResponseType<IReadOnlyList<ClassQuranProgressRowDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetClassProgress(Guid classroomId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetClassQuranProgressQuery(classroomId), cancellationToken));

    [HttpPost("progress")]
    [Authorize(Roles = WriteRoles)]
    [ProducesResponseType<QuranProgressDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateProgress([FromBody] CreateProgressRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new CreateQuranProgressCommand(
                request.StudentId, request.JuzNumber, request.HizbNumber, request.SurahNumber,
                request.Status, request.EvaluationDate, request.Notes),
            cancellationToken);

        return CreatedAtAction(nameof(GetStudentProgress), new { studentId = result.StudentId }, result);
    }

    [HttpPut("progress/{id:guid}")]
    [Authorize(Roles = WriteRoles)]
    [ProducesResponseType<QuranProgressDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateProgress(Guid id, [FromBody] UpdateProgressRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new UpdateQuranProgressCommand(id, request.Status, request.EvaluationDate, request.Notes, request.RowVersion),
            cancellationToken));

    // ---------------------------------------------------------------- QuranEvaluation

    [HttpGet("evaluations")]
    [Authorize(Roles = ReadRoles)]
    [ProducesResponseType<IReadOnlyList<QuranEvaluationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStudentEvaluations([FromQuery] Guid studentId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetStudentQuranEvaluationsQuery(studentId), cancellationToken));

    [HttpGet("evaluations/classroom/{classroomId:guid}")]
    [Authorize(Roles = ReadRoles)]
    [ProducesResponseType<IReadOnlyList<ClassQuranEvaluationRowDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetClassEvaluations(Guid classroomId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetClassQuranEvaluationsQuery(classroomId), cancellationToken));

    [HttpPost("evaluations")]
    [Authorize(Roles = WriteRoles)]
    [ProducesResponseType<QuranEvaluationDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateEvaluation([FromBody] CreateEvaluationRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new CreateQuranEvaluationCommand(
                request.StudentId, request.EvaluationDate, request.MemoryMistakes,
                request.TajwidMistakes, request.Hesitations, request.FinalScore),
            cancellationToken);

        return CreatedAtAction(nameof(GetStudentEvaluations), new { studentId = result.StudentId }, result);
    }

    [HttpPut("evaluations/{id:guid}")]
    [Authorize(Roles = WriteRoles)]
    [ProducesResponseType<QuranEvaluationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateEvaluation(Guid id, [FromBody] UpdateEvaluationRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new UpdateQuranEvaluationCommand(
                id, request.EvaluationDate, request.MemoryMistakes, request.TajwidMistakes,
                request.Hesitations, request.FinalScore, request.RowVersion),
            cancellationToken));
}
```

- [ ] **Step 4 : Lancer le test de garde de module pour vérifier qu'il passe**

Run: `dotnet test tests/SamaEcole.FunctionalTests --filter FullyQualifiedName~QuranModuleGateTests`
Expected: PASS (2/2)

- [ ] **Step 5 : Écrire le test de rôles (échoue tant que non vérifié — cette étape teste, elle ne devrait PAS échouer si l'implémentation ci-dessus est correcte ; sert de filet)**

```csharp
// tests/SamaEcole.FunctionalTests/Quran/QuranEndpointsTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Quran;

public class QuranEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);

    private async Task<string> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private static object ValidSettingsBody() => new
    {
        gradingScale = "20",
        studentMatriculeFormat = "ELEV-{YEAR}-{SEQ:4}",
        teacherMatriculeFormat = "ENS-{YEAR}-{SEQ:3}",
        autoLogoutMinutes = 10,
        dateFormat = "dd/MM/yyyy",
        tuitionMonthsPerYear = 9,
        allowSecretaryToManageGrading = false,
        isPedagogyEnabled = true,
        isFinanceEnabled = true,
        isInternatEnabled = false,
        isCoranModuleEnabled = true
    };

    private async Task EnableModuleAsync(string directeurToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/schools/current/settings")
        {
            Content = JsonContent.Create(ValidSettingsBody())
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", directeurToken);
        (await _client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string accessToken, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Secretariat_Cannot_Create_A_Progress_Entry()
    {
        var directeur = await LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
        await EnableModuleAsync(directeur);
        var secretariat = await LoginAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/quran/progress", secretariat, new
        {
            studentId = Guid.NewGuid(), juzNumber = 1, hizbNumber = 1, surahNumber = 1,
            status = "InProcess", evaluationDate = (DateOnly?)null, notes = (string?)null
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Secretariat_Can_Read_The_Progress_List()
    {
        var directeur = await LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
        await EnableModuleAsync(directeur);
        var secretariat = await LoginAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        var response = await SendAsync(HttpMethod.Get, $"/api/v1/quran/progress?studentId={Guid.NewGuid()}", secretariat);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Enseignant_Can_Create_An_Evaluation()
    {
        var directeur = await LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
        await EnableModuleAsync(directeur);
        var enseignant = await LoginAsync(AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword);

        // AuthApiFactory ne crée pas d'élève par défaut : un StudentId inconnu doit être refusé en
        // 422 (élève introuvable), preuve que le rôle Enseignant a bien franchi la garde 403 pour
        // atteindre le Handler.
        var response = await SendAsync(HttpMethod.Post, "/api/v1/quran/evaluations", enseignant, new
        {
            studentId = Guid.NewGuid(), evaluationDate = DateOnly.FromDateTime(DateTime.UtcNow),
            memoryMistakes = 0, tajwidMistakes = 0, hesitations = 0, finalScore = 18
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
```

- [ ] **Step 6 : Lancer les deux fichiers de tests fonctionnels**

Run: `dotnet test tests/SamaEcole.FunctionalTests --filter FullyQualifiedName~Quran`
Expected: PASS (5/5)

- [ ] **Step 7 : Commit**

```bash
git add src/SamaEcole.Web/Controllers/QuranController.cs tests/SamaEcole.FunctionalTests/Quran/
git commit -m "feat(quran): ajoute QuranController"
```

---

### Task 10 : Documentation `openapi.yaml`

**Files:**
- Modify: `openapi.yaml`

**Interfaces:** aucune — documentation pure, aucun code ne dépend de ce fichier.

- [ ] **Step 1 : Ajouter le tag**

Dans la liste des tags (près de la ligne 46, à côté de `- name: ClassJournal`), ajouter :

```yaml
  - name: Quran
```

- [ ] **Step 2 : Ajouter les chemins**

Après la section `/class-journal/{id}` (juste avant le prochain tag de section), ajouter :

```yaml
  /quran/progress:
    get:
      tags: [Quran]
      summary: Historique de suivi de mémorisation d'un élève (Directeur, Enseignant, Secrétariat)
      parameters:
        - name: studentId
          in: query
          required: true
          schema: { type: string, format: uuid }
      responses:
        '200':
          description: Liste des observations
          content:
            application/json:
              schema:
                type: array
                items: { $ref: '#/components/schemas/QuranProgress' }
    post:
      tags: [Quran]
      summary: Créer une observation de suivi coranique (Directeur, Enseignant)
      description: >-
        Aucune contrainte d'unicité : plusieurs observations pour le même élève/sourate sont
        légitimes au fil du temps.
      requestBody:
        required: true
        content:
          application/json:
            schema: { $ref: '#/components/schemas/QuranProgressCreateRequest' }
      responses:
        '201':
          description: Observation créée
          content:
            application/json:
              schema: { $ref: '#/components/schemas/QuranProgress' }
        '403': { $ref: '#/components/responses/Forbidden' }
        '422': { $ref: '#/components/responses/ValidationError' }

  /quran/progress/classroom/{classroomId}:
    get:
      tags: [Quran]
      summary: Suivi coranique de tous les élèves d'une classe (Directeur, Enseignant, Secrétariat)
      parameters:
        - name: classroomId
          in: path
          required: true
          schema: { type: string, format: uuid }
      responses:
        '200':
          description: Une ligne par élève, avec la liste de ses observations
          content:
            application/json:
              schema:
                type: array
                items: { $ref: '#/components/schemas/ClassQuranProgressRow' }
        '404': { $ref: '#/components/responses/NotFound' }

  /quran/progress/{id}:
    put:
      tags: [Quran]
      summary: Corriger une observation — statut/date/notes seulement (Directeur, Enseignant)
      description: >-
        Juz, Hizb, sourate et élève ne sont jamais modifiables (identité de la ligne, immuable).
      parameters:
        - name: id
          in: path
          required: true
          schema: { type: string, format: uuid }
      requestBody:
        required: true
        content:
          application/json:
            schema: { $ref: '#/components/schemas/QuranProgressUpdateRequest' }
      responses:
        '200':
          description: Observation corrigée
          content:
            application/json:
              schema: { $ref: '#/components/schemas/QuranProgress' }
        '403': { $ref: '#/components/responses/Forbidden' }
        '404': { $ref: '#/components/responses/NotFound' }
        '409': { description: RowVersion périmé (la ligne a été modifiée entre-temps) }
        '422': { $ref: '#/components/responses/ValidationError' }

  /quran/evaluations:
    get:
      tags: [Quran]
      summary: Historique des évaluations orales d'un élève (Directeur, Enseignant, Secrétariat)
      parameters:
        - name: studentId
          in: query
          required: true
          schema: { type: string, format: uuid }
      responses:
        '200':
          description: Liste des évaluations
          content:
            application/json:
              schema:
                type: array
                items: { $ref: '#/components/schemas/QuranEvaluation' }
    post:
      tags: [Quran]
      summary: Enregistrer une évaluation orale de récitation (Directeur, Enseignant)
      requestBody:
        required: true
        content:
          application/json:
            schema: { $ref: '#/components/schemas/QuranEvaluationCreateRequest' }
      responses:
        '201':
          description: Évaluation créée
          content:
            application/json:
              schema: { $ref: '#/components/schemas/QuranEvaluation' }
        '403': { $ref: '#/components/responses/Forbidden' }
        '422': { $ref: '#/components/responses/ValidationError' }

  /quran/evaluations/classroom/{classroomId}:
    get:
      tags: [Quran]
      summary: Évaluations orales de tous les élèves d'une classe (Directeur, Enseignant, Secrétariat)
      parameters:
        - name: classroomId
          in: path
          required: true
          schema: { type: string, format: uuid }
      responses:
        '200':
          description: Une ligne par élève, avec la liste de ses évaluations
          content:
            application/json:
              schema:
                type: array
                items: { $ref: '#/components/schemas/ClassQuranEvaluationRow' }
        '404': { $ref: '#/components/responses/NotFound' }

  /quran/evaluations/{id}:
    put:
      tags: [Quran]
      summary: Corriger une évaluation orale déjà saisie (Directeur, Enseignant)
      parameters:
        - name: id
          in: path
          required: true
          schema: { type: string, format: uuid }
      requestBody:
        required: true
        content:
          application/json:
            schema: { $ref: '#/components/schemas/QuranEvaluationUpdateRequest' }
      responses:
        '200':
          description: Évaluation corrigée
          content:
            application/json:
              schema: { $ref: '#/components/schemas/QuranEvaluation' }
        '403': { $ref: '#/components/responses/Forbidden' }
        '404': { $ref: '#/components/responses/NotFound' }
        '409': { description: RowVersion périmé (la ligne a été modifiée entre-temps) }
        '422': { $ref: '#/components/responses/ValidationError' }
```

- [ ] **Step 3 : Ajouter les schémas**

Après la section des schémas `ClassJournalEntry*` (près de la ligne 6954), ajouter :

```yaml
    QuranProgress:
      type: object
      properties:
        id: { type: string, format: uuid }
        studentId: { type: string, format: uuid }
        juzNumber: { type: integer, minimum: 1, maximum: 30 }
        hizbNumber: { type: integer, minimum: 1, maximum: 60 }
        surahNumber: { type: integer, minimum: 1, maximum: 114 }
        status: { type: string, enum: [InProcess, Memorized, Revised] }
        evaluationDate: { type: string, format: date, nullable: true }
        notes: { type: string, nullable: true }
        rowVersion: { type: integer, format: int64 }
    QuranProgressCreateRequest:
      type: object
      required: [studentId, juzNumber, hizbNumber, surahNumber, status]
      properties:
        studentId: { type: string, format: uuid }
        juzNumber: { type: integer, minimum: 1, maximum: 30 }
        hizbNumber: { type: integer, minimum: 1, maximum: 60 }
        surahNumber: { type: integer, minimum: 1, maximum: 114 }
        status: { type: string, enum: [InProcess, Memorized, Revised] }
        evaluationDate: { type: string, format: date, nullable: true }
        notes: { type: string, nullable: true }
    QuranProgressUpdateRequest:
      type: object
      required: [status, rowVersion]
      description: Juz, Hizb, sourate et élève ne se corrigent jamais ici.
      properties:
        status: { type: string, enum: [InProcess, Memorized, Revised] }
        evaluationDate: { type: string, format: date, nullable: true }
        notes: { type: string, nullable: true }
        rowVersion: { type: integer, format: int64 }
    ClassQuranProgressRow:
      type: object
      properties:
        studentId: { type: string, format: uuid }
        matricule: { type: string }
        fullName: { type: string }
        entries:
          type: array
          items: { $ref: '#/components/schemas/QuranProgress' }
    QuranEvaluation:
      type: object
      properties:
        id: { type: string, format: uuid }
        studentId: { type: string, format: uuid }
        evaluationDate: { type: string, format: date }
        memoryMistakes: { type: integer, minimum: 0 }
        tajwidMistakes: { type: integer, minimum: 0 }
        hesitations: { type: integer, minimum: 0 }
        finalScore: { type: number, minimum: 0 }
        rowVersion: { type: integer, format: int64 }
    QuranEvaluationCreateRequest:
      type: object
      required: [studentId, evaluationDate, memoryMistakes, tajwidMistakes, hesitations, finalScore]
      properties:
        studentId: { type: string, format: uuid }
        evaluationDate: { type: string, format: date }
        memoryMistakes: { type: integer, minimum: 0 }
        tajwidMistakes: { type: integer, minimum: 0 }
        hesitations: { type: integer, minimum: 0 }
        finalScore: { type: number, minimum: 0 }
    QuranEvaluationUpdateRequest:
      type: object
      required: [evaluationDate, memoryMistakes, tajwidMistakes, hesitations, finalScore, rowVersion]
      description: L'élève ne se corrige jamais ici.
      properties:
        evaluationDate: { type: string, format: date }
        memoryMistakes: { type: integer, minimum: 0 }
        tajwidMistakes: { type: integer, minimum: 0 }
        hesitations: { type: integer, minimum: 0 }
        finalScore: { type: number, minimum: 0 }
        rowVersion: { type: integer, format: int64 }
    ClassQuranEvaluationRow:
      type: object
      properties:
        studentId: { type: string, format: uuid }
        matricule: { type: string }
        fullName: { type: string }
        entries:
          type: array
          items: { $ref: '#/components/schemas/QuranEvaluation' }
```

- [ ] **Step 4 : Vérifier que le fichier reste un YAML valide**

Run: `dotnet build` (le serveur charge `openapi.yaml` au démarrage via Swashbuckle/NSwag si configuré — un YAML invalide fait échouer le build ou le démarrage ; à défaut, une simple validation manuelle de l'indentation suffit puisque ce fichier n'est pas généré).

- [ ] **Step 5 : Commit**

```bash
git add openapi.yaml
git commit -m "docs(quran): documente les routes du module Coran/Franco-Arabe dans openapi.yaml"
```

---

### Task 11 : Vérification finale

**Files:** aucun changement de fichier.

- [ ] **Step 1 : Suite ciblée**

Run: `dotnet test --filter "FullyQualifiedName~Quran"`
Expected: tous les tests Quran (unitaires + intégration + fonctionnels) au vert.

- [ ] **Step 2 : Catégorie MultiTenant**

Run: `dotnet test --filter Category=MultiTenant`
Expected: 0 échec lié à Quran. Un échec sur un test SANS RAPPORT (déjà observé comme flaky sous forte charge de conteneurs en Phase 1) se revérifie seul avant d'être traité comme une régression.

- [ ] **Step 3 : Suite complète**

Run: `dotnet test`
Expected: 0 échec (hors flakiness déjà caractérisée et non liée à ce lot).

## Self-Review

**1. Couverture de la spec** — §3.1 → Tasks 1-2, §3.2 (queries) → Tasks 3-4, §3.2 QuranEvaluation →
Tasks 5-6, queries → Tasks 7-8, §4 (sécurité/contrôleur) → Task 9, §3.6/openapi → Task 10, §5 (tests)
→ répartis dans chaque tâche + Task 11.

**2. Placeholders** — aucun ; chaque étape contient le code réel.

**3. Cohérence des types** — `QuranProgressDto`/`QuranEvaluationDto` gardent la même forme entre
leur définition (Tasks 1/5) et leur usage dans les Handlers de requête (Tasks 3/4/7/8) et le
contrôleur (Task 9) ; `ClassQuranProgressRowDto`/`ClassQuranEvaluationRowDto` définis une seule fois
chacun (Tasks 4/8) et réutilisés tels quels dans `QuranController`.
