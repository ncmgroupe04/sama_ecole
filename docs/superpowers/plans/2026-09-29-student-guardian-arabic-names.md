# Noms bilingues Élève & Tuteur — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add two nullable Arabic mirror fields — `Student.FullNameAr` and `Student.GuardianNameAr` — across the domain model, EF configuration/migration, read/write CQRS, CSV bulk import, and the Razor/Alpine.js UI, following the existing `Subject.NameAr` precedent exactly.

**Architecture:** Purely additive vertical slices. Each task adds the two fields to one layer (or one read/write path) and proves it with a test before moving to the next. No existing field, endpoint, or the 364 existing `Student.FullName` call sites are touched.

**Tech Stack:** ASP.NET Core 9 (C#), EF Core + Npgsql, MediatR (CQRS), FluentValidation, ClosedXML (Excel import/template), Razor views + Alpine.js + Tailwind, xUnit + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-09-29-student-guardian-arabic-names-design.md`

## Global Constraints

- Both new fields are `string?`, `HasMaxLength(200)`, no `.NotEmpty()` — optional, unlike `FullName`/`BirthPlace`.
- No traduction automatique : saisie libre, jamais une valeur dérivée du champ FR.
- Never apply `.ToTitleCase()` (fr-FR-specific casing) to the Arabic fields — mirror `Subject.NameAr`'s `Trimmed()` helper instead (whitespace-only → `null`, otherwise `.Trim()`).
- No RLS/migration risk: `students` is already in `TenantTables`; this adds two nullable columns only.
- None of the 364 existing `FullName` call sites, `StudentsExportModel.cs`, `GetStudentsExportPdfQueryHandler.cs`, or `ReportCardDocument` are touched (out of scope — separate specs).
- Integration/functional tests need a live Postgres: run `docker compose up -d` once before Tasks 1, 2, 3, 4, 6 test steps if not already running.
- Migration command: `dotnet ef migrations add <Name> -p src/SamaEcole.Persistence -s src/SamaEcole.Web`.

---

## Task 1: Domain + Persistence + Migration + `GetStudentDetailQuery`

**Files:**
- Modify: `src/SamaEcole.Domain/Entities/Student.cs`
- Modify: `src/SamaEcole.Persistence/Configurations/StudentConfiguration.cs`
- Create: `src/SamaEcole.Persistence/Migrations/<timestamp>_AddStudentArabicNames.cs` (generated)
- Modify: `src/SamaEcole.Application/Students/Queries/GetStudentDetail/GetStudentDetailQuery.cs`
- Test: `tests/SamaEcole.IntegrationTests/Students/GetStudentDetailQueryTests.cs`

**Interfaces:**
- Produces: `Student.FullNameAr` (`string?`), `Student.GuardianNameAr` (`string?`) — consumed by every later task.
- Produces: `StudentIdentityDto.FullNameAr`, `StudentIdentityDto.GuardianNameAr` (positional record, appended right after `GuardianEmail`/before `RowVersion`... see exact position below) — consumed by Task 7 (UI detail view).

- [ ] **Step 1: Write the failing test**

In `tests/SamaEcole.IntegrationTests/Students/GetStudentDetailQueryTests.cs`, find the `EleveEcoleB` seed (around line 102-106):

```csharp
            new Student
            {
                Id = EleveEcoleB, SchoolId = EcoleB, Matricule = "ELEV-2026-0001", FullName = "Modou Diop",
                BirthDate = new DateOnly(2014, 8, 2), BirthPlace = "Dakar", Gender = "M", ClassroomId = ClasseB
            },
```

Replace it with (adding the two new fields to the existing seed — this does not change any other test's assertions):

```csharp
            new Student
            {
                Id = EleveEcoleB, SchoolId = EcoleB, Matricule = "ELEV-2026-0001", FullName = "Modou Diop",
                FullNameAr = "مودو ديوب", GuardianNameAr = "فاطمة ديوب",
                BirthDate = new DateOnly(2014, 8, 2), BirthPlace = "Dakar", Gender = "M", ClassroomId = ClasseB
            },
```

Then add a new fact right after `Handle_Returns_The_Student_When_Called_From_Its_Own_School` (after line 186):

```csharp

    [Fact]
    public async Task Handle_Exposes_The_Arabic_Mirror_Names_When_Set()
    {
        await using var db = _db.NewAppContext(EcoleB);
        var handler = new GetStudentDetailQueryHandler(db, new FakeCurrentUserService(Role.Directeur), new CoefficientOverrideLoader(db));

        var detail = await handler.Handle(new GetStudentDetailQuery(EleveEcoleB), CancellationToken.None);

        detail.Identity.FullNameAr.Should().Be("مودو ديوب");
        detail.Identity.GuardianNameAr.Should().Be("فاطمة ديوب");
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SamaEcole.IntegrationTests/SamaEcole.IntegrationTests.csproj --filter "FullyQualifiedName~GetStudentDetailQueryTests"`
Expected: build FAILS — `Student` has no `FullNameAr`/`GuardianNameAr`, `StudentIdentityDto` has no such members (CS0117/CS1729).

- [ ] **Step 3: Add the fields to `Student.cs`**

In `src/SamaEcole.Domain/Entities/Student.cs`, after `public string? Address { get; set; }` (the last line before the closing brace), add:

```csharp

    /// <summary>
    /// Nom complet de l'élève en arabe (module Franco-Arabe), saisi librement par l'école — aucune
    /// traduction automatique. Null tant que personne ne l'a renseigné : même principe que
    /// <see cref="Subject.NameAr"/>, jamais une valeur inventée.
    /// </summary>
    public string? FullNameAr { get; set; }

    /// <summary>Miroir arabe de <see cref="GuardianName"/> — mêmes règles que <see cref="FullNameAr"/>.</summary>
    public string? GuardianNameAr { get; set; }
```

(`Subject` resolves without qualification: both `Student` and `Subject` live in `SamaEcole.Domain.Entities`.)

- [ ] **Step 4: Add the EF configuration**

In `src/SamaEcole.Persistence/Configurations/StudentConfiguration.cs`, after the line `builder.Property(s => s.Address).HasMaxLength(300);`, add:

```csharp
        builder.Property(s => s.FullNameAr).HasMaxLength(200);
        builder.Property(s => s.GuardianNameAr).HasMaxLength(200);
```

- [ ] **Step 5: Generate the migration**

Run: `dotnet ef migrations add AddStudentArabicNames -p src/SamaEcole.Persistence -s src/SamaEcole.Web`
Expected: creates `src/SamaEcole.Persistence/Migrations/<timestamp>_AddStudentArabicNames.cs` and its `.Designer.cs`, and updates `ApplicationDbContextModelSnapshot.cs`. Open the generated `Up()` and confirm it contains exactly two `migrationBuilder.AddColumn<string>(name: "FullNameAr", table: "students", type: "character varying(200)", maxLength: 200, nullable: true)`-shaped calls (one per column) and nothing else — if EF also emitted unrelated column changes, STOP and investigate before continuing (the model snapshot may be out of sync with a prior uncommitted change).

- [ ] **Step 6: Apply the migration to the local database**

Run: `dotnet ef database update -p src/SamaEcole.Persistence -s src/SamaEcole.Web`
Expected: `Applying migration '<timestamp>_AddStudentArabicNames'.` then success.

- [ ] **Step 7: Add the fields to `StudentIdentityDto` and both construction sites**

In `src/SamaEcole.Application/Students/Queries/GetStudentDetail/GetStudentDetailQuery.cs`:

a) In the `StudentIdentityDto` record (around line 78), change:

```csharp
    string? GuardianName,
    string? GuardianPhone,
    string? GuardianEmail,
    string? Address,
    uint RowVersion,
```

to:

```csharp
    string? GuardianName,
    string? GuardianPhone,
    string? GuardianEmail,
    string? Address,
    string? FullNameAr,
    string? GuardianNameAr,
    uint RowVersion,
```

b) In the EF projection inside `Handle` (around line 194-197), change:

```csharp
                s.GuardianName,
                s.GuardianPhone,
                s.GuardianEmail,
                s.Address,
                RowVersion = EF.Property<uint>(s, "xmin"),
```

to:

```csharp
                s.GuardianName,
                s.GuardianPhone,
                s.GuardianEmail,
                s.Address,
                s.FullNameAr,
                s.GuardianNameAr,
                RowVersion = EF.Property<uint>(s, "xmin"),
```

c) In the manual `StudentIdentityDto` construction (around line 250-254), change:

```csharp
            student.GuardianName,
            student.GuardianPhone,
            student.GuardianEmail,
            student.Address,
            student.RowVersion,
```

to:

```csharp
            student.GuardianName,
            student.GuardianPhone,
            student.GuardianEmail,
            student.Address,
            student.FullNameAr,
            student.GuardianNameAr,
            student.RowVersion,
```

- [ ] **Step 8: Run the test to verify it passes**

Run: `dotnet test tests/SamaEcole.IntegrationTests/SamaEcole.IntegrationTests.csproj --filter "FullyQualifiedName~GetStudentDetailQueryTests"`
Expected: PASS, all facts in the file green (the new one plus the existing ones — the seed edit in Step 1 must not have broken any of them, since none assert on the whole `Student` object).

- [ ] **Step 9: Commit**

```bash
git add src/SamaEcole.Domain/Entities/Student.cs src/SamaEcole.Persistence/Configurations/StudentConfiguration.cs src/SamaEcole.Persistence/Migrations/ src/SamaEcole.Application/Students/Queries/GetStudentDetail/GetStudentDetailQuery.cs tests/SamaEcole.IntegrationTests/Students/GetStudentDetailQueryTests.cs
git commit -m "feat(students): ajoute FullNameAr/GuardianNameAr (domaine, persistence, fiche détail)"
```

---

## Task 2: `GetStudentsQuery` / `StudentListItem`

**Files:**
- Modify: `src/SamaEcole.Application/Students/Queries/GetStudents/GetStudentsQuery.cs`
- Modify: `src/SamaEcole.Application/Students/Queries/GetStudents/GetStudentsQueryHandler.cs`
- Test: `tests/SamaEcole.IntegrationTests/Students/GetStudentsQueryHandlerTests.cs`

**Interfaces:**
- Consumes: `Student.FullNameAr`/`GuardianNameAr` (Task 1).
- Produces: `StudentListItem.FullNameAr`, `StudentListItem.GuardianNameAr` — consumed by Task 7 (the detail modal's `detailStudent` binding, per the spec's §4.2 correction: the detail view's guardian panel reads from the list item, not from `GetStudentDetailQuery`).

- [ ] **Step 1: Write the failing test**

In `tests/SamaEcole.IntegrationTests/Students/GetStudentsQueryHandlerTests.cs`, add a new fact after the class's existing facts (find the end of the last `[Fact]` method and insert before the closing `}` of the class):

```csharp

    [Fact]
    public async Task Items_Expose_The_Arabic_Mirror_Names_When_Set()
    {
        var eleveId = Guid.Parse("11111111-0000-0000-0000-0000000000a9");

        await using (var owner = _db.NewOwnerContext())
        {
            owner.Students.Add(new Student
            {
                Id = eleveId, SchoolId = EcoleA, Matricule = "ELEV-2026-0099", FullName = "Fatou Sarr",
                FullNameAr = "فاتو سار", GuardianNameAr = "عمر سار",
                BirthDate = new DateOnly(2015, 5, 5), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA
            });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        await using var db = _db.NewAppContext(EcoleA);
        var handler = new GetStudentsQueryHandler(db);

        var result = await handler.Handle(new GetStudentsQuery { PageSize = 50 }, CancellationToken.None);

        var item = result.Items.Should().ContainSingle(i => i.Id == eleveId).Subject;
        item.FullNameAr.Should().Be("فاتو سار");
        item.GuardianNameAr.Should().Be("عمر سار");
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SamaEcole.IntegrationTests/SamaEcole.IntegrationTests.csproj --filter "FullyQualifiedName~GetStudentsQueryHandlerTests"`
Expected: build FAILS — `StudentListItem` has no `FullNameAr`/`GuardianNameAr` members (CS1061).

- [ ] **Step 3: Add the fields to `StudentListItem`**

In `src/SamaEcole.Application/Students/Queries/GetStudents/GetStudentsQuery.cs`, change the end of the `StudentListItem` record (currently ending `string? GuardianEmail,\n    string? Address);`):

```csharp
    string? GuardianName,
    string? GuardianPhone,
    string? GuardianEmail,
    string? Address);
```

to:

```csharp
    string? GuardianName,
    string? GuardianPhone,
    string? GuardianEmail,
    string? Address,
    string? FullNameAr,
    string? GuardianNameAr);
```

- [ ] **Step 4: Add the fields to the handler's projection and construction**

In `src/SamaEcole.Application/Students/Queries/GetStudents/GetStudentsQueryHandler.cs`:

a) In the `.Select(s => new { ... })` projection (around line 116-121), change:

```csharp
                s.PhotoUrl,
                s.PhotoData,
                s.GuardianName,
                s.GuardianPhone,
                s.GuardianEmail,
                s.Address
            })
```

to:

```csharp
                s.PhotoUrl,
                s.PhotoData,
                s.GuardianName,
                s.GuardianPhone,
                s.GuardianEmail,
                s.Address,
                s.FullNameAr,
                s.GuardianNameAr
            })
```

b) In the `StudentListItem` construction (around line 126-140), change:

```csharp
                r.GuardianName,
                r.GuardianPhone,
                r.GuardianEmail,
                r.Address))
```

to:

```csharp
                r.GuardianName,
                r.GuardianPhone,
                r.GuardianEmail,
                r.Address,
                r.FullNameAr,
                r.GuardianNameAr))
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/SamaEcole.IntegrationTests/SamaEcole.IntegrationTests.csproj --filter "FullyQualifiedName~GetStudentsQueryHandlerTests"`
Expected: PASS, all facts green.

- [ ] **Step 6: Commit**

```bash
git add src/SamaEcole.Application/Students/Queries/GetStudents/ tests/SamaEcole.IntegrationTests/Students/GetStudentsQueryHandlerTests.cs
git commit -m "feat(students): expose FullNameAr/GuardianNameAr sur StudentListItem"
```

---

## Task 3: `CreateStudentCommand`

**Files:**
- Modify: `src/SamaEcole.Application/Students/Commands/CreateStudent/CreateStudentCommand.cs`
- Modify: `src/SamaEcole.Application/Students/Commands/CreateStudent/CreateStudentCommandHandler.cs`
- Modify: `src/SamaEcole.Application/Students/Commands/CreateStudent/CreateStudentCommandValidator.cs`
- Test: `tests/SamaEcole.UnitTests/Students/CreateStudentCommandValidatorTests.cs`
- Test: `tests/SamaEcole.FunctionalTests/Students/StudentsEndpointsTests.cs`

**Interfaces:**
- Consumes: `Student.FullNameAr`/`GuardianNameAr` (Task 1).
- Produces: `CreateStudentCommand.FullNameAr`, `CreateStudentCommand.GuardianNameAr` (`string?`, `init`-only properties).

- [ ] **Step 1: Write the failing unit test**

In `tests/SamaEcole.UnitTests/Students/CreateStudentCommandValidatorTests.cs`, add after the last test method (before the closing `}` of the class):

```csharp

    [Fact]
    public void Should_Fail_When_FullNameAr_Exceeds_200_Characters()
    {
        var command = new CreateStudentCommand
        {
            FullName = "Awa Fall",
            BirthDate = new DateOnly(2015, 3, 12),
            BirthPlace = "Dakar",
            Gender = "F",
            ClassroomId = Guid.NewGuid(),
            FullNameAr = new string('ا', 201)
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.FullNameAr));
    }

    [Fact]
    public void Should_Succeed_When_FullNameAr_And_GuardianNameAr_Are_Absent()
    {
        // Facultatifs, contrairement à FullName : un élève sans nom arabe doit rester enregistrable.
        var command = new CreateStudentCommand
        {
            FullName = "Awa Fall",
            BirthDate = new DateOnly(2015, 3, 12),
            BirthPlace = "Dakar",
            Gender = "F",
            ClassroomId = Guid.NewGuid()
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SamaEcole.UnitTests/SamaEcole.UnitTests.csproj --filter "FullyQualifiedName~CreateStudentCommandValidatorTests"`
Expected: build FAILS — `CreateStudentCommand` has no `FullNameAr` member (CS0117).

- [ ] **Step 3: Add the fields to `CreateStudentCommand`**

In `src/SamaEcole.Application/Students/Commands/CreateStudent/CreateStudentCommand.cs`, change:

```csharp
    public string? GuardianName { get; init; }
    public string? GuardianPhone { get; init; }
    public string? GuardianEmail { get; init; }
    public string? Address { get; init; }
}
```

to:

```csharp
    public string? GuardianName { get; init; }
    public string? GuardianPhone { get; init; }
    public string? GuardianEmail { get; init; }
    public string? Address { get; init; }
    public string? FullNameAr { get; init; }
    public string? GuardianNameAr { get; init; }
}
```

- [ ] **Step 4: Add the validator rules**

In `src/SamaEcole.Application/Students/Commands/CreateStudent/CreateStudentCommandValidator.cs`, after the `RuleFor(x => x.Address).MaximumLength(300).NoHtml();` line, add:

```csharp
        RuleFor(x => x.FullNameAr).MaximumLength(200).NoHtml();
        RuleFor(x => x.GuardianNameAr).MaximumLength(200).NoHtml();
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/SamaEcole.UnitTests/SamaEcole.UnitTests.csproj --filter "FullyQualifiedName~CreateStudentCommandValidatorTests"`
Expected: PASS, all facts green.

- [ ] **Step 6: Assign the fields in the handler**

In `src/SamaEcole.Application/Students/Commands/CreateStudent/CreateStudentCommandHandler.cs`, add a private static `Trimmed` helper (mirroring `CreateSubjectCommandHandler`) and use it — change the `Student` object initializer:

```csharp
            var student = new Student
            {
                SchoolId = schoolId,
                Matricule = matricule,
                FullName = request.FullName.ToTitleCase(),
                BirthDate = request.BirthDate,
                BirthPlace = request.BirthPlace,
                Gender = request.Gender,
                ClassroomId = request.ClassroomId,
                PhotoUrl = request.PhotoUrl,
                PhotoData = request.PhotoData is null ? null : Convert.FromBase64String(request.PhotoData),
                GuardianName = request.GuardianName.ToTitleCase(),
                GuardianPhone = request.GuardianPhone,
                GuardianEmail = request.GuardianEmail,
                Address = request.Address
            };
```

to:

```csharp
            var student = new Student
            {
                SchoolId = schoolId,
                Matricule = matricule,
                FullName = request.FullName.ToTitleCase(),
                BirthDate = request.BirthDate,
                BirthPlace = request.BirthPlace,
                Gender = request.Gender,
                ClassroomId = request.ClassroomId,
                PhotoUrl = request.PhotoUrl,
                PhotoData = request.PhotoData is null ? null : Convert.FromBase64String(request.PhotoData),
                GuardianName = request.GuardianName.ToTitleCase(),
                GuardianPhone = request.GuardianPhone,
                GuardianEmail = request.GuardianEmail,
                Address = request.Address,
                FullNameAr = Trimmed(request.FullNameAr),
                GuardianNameAr = Trimmed(request.GuardianNameAr)
            };
```

Then add the helper as a new private static method on `CreateStudentCommandHandler` (after the closing brace of `Handle`, before the closing brace of the class):

```csharp

    /// <summary>Vide/blanc = « pas de nom arabe » (null), jamais une chaîne vide stockée — même
    /// convention que CreateSubjectCommandHandler.Trimmed pour Subject.NameAr. Jamais .ToTitleCase()
    /// (casse fr-FR) sur de l'arabe, qui n'a pas de notion de casse.</summary>
    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
```

- [ ] **Step 7: Write the failing functional round-trip test**

In `tests/SamaEcole.FunctionalTests/Students/StudentsEndpointsTests.cs`, first extend the local `StudentIdentityDto` record (around line 38-41) to add the two fields:

```csharp
    private record StudentIdentityDto(
        Guid Id, string Matricule, string FullName, DateOnly BirthDate, string? BirthPlace, string Gender,
        Guid ClassroomId, string ClassroomName, string? PhotoUrl, string? GuardianName, string? GuardianPhone,
        string? FullNameAr, string? GuardianNameAr, uint RowVersion);
```

Then add a new fact after `An_Enseignant_Must_Not_Create_A_Student` (after its closing `}`, around line 132):

```csharp

    [Fact]
    public async Task Creating_A_Student_With_Arabic_Names_Round_Trips_Them_Through_The_Detail_Endpoint()
    {
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CM2 Test Arabe");

        var response = await SendAsync(HttpMethod.Post, "/api/v1/students", directeur, new
        {
            fullName = "Ibrahima Ndoye",
            birthDate = "2015-03-12",
            birthPlace = "Thiès",
            gender = "M",
            classroomId = classroom.Id,
            fullNameAr = "  إبراهيما ندوي  ",
            guardianNameAr = "خديجة ندوي"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<StudentCreated>())!;

        var identity = await FetchStudentAsync(directeur, created.Id);
        identity.FullNameAr.Should().Be("إبراهيما ندوي", "le nom arabe doit être détouré (Trim) comme les autres champs texte");
        identity.GuardianNameAr.Should().Be("خديجة ندوي");
    }
```

- [ ] **Step 8: Run the tests to verify they fail, then pass**

Run: `dotnet test tests/SamaEcole.FunctionalTests/SamaEcole.FunctionalTests.csproj --filter "FullyQualifiedName~StudentsEndpointsTests"`
Expected first: build FAILS if Steps 3/6 above were skipped, or the new fact fails with a JSON mismatch if the DTO/handler wiring is incomplete. After Steps 3-6 are in place: PASS, all facts green.

- [ ] **Step 9: Commit**

```bash
git add src/SamaEcole.Application/Students/Commands/CreateStudent/ tests/SamaEcole.UnitTests/Students/CreateStudentCommandValidatorTests.cs tests/SamaEcole.FunctionalTests/Students/StudentsEndpointsTests.cs
git commit -m "feat(students): FullNameAr/GuardianNameAr sur CreateStudentCommand"
```

---

## Task 4: `UpdateStudentCommand`

**Files:**
- Modify: `src/SamaEcole.Application/Students/Commands/UpdateStudent/UpdateStudentCommand.cs`
- Modify: `src/SamaEcole.Application/Students/Commands/UpdateStudent/UpdateStudentCommandHandler.cs`
- Modify: `src/SamaEcole.Application/Students/Commands/UpdateStudent/UpdateStudentCommandValidator.cs`
- Modify: `src/SamaEcole.Web/Controllers/StudentsController.cs`
- Create: `tests/SamaEcole.UnitTests/Students/UpdateStudentCommandValidatorTests.cs`
- Modify: `tests/SamaEcole.FunctionalTests/Students/StudentsEndpointsTests.cs`

**Interfaces:**
- Consumes: `Student.FullNameAr`/`GuardianNameAr` (Task 1), the `Trimmed` helper pattern from Task 3 (duplicated locally — `UpdateStudentCommandHandler` has no shared base with `CreateStudentCommandHandler`).
- Produces: `UpdateStudentCommand.FullNameAr`, `UpdateStudentCommand.GuardianNameAr` (positional, inserted after `Address`, before `RowVersion`), `StudentsController.UpdateStudentRequest.FullNameAr`/`GuardianNameAr` (same position).

`UpdateStudentCommand` and `UpdateStudentRequest` are **positional records** with exactly one construction call site (`StudentsController.Update`). This task must update the record, the request DTO, and that call site together or the build breaks.

- [ ] **Step 1: Write the failing unit test**

Create `tests/SamaEcole.UnitTests/Students/UpdateStudentCommandValidatorTests.cs`:

```csharp
using FluentAssertions;
using SamaEcole.Application.Students.Commands.UpdateStudent;
using Xunit;

namespace SamaEcole.UnitTests.Students;

public class UpdateStudentCommandValidatorTests
{
    private readonly UpdateStudentCommandValidator _validator = new();

    private static UpdateStudentCommand ValidCommand(string? fullNameAr = null, string? guardianNameAr = null) =>
        new(
            Guid.NewGuid(), "Awa Fall", new DateOnly(2015, 3, 12), "Dakar", "F", Guid.NewGuid(),
            PhotoUrl: null, GuardianName: null, GuardianPhone: null, GuardianEmail: null, Address: null,
            FullNameAr: fullNameAr, GuardianNameAr: guardianNameAr, RowVersion: 1);

    [Fact]
    public void Should_Succeed_When_FullNameAr_And_GuardianNameAr_Are_Absent()
    {
        var result = _validator.Validate(ValidCommand());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Should_Fail_When_GuardianNameAr_Exceeds_200_Characters()
    {
        var command = ValidCommand(guardianNameAr: new string('ا', 201));

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.GuardianNameAr));
    }

    [Fact]
    public void Should_Fail_When_FullNameAr_Contains_Html()
    {
        var command = ValidCommand(fullNameAr: "<img src=x onerror=alert(1)>");

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.FullNameAr));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SamaEcole.UnitTests/SamaEcole.UnitTests.csproj --filter "FullyQualifiedName~UpdateStudentCommandValidatorTests"`
Expected: build FAILS — `UpdateStudentCommand`'s positional constructor doesn't accept `FullNameAr`/`GuardianNameAr` named arguments (CS1739).

- [ ] **Step 3: Add the fields to `UpdateStudentCommand`**

In `src/SamaEcole.Application/Students/Commands/UpdateStudent/UpdateStudentCommand.cs`, change:

```csharp
public record UpdateStudentCommand(
    Guid Id,
    string FullName,
    DateOnly BirthDate,
    string BirthPlace,
    string Gender,
    Guid ClassroomId,
    string? PhotoUrl,
    string? GuardianName,
    string? GuardianPhone,
    string? GuardianEmail,
    string? Address,
    uint RowVersion) : IRequest<UpdateStudentResult>;
```

to:

```csharp
public record UpdateStudentCommand(
    Guid Id,
    string FullName,
    DateOnly BirthDate,
    string BirthPlace,
    string Gender,
    Guid ClassroomId,
    string? PhotoUrl,
    string? GuardianName,
    string? GuardianPhone,
    string? GuardianEmail,
    string? Address,
    string? FullNameAr,
    string? GuardianNameAr,
    uint RowVersion) : IRequest<UpdateStudentResult>;
```

- [ ] **Step 4: Add the validator rules**

In `src/SamaEcole.Application/Students/Commands/UpdateStudent/UpdateStudentCommandValidator.cs`, after `RuleFor(x => x.Address).MaximumLength(300).NoHtml();`, add:

```csharp
        RuleFor(x => x.FullNameAr).MaximumLength(200).NoHtml();
        RuleFor(x => x.GuardianNameAr).MaximumLength(200).NoHtml();
```

- [ ] **Step 5: Fix the one call site — `StudentsController.cs`**

In `src/SamaEcole.Web/Controllers/StudentsController.cs`, the `UpdateStudentRequest` record (around line 39-50):

```csharp
    public record UpdateStudentRequest(
        string FullName,
        DateOnly BirthDate,
        string BirthPlace,
        string Gender,
        Guid ClassroomId,
        string? PhotoUrl,
        string? GuardianName,
        string? GuardianPhone,
        string? GuardianEmail,
        string? Address,
        uint RowVersion);
```

becomes:

```csharp
    public record UpdateStudentRequest(
        string FullName,
        DateOnly BirthDate,
        string BirthPlace,
        string Gender,
        Guid ClassroomId,
        string? PhotoUrl,
        string? GuardianName,
        string? GuardianPhone,
        string? GuardianEmail,
        string? Address,
        string? FullNameAr,
        string? GuardianNameAr,
        uint RowVersion);
```

And the `Update` action (around line 176-183):

```csharp
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateStudentRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new UpdateStudentCommand(
                id, request.FullName, request.BirthDate, request.BirthPlace, request.Gender,
                request.ClassroomId, request.PhotoUrl, request.GuardianName, request.GuardianPhone,
                request.GuardianEmail, request.Address, request.RowVersion),
            cancellationToken));
```

becomes:

```csharp
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateStudentRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new UpdateStudentCommand(
                id, request.FullName, request.BirthDate, request.BirthPlace, request.Gender,
                request.ClassroomId, request.PhotoUrl, request.GuardianName, request.GuardianPhone,
                request.GuardianEmail, request.Address, request.FullNameAr, request.GuardianNameAr,
                request.RowVersion),
            cancellationToken));
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/SamaEcole.UnitTests/SamaEcole.UnitTests.csproj --filter "FullyQualifiedName~UpdateStudentCommandValidatorTests"`
Expected: PASS, all three facts green.

- [ ] **Step 7: Assign the fields in the handler**

In `src/SamaEcole.Application/Students/Commands/UpdateStudent/UpdateStudentCommandHandler.cs`, change:

```csharp
        student.FullName = request.FullName.ToTitleCase();
        student.BirthDate = request.BirthDate;
        student.BirthPlace = request.BirthPlace;
        student.Gender = request.Gender;
        student.ClassroomId = request.ClassroomId;
        student.PhotoUrl = request.PhotoUrl;
        student.GuardianName = request.GuardianName.ToTitleCase();
        student.GuardianPhone = request.GuardianPhone;
        student.GuardianEmail = request.GuardianEmail;
        student.Address = request.Address;
```

to:

```csharp
        student.FullName = request.FullName.ToTitleCase();
        student.BirthDate = request.BirthDate;
        student.BirthPlace = request.BirthPlace;
        student.Gender = request.Gender;
        student.ClassroomId = request.ClassroomId;
        student.PhotoUrl = request.PhotoUrl;
        student.GuardianName = request.GuardianName.ToTitleCase();
        student.GuardianPhone = request.GuardianPhone;
        student.GuardianEmail = request.GuardianEmail;
        student.Address = request.Address;
        student.FullNameAr = Trimmed(request.FullNameAr);
        student.GuardianNameAr = Trimmed(request.GuardianNameAr);
```

Then add the same helper as in Task 3 (this handler has no shared base with `CreateStudentCommandHandler`, so it is duplicated locally — same convention, same doc comment), after the closing brace of `Handle`, before the closing brace of the class:

```csharp

    /// <summary>Vide/blanc = « pas de nom arabe » (null), jamais une chaîne vide stockée — même
    /// convention que CreateStudentCommandHandler.Trimmed / CreateSubjectCommandHandler.Trimmed.
    /// Jamais .ToTitleCase() (casse fr-FR) sur de l'arabe, qui n'a pas de notion de casse.</summary>
    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
```

- [ ] **Step 8: Write the failing functional round-trip test**

In `tests/SamaEcole.FunctionalTests/Students/StudentsEndpointsTests.cs`, add a new fact after `Creating_A_Student_With_Arabic_Names_Round_Trips_Them_Through_The_Detail_Endpoint` (from Task 3):

```csharp

    [Fact]
    public async Task Updating_A_Student_Persists_The_Arabic_Mirror_Names()
    {
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CM2 Test Update Arabe");
        var created = await CreateStudentAsync(directeur, classroom.Id, "Seydou Ba");
        var before = await FetchStudentAsync(directeur, created.Id);

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/students/{created.Id}", directeur, new
        {
            fullName = before.FullName,
            birthDate = before.BirthDate,
            birthPlace = before.BirthPlace,
            gender = before.Gender,
            classroomId = before.ClassroomId,
            fullNameAr = "سيدو با",
            guardianNameAr = "آمينة با",
            rowVersion = before.RowVersion
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await FetchStudentAsync(directeur, created.Id);
        after.FullNameAr.Should().Be("سيدو با");
        after.GuardianNameAr.Should().Be("آمينة با");
    }
```

- [ ] **Step 9: Run the tests to verify they fail, then pass**

Run: `dotnet test tests/SamaEcole.FunctionalTests/SamaEcole.FunctionalTests.csproj --filter "FullyQualifiedName~StudentsEndpointsTests"`
Expected first: build FAILS or the new fact fails until Steps 3/5/7 are all in place. After: PASS, all facts green (including Task 3's fact).

- [ ] **Step 10: Commit**

```bash
git add src/SamaEcole.Application/Students/Commands/UpdateStudent/ src/SamaEcole.Web/Controllers/StudentsController.cs tests/SamaEcole.UnitTests/Students/UpdateStudentCommandValidatorTests.cs tests/SamaEcole.FunctionalTests/Students/StudentsEndpointsTests.cs
git commit -m "feat(students): FullNameAr/GuardianNameAr sur UpdateStudentCommand"
```

---

## Task 5: CSV/Excel import — `StudentImportFileRow` + `StudentImportFileParser`

**Files:**
- Modify: `src/SamaEcole.Application/Students/StudentImportFileRow.cs`
- Modify: `src/SamaEcole.Infrastructure/Files/StudentImportFileParser.cs`
- Test: `tests/SamaEcole.UnitTests/Students/StudentImportFileParserTests.cs`

**Interfaces:**
- Produces: `StudentImportFileRow.FullNameAr` (`string`, position 10), `StudentImportFileRow.GuardianNameAr` (`string`, position 11) — consumed by Task 6. Both are **non-nullable `string`** (empty string when absent), matching every other column on this record — nullability is handled downstream in `ImportStudentsCommandHandler.ValidateRow`, never on the raw parsed row.

- [ ] **Step 1: Write the failing test**

In `tests/SamaEcole.UnitTests/Students/StudentImportFileParserTests.cs`, add after `Missing_Trailing_Optional_Columns_Are_Padded_With_Empty_Strings` (after its closing `}`, around line 62):

```csharp

    [Fact]
    public void The_Two_Trailing_Arabic_Name_Columns_Are_Read_When_Present()
    {
        var rows = _parser.Parse(
            Csv($"{Header};NomAr;TuteurAr\nAwa Ndiaye;12/03/2015;Dakar;F;CM2 A;Moussa;+221771234567;;;أوا نداي;موسى نداي"),
            "eleves.csv");

        rows.Should().ContainSingle();
        rows[0].FullNameAr.Should().Be("أوا نداي");
        rows[0].GuardianNameAr.Should().Be("موسى نداي");
    }

    [Fact]
    public void The_Two_Trailing_Arabic_Name_Columns_Default_To_Empty_When_The_File_Predates_Them()
    {
        // Compatibilité ascendante : un fichier à 9 colonnes (sans les deux colonnes arabes) reste
        // importable, comme le garantit déjà le padding testé par Missing_Trailing_Optional_Columns...
        var rows = _parser.Parse(Csv($"{Header}\nAwa Ndiaye;12/03/2015;Dakar;F;CM2 A"), "eleves.csv");

        rows.Should().ContainSingle();
        rows[0].FullNameAr.Should().Be("");
        rows[0].GuardianNameAr.Should().Be("");
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/SamaEcole.UnitTests/SamaEcole.UnitTests.csproj --filter "FullyQualifiedName~StudentImportFileParserTests"`
Expected: build FAILS — `StudentImportFileRow` has no `FullNameAr`/`GuardianNameAr` members (CS1061).

- [ ] **Step 3: Add the fields to `StudentImportFileRow`**

In `src/SamaEcole.Application/Students/StudentImportFileRow.cs`, change:

```csharp
public record StudentImportFileRow(
    int RowNumber,
    string FullName,
    string BirthDate,
    string BirthPlace,
    string Gender,
    string ClassroomName,
    string GuardianName,
    string GuardianPhone,
    string GuardianEmail,
    string Address);
```

to:

```csharp
public record StudentImportFileRow(
    int RowNumber,
    string FullName,
    string BirthDate,
    string BirthPlace,
    string Gender,
    string ClassroomName,
    string GuardianName,
    string GuardianPhone,
    string GuardianEmail,
    string Address,
    string FullNameAr,
    string GuardianNameAr);
```

Also update the class doc comment's column list (line 9-12) to keep it accurate:

```csharp
/// Colonnes fixes, dans cet ORDRE (celui du modèle téléchargeable, GetStudentImportTemplateQuery) :
/// Nom complet, Date de naissance, Lieu de naissance, Genre, Classe, Nom du tuteur, Téléphone du
/// tuteur, E-mail du tuteur, Adresse, Nom complet (arabe), Nom du tuteur (arabe) — une CLASSE PAR
/// LIGNE (pas un import ciblé sur une seule classe comme les notes) : une rentrée scolaire mélange
/// plusieurs classes dans un même fichier.
```

- [ ] **Step 4: Update `StudentImportFileParser`**

In `src/SamaEcole.Infrastructure/Files/StudentImportFileParser.cs`:

a) Change `ColumnCount`:

```csharp
    private const int ColumnCount = 9;
```

to:

```csharp
    private const int ColumnCount = 11;
```

Update its doc comment reference too (`/// Nombre de colonnes attendu (voir StudentImportFileRow)` stays accurate as-is).

b) In `ParseCsv`, change the row construction:

```csharp
            rows.Add(new StudentImportFileRow(
                i + 1, fields[0], fields[1], fields[2], fields[3], fields[4], fields[5], fields[6], fields[7], fields[8]));
```

to:

```csharp
            rows.Add(new StudentImportFileRow(
                i + 1, fields[0], fields[1], fields[2], fields[3], fields[4], fields[5], fields[6], fields[7], fields[8],
                fields[9], fields[10]));
```

c) In `ParseExcel`, change the row construction:

```csharp
                rows.Add(new StudentImportFileRow(
                    xlRow.RowNumber(), fields[0], fields[1], fields[2], fields[3], fields[4], fields[5], fields[6], fields[7], fields[8]));
```

to:

```csharp
                rows.Add(new StudentImportFileRow(
                    xlRow.RowNumber(), fields[0], fields[1], fields[2], fields[3], fields[4], fields[5], fields[6], fields[7], fields[8],
                    fields[9], fields[10]));
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/SamaEcole.UnitTests/SamaEcole.UnitTests.csproj --filter "FullyQualifiedName~StudentImportFileParserTests"`
Expected: PASS, all facts green (including the pre-existing ones — `Missing_Trailing_Optional_Columns_Are_Padded_With_Empty_Strings` and every other short-row fixture must still pass unchanged, since the pad-to-`ColumnCount` loop already generalizes to 11).

- [ ] **Step 6: Commit**

```bash
git add src/SamaEcole.Application/Students/StudentImportFileRow.cs src/SamaEcole.Infrastructure/Files/StudentImportFileParser.cs tests/SamaEcole.UnitTests/Students/StudentImportFileParserTests.cs
git commit -m "feat(students): lit les colonnes FullNameAr/GuardianNameAr dans l'import CSV/Excel"
```

---

## Task 6: CSV/Excel import — handler, row validation, template

**Files:**
- Modify: `src/SamaEcole.Application/Students/Commands/ImportStudents/ImportStudentsCommand.cs`
- Modify: `src/SamaEcole.Application/Students/Commands/ImportStudents/ImportStudentsCommandHandler.cs`
- Modify: `src/SamaEcole.Infrastructure/Files/StudentImportTemplateGenerator.cs`
- Modify: `tests/SamaEcole.FunctionalTests/Students/StudentImportEndpointsTests.cs`

**Interfaces:**
- Consumes: `StudentImportFileRow.FullNameAr`/`GuardianNameAr` (Task 5), `Student.FullNameAr`/`GuardianNameAr` (Task 1).
- Produces: `ImportStudentsRowResult.FullNameAr`/`GuardianNameAr` (raw echo), `FieldErrors["fullNameAr"]`/`["guardianNameAr"]` keys.

- [ ] **Step 1: Write the failing functional tests**

In `tests/SamaEcole.FunctionalTests/Students/StudentImportEndpointsTests.cs`:

a) Extend the local `ImportRowResultDto` record (around line 38-40):

```csharp
    private record ImportRowResultDto(int RowNumber, bool IsValid, string FullName, string BirthDate,
        string BirthPlace, string Gender, string ClassroomName, string GuardianName, string GuardianPhone,
        string FullNameAr, string GuardianNameAr, Dictionary<string, string> FieldErrors);
```

b) Add two new facts after `A_Dry_Run_On_A_Well_Formed_File_Reports_Every_Row_Valid_And_Writes_Nothing` (after its closing `}`, around line 123):

```csharp

    [Fact]
    public async Task A_Row_With_Valid_Arabic_Names_Is_Accepted_And_Echoed_Back()
    {
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CM2 Import Arabe");

        var csv = $"{Header};NomAr;TuteurAr\nAwa Ndiaye;12/03/2015;Dakar;F;{classroom.Name};;;;;أوا نداي;موسى نداي";

        var response = await ImportAsync(directeur, csv, dryRun: true);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
        result.InvalidRows.Should().Be(0);
        result.Rows.Single().FullNameAr.Should().Be("أوا نداي");
        result.Rows.Single().GuardianNameAr.Should().Be("موسى نداي");
    }

    [Fact]
    public async Task A_Row_With_An_Oversized_FullNameAr_Is_Rejected_With_A_Field_Error()
    {
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CM2 Import Arabe Invalide");
        var tooLong = new string('ا', 201);

        var csv = $"{Header};NomAr;TuteurAr\nAwa Ndiaye;12/03/2015;Dakar;F;{classroom.Name};;;;;{tooLong};";

        var response = await ImportAsync(directeur, csv, dryRun: true);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
        result.InvalidRows.Should().Be(1);
        result.Rows.Single().FieldErrors.Should().ContainKey("fullNameAr");
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/SamaEcole.FunctionalTests/SamaEcole.FunctionalTests.csproj --filter "FullyQualifiedName~StudentImportEndpointsTests"`
Expected: build FAILS — `ImportRowResultDto` (test-local record) has more members than the actual JSON response provides yet, or `System.Text.Json` deserialization leaves `FullNameAr`/`GuardianNameAr` at their default (empty string, since these are non-nullable `string` positional properties) causing the two new facts to fail assertions (`""` vs `"أوا نداي"`).

- [ ] **Step 3: Add the fields to `ImportStudentsRowResult`**

In `src/SamaEcole.Application/Students/Commands/ImportStudents/ImportStudentsCommand.cs`, change:

```csharp
public record ImportStudentsRowResult(
    int RowNumber,
    bool IsValid,
    string FullName,
    string BirthDate,
    string BirthPlace,
    string Gender,
    string ClassroomName,
    string GuardianName,
    string GuardianPhone,
    string GuardianEmail,
    string Address,
    IReadOnlyDictionary<string, string> FieldErrors);
```

to:

```csharp
public record ImportStudentsRowResult(
    int RowNumber,
    bool IsValid,
    string FullName,
    string BirthDate,
    string BirthPlace,
    string Gender,
    string ClassroomName,
    string GuardianName,
    string GuardianPhone,
    string GuardianEmail,
    string Address,
    string FullNameAr,
    string GuardianNameAr,
    IReadOnlyDictionary<string, string> FieldErrors);
```

Also update its doc comment's `FieldErrors` key list (around line 28-29):

```csharp
/// Clés de <see cref="FieldErrors"/> : "fullName", "birthDate", "birthPlace", "gender", "classroomName",
/// "guardianName", "guardianPhone", "guardianEmail", "address", "fullNameAr", "guardianNameAr".
```

- [ ] **Step 4: Update `ImportStudentsCommandHandler`**

In `src/SamaEcole.Application/Students/Commands/ImportStudents/ImportStudentsCommandHandler.cs`:

a) Extend the private `ParsedStudentRow` record (around line 28-30):

```csharp
    private record ParsedStudentRow(
        string FullName, DateOnly BirthDate, string BirthPlace, string Gender, Guid ClassroomId,
        string? GuardianName, string? GuardianPhone, string? GuardianEmail, string? Address);
```

to:

```csharp
    private record ParsedStudentRow(
        string FullName, DateOnly BirthDate, string BirthPlace, string Gender, Guid ClassroomId,
        string? GuardianName, string? GuardianPhone, string? GuardianEmail, string? Address,
        string? FullNameAr, string? GuardianNameAr);
```

b) In `Handle`, the `results.Add(new ImportStudentsRowResult(...))` call (around line 71-75):

```csharp
            results.Add(new ImportStudentsRowResult(
                row.RowNumber, fieldErrors.Count == 0,
                row.FullName, row.BirthDate, row.BirthPlace, row.Gender, row.ClassroomName,
                row.GuardianName, row.GuardianPhone, row.GuardianEmail, row.Address,
                fieldErrors));
```

becomes:

```csharp
            results.Add(new ImportStudentsRowResult(
                row.RowNumber, fieldErrors.Count == 0,
                row.FullName, row.BirthDate, row.BirthPlace, row.Gender, row.ClassroomName,
                row.GuardianName, row.GuardianPhone, row.GuardianEmail, row.Address,
                row.FullNameAr, row.GuardianNameAr,
                fieldErrors));
```

c) In the transaction loop, the `new Student { ... }` construction (around line 115-128):

```csharp
                dbContext.Students.Add(new Student
                {
                    SchoolId = schoolId,
                    Matricule = matricule,
                    FullName = parsed.FullName,
                    BirthDate = parsed.BirthDate,
                    BirthPlace = parsed.BirthPlace,
                    Gender = parsed.Gender,
                    ClassroomId = parsed.ClassroomId,
                    GuardianName = parsed.GuardianName,
                    GuardianPhone = parsed.GuardianPhone,
                    GuardianEmail = parsed.GuardianEmail,
                    Address = parsed.Address
                });
```

becomes:

```csharp
                dbContext.Students.Add(new Student
                {
                    SchoolId = schoolId,
                    Matricule = matricule,
                    FullName = parsed.FullName,
                    BirthDate = parsed.BirthDate,
                    BirthPlace = parsed.BirthPlace,
                    Gender = parsed.Gender,
                    ClassroomId = parsed.ClassroomId,
                    GuardianName = parsed.GuardianName,
                    GuardianPhone = parsed.GuardianPhone,
                    GuardianEmail = parsed.GuardianEmail,
                    Address = parsed.Address,
                    FullNameAr = parsed.FullNameAr,
                    GuardianNameAr = parsed.GuardianNameAr
                });
```

d) In `ValidateRow`, after the existing `address` block (around line 245-253, right before `if (errors.Count > 0)`), add the two new field checks — same shape as `guardianName`'s (around line 211-219), but WITHOUT the "obligatoire" branch (these are optional, `guardianName` has none either — only a length + safe-text check):

```csharp
        var fullNameAr = row.FullNameAr.Trim();
        if (fullNameAr.Length > 200)
        {
            errors["fullNameAr"] = "Le nom complet en arabe ne peut pas dépasser 200 caractères.";
        }
        else if (!SafeTextValidation.IsSafeText(fullNameAr))
        {
            errors["fullNameAr"] = SafeTextValidation.ErrorMessage;
        }

        var guardianNameAr = row.GuardianNameAr.Trim();
        if (guardianNameAr.Length > 200)
        {
            errors["guardianNameAr"] = "Le nom du tuteur en arabe ne peut pas dépasser 200 caractères.";
        }
        else if (!SafeTextValidation.IsSafeText(guardianNameAr))
        {
            errors["guardianNameAr"] = SafeTextValidation.ErrorMessage;
        }
```

e) In the `return (errors, new ParsedStudentRow(...))` at the end of `ValidateRow` (around line 260-265):

```csharp
        return (errors, new ParsedStudentRow(
            fullName, birthDate, birthPlace, gender, classroomId,
            guardianName.Length == 0 ? null : guardianName,
            guardianPhone.Length == 0 ? null : guardianPhone,
            guardianEmail.Length == 0 ? null : guardianEmail,
            address.Length == 0 ? null : address));
```

becomes:

```csharp
        return (errors, new ParsedStudentRow(
            fullName, birthDate, birthPlace, gender, classroomId,
            guardianName.Length == 0 ? null : guardianName,
            guardianPhone.Length == 0 ? null : guardianPhone,
            guardianEmail.Length == 0 ? null : guardianEmail,
            address.Length == 0 ? null : address,
            fullNameAr.Length == 0 ? null : fullNameAr,
            guardianNameAr.Length == 0 ? null : guardianNameAr));
```

- [ ] **Step 5: Extend the downloadable template**

In `src/SamaEcole.Infrastructure/Files/StudentImportTemplateGenerator.cs`:

a) Change `Headers`:

```csharp
    private static readonly string[] Headers =
    [
        "Nom complet", "Date de naissance (jj/mm/aaaa)", "Lieu de naissance", "Genre (M/F)",
        "Classe", "Nom du tuteur", "Téléphone du tuteur", "E-mail du tuteur", "Adresse"
    ];
```

to:

```csharp
    private static readonly string[] Headers =
    [
        "Nom complet", "Date de naissance (jj/mm/aaaa)", "Lieu de naissance", "Genre (M/F)",
        "Classe", "Nom du tuteur", "Téléphone du tuteur", "E-mail du tuteur", "Adresse",
        "Nom complet (arabe, facultatif)", "Nom du tuteur (arabe, facultatif)"
    ];
```

b) After `sheet.Cell(2, 9).Value = "Cité Keur Gorgui, Dakar";`, add the two example cells:

```csharp
        sheet.Cell(2, 10).Value = "إبراهيما نداي";
        sheet.Cell(2, 11).Value = "موسى نداي";
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/SamaEcole.FunctionalTests/SamaEcole.FunctionalTests.csproj --filter "FullyQualifiedName~StudentImportEndpointsTests"`
Expected: PASS, all facts green (including every pre-existing import fact — the 9-column CSVs they use still parse correctly thanks to Task 5's padding).

- [ ] **Step 7: Commit**

```bash
git add src/SamaEcole.Application/Students/Commands/ImportStudents/ src/SamaEcole.Infrastructure/Files/StudentImportTemplateGenerator.cs tests/SamaEcole.FunctionalTests/Students/StudentImportEndpointsTests.cs
git commit -m "feat(students): valide et importe FullNameAr/GuardianNameAr depuis le fichier de masse"
```

---

## Task 7: UI (Razor + Alpine.js) — `Views/Students/Index.cshtml` + `wwwroot/js/students.js`

**Files:**
- Modify: `src/SamaEcole.Web/Views/Students/Index.cshtml`
- Modify: `src/SamaEcole.Web/wwwroot/js/students.js`

**Interfaces:**
- Consumes: `CreateStudentCommand.FullNameAr`/`GuardianNameAr` (Task 3), `UpdateStudentCommand.FullNameAr`/`GuardianNameAr` (Task 4), `StudentListItem.FullNameAr`/`GuardianNameAr` (Task 2 — this is what `detailStudent` binds to), `ImportStudentsRowResult.FullNameAr`/`GuardianNameAr` + `FieldErrors["fullNameAr"/"guardianNameAr"]` (Task 6).

No dedicated JS test infrastructure exists for `Views/Students/Index.cshtml` today (`src/SamaEcole.Web/tests/js/` has no `students*.test.mjs`), so this task has no automated red/green cycle — verify manually per Step 6 below instead of via `npm test`.

- [ ] **Step 1: `newStudent` initial state + reset (create modal data)**

In `src/SamaEcole.Web/wwwroot/js/students.js`:

a) The `newStudent` initializer (around line 111-123):

```javascript
        newStudent: {
            fullName: '',
            birthDate: '',
            birthPlace: '',
            gender: 'M',
            classroomId: '', // Must be UUID
            photoUrl: '',
            photoData: '', // Feature B — base64 déjà compressé (photo-compress.js), rempli par <photo-dropzone>.
            guardianName: '',
            guardianPhone: '',
            guardianEmail: '',
            address: ''
        },
```

becomes:

```javascript
        newStudent: {
            fullName: '',
            birthDate: '',
            birthPlace: '',
            gender: 'M',
            classroomId: '', // Must be UUID
            photoUrl: '',
            photoData: '', // Feature B — base64 déjà compressé (photo-compress.js), rempli par <photo-dropzone>.
            guardianName: '',
            guardianPhone: '',
            guardianEmail: '',
            address: '',
            fullNameAr: '',
            guardianNameAr: ''
        },
```

b) The reset after a successful create, in `submitCreate()` (around line 853):

```javascript
                this.newStudent = { fullName: '', birthDate: '', birthPlace: '', gender: 'M', classroomId: '', photoUrl: '', photoData: '', guardianName: '', guardianPhone: '', guardianEmail: '', address: '' };
```

becomes:

```javascript
                this.newStudent = { fullName: '', birthDate: '', birthPlace: '', gender: 'M', classroomId: '', photoUrl: '', photoData: '', guardianName: '', guardianPhone: '', guardianEmail: '', address: '', fullNameAr: '', guardianNameAr: '' };
```

- [ ] **Step 2: `editingStudent` initial state, population, and reset**

In `src/SamaEcole.Web/wwwroot/js/students.js`:

a) `emptyEditingStudent()` (around line 7-13):

```javascript
function emptyEditingStudent() {
    return {
        fullName: '', birthDate: '', birthPlace: '', gender: '', classroomId: '',
        photoUrl: '', photoDisplayUrl: '', guardianName: '', guardianPhone: '', guardianEmail: '',
        address: '', rowVersion: null
    };
}
```

becomes:

```javascript
function emptyEditingStudent() {
    return {
        fullName: '', birthDate: '', birthPlace: '', gender: '', classroomId: '',
        photoUrl: '', photoDisplayUrl: '', guardianName: '', guardianPhone: '', guardianEmail: '',
        address: '', fullNameAr: '', guardianNameAr: '', rowVersion: null
    };
}
```

b) `openEditStudent()` (around line 628-644): note this populates from `this.studentDetail.identity`, which Task 1 already extended with `fullNameAr`/`guardianNameAr` — change:

```javascript
            this.editingStudent = {
                fullName: identity.fullName,
                birthDate: identity.birthDate,
                birthPlace: identity.birthPlace || '',
                gender: identity.gender,
                classroomId: identity.classroomId,
                photoUrl: identity.photoUrl || '', // URL brute, jamais la photo téléversée (round-trip fidèle).
                photoDisplayUrl: identity.photoDisplayUrl || '', // Aperçu <photo-dropzone> uniquement.
                guardianName: identity.guardianName || '',
                guardianPhone: identity.guardianPhone || '',
                guardianEmail: identity.guardianEmail || '',
                address: identity.address || '',
                rowVersion: identity.rowVersion
            };
```

to:

```javascript
            this.editingStudent = {
                fullName: identity.fullName,
                birthDate: identity.birthDate,
                birthPlace: identity.birthPlace || '',
                gender: identity.gender,
                classroomId: identity.classroomId,
                photoUrl: identity.photoUrl || '', // URL brute, jamais la photo téléversée (round-trip fidèle).
                photoDisplayUrl: identity.photoDisplayUrl || '', // Aperçu <photo-dropzone> uniquement.
                guardianName: identity.guardianName || '',
                guardianPhone: identity.guardianPhone || '',
                guardianEmail: identity.guardianEmail || '',
                address: identity.address || '',
                fullNameAr: identity.fullNameAr || '',
                guardianNameAr: identity.guardianNameAr || '',
                rowVersion: identity.rowVersion
            };
```

(`submitEditStudent()` already posts the whole `this.editingStudent` object via `window.api.put` — no change needed there.)

- [ ] **Step 3: Detail view — header (FullNameAr) and guardian panel (GuardianNameAr)**

In `src/SamaEcole.Web/Views/Students/Index.cshtml`:

a) After the `<h2>` showing `detailStudent.fullName` (around line 234):

```html
                <h2 class="text-2xl font-bold text-slate-900" x-text="detailStudent ? detailStudent.fullName : ''"></h2>
```

add, right after that line:

```html
                <p x-show="detailStudent && detailStudent.fullNameAr" x-cloak dir="rtl"
                   class="text-lg font-semibold text-slate-500" x-text="detailStudent ? detailStudent.fullNameAr : ''"></p>
```

b) In the « Tuteur légal » panel, after the `<p>` showing `detailStudent.guardianName` (around line 392-395):

```html
            <div>
                <p class="text-[11px] font-semibold text-slate-400 uppercase tracking-wider mb-1">Nom</p>
                <p class="text-sm font-bold text-slate-900" x-text="detailStudent && detailStudent.guardianName ? detailStudent.guardianName : '—'"></p>
            </div>
```

becomes:

```html
            <div>
                <p class="text-[11px] font-semibold text-slate-400 uppercase tracking-wider mb-1">Nom</p>
                <p class="text-sm font-bold text-slate-900" x-text="detailStudent && detailStudent.guardianName ? detailStudent.guardianName : '—'"></p>
                <p x-show="detailStudent && detailStudent.guardianNameAr" x-cloak dir="rtl"
                   class="text-sm font-semibold text-slate-500 mt-0.5" x-text="detailStudent ? detailStudent.guardianNameAr : ''"></p>
            </div>
```

- [ ] **Step 4: Create modal — two new fields**

In `src/SamaEcole.Web/Views/Students/Index.cshtml`, after the « Nom complet » field of the CREATE modal (around line 1052-1056):

```html
 <!-- Nom complet -->
 <div>
 <label class="form-label form-label-required">Nom complet</label>
 <input type="text" x-model="newStudent.fullName" required class="input-field" :class="createErrors.fullname && 'input-field-error'">
 <p x-show="createErrors.fullname" x-text="createErrors.fullname" class="field-error"></p>
 </div>
```

insert immediately after (before the « Date de naissance » field):

```html
 @* Module Franco-Arabe : facultatif, aucune traduction automatique — voir Subject.NameAr. *@
 <div>
  <label for="student-full-name-ar" class="form-label">Nom complet en arabe (facultatif)</label>
  <input id="student-full-name-ar" type="text" x-model="newStudent.fullNameAr" maxlength="200" dir="rtl"
   class="input-field" :class="createErrors.fullnamear && 'input-field-error'">
  <p x-show="createErrors.fullnamear" x-cloak x-text="createErrors.fullnamear" class="field-error"></p>
 </div>
```

And after the « Nom du tuteur » field of the CREATE modal (around line 1110-1114):

```html
 <!-- Tuteur -->
 <div>
 <label class="form-label">Nom du tuteur</label>
 <input type="text" x-model="newStudent.guardianName" class="input-field">
 </div>
```

insert immediately after:

```html
 <div>
  <label for="student-guardian-name-ar" class="form-label">Nom du tuteur en arabe (facultatif)</label>
  <input id="student-guardian-name-ar" type="text" x-model="newStudent.guardianNameAr" maxlength="200" dir="rtl"
   class="input-field" :class="createErrors.guardiannamear && 'input-field-error'">
  <p x-show="createErrors.guardiannamear" x-cloak x-text="createErrors.guardiannamear" class="field-error"></p>
 </div>
```

- [ ] **Step 5: Edit modal — two new fields**

In `src/SamaEcole.Web/Views/Students/Index.cshtml`, after the « Nom complet » field of the EDIT modal (around line 876-880):

```html
 <div>
 <label class="form-label form-label-required">Nom complet</label>
 <input type="text" x-model="editingStudent.fullName" required class="input-field" :class="studentEditErrors.fullname && 'input-field-error'">
 <p x-show="studentEditErrors.fullname" x-text="studentEditErrors.fullname" class="field-error"></p>
 </div>
```

insert immediately after:

```html
 <div>
  <label for="edit-student-full-name-ar" class="form-label">Nom complet en arabe (facultatif)</label>
  <input id="edit-student-full-name-ar" type="text" x-model="editingStudent.fullNameAr" maxlength="200" dir="rtl"
   class="input-field" :class="studentEditErrors.fullnamear && 'input-field-error'">
  <p x-show="studentEditErrors.fullnamear" x-cloak x-text="studentEditErrors.fullnamear" class="field-error"></p>
 </div>
```

And after the « Nom du tuteur » field of the EDIT modal (around line 927-931):

```html
 <div>
 <label class="form-label">Nom du tuteur</label>
 <input type="text" x-model="editingStudent.guardianName" class="input-field" :class="studentEditErrors.guardianname && 'input-field-error'">
 <p x-show="studentEditErrors.guardianname" x-text="studentEditErrors.guardianname" class="field-error"></p>
 </div>
```

insert immediately after:

```html
 <div>
  <label for="edit-student-guardian-name-ar" class="form-label">Nom du tuteur en arabe (facultatif)</label>
  <input id="edit-student-guardian-name-ar" type="text" x-model="editingStudent.guardianNameAr" maxlength="200" dir="rtl"
   class="input-field" :class="studentEditErrors.guardiannamear && 'input-field-error'">
  <p x-show="studentEditErrors.guardiannamear" x-cloak x-text="studentEditErrors.guardiannamear" class="field-error"></p>
 </div>
```

- [ ] **Step 6: Manual verification**

Run: `npm run build:css --prefix src/SamaEcole.Web` (compiles Tailwind — new markup uses only existing utility classes already in `site.css`'s scan paths, so no new classes should be missing, but rebuild to be sure the build itself doesn't error).
Run: `dotnet run --project src/SamaEcole.Web`, log in as Directeur, open **Élèves**:
- Create a student, fill « Nom complet en arabe » and « Nom du tuteur en arabe » with Arabic text (e.g. `فاطمة`), submit, confirm no error.
- Open its detail card: confirm the Arabic name shows under the French name (RTL), and the Arabic guardian name shows under the French guardian name in the « Tuteur légal » panel.
- Edit the student, change the Arabic fields, save, reopen the detail card, confirm the new values show.
- Leave both Arabic fields empty on a new student: confirm no `—`/blank artifact appears where the optional line would have been (the `x-show` must hide the `<p>` entirely).

- [ ] **Step 7: Import preview table — two new columns**

In `src/SamaEcole.Web/Views/Students/Index.cshtml`, the header row (around line 1198-1209):

```html
 <tr class="table-head">
 <th class="py-2 px-2 w-10">L.</th>
 <th class="py-2 px-2">Nom complet</th>
 <th class="py-2 px-2">Naissance</th>
 <th class="py-2 px-2">Lieu</th>
 <th class="py-2 px-2">Genre</th>
 <th class="py-2 px-2">Classe</th>
 <th class="py-2 px-2">Tuteur</th>
 <th class="py-2 px-2">Tél. tuteur</th>
                                    <th class="py-2 px-2">E-mail tuteur</th>
                                    <th class="py-2 px-2">Adresse</th>
 </tr>
```

becomes:

```html
 <tr class="table-head">
 <th class="py-2 px-2 w-10">L.</th>
 <th class="py-2 px-2">Nom complet</th>
 <th class="py-2 px-2">Naissance</th>
 <th class="py-2 px-2">Lieu</th>
 <th class="py-2 px-2">Genre</th>
 <th class="py-2 px-2">Classe</th>
 <th class="py-2 px-2">Tuteur</th>
 <th class="py-2 px-2">Tél. tuteur</th>
                                    <th class="py-2 px-2">E-mail tuteur</th>
                                    <th class="py-2 px-2">Adresse</th>
                                    <th class="py-2 px-2">Nom (arabe)</th>
                                    <th class="py-2 px-2">Tuteur (arabe)</th>
 </tr>
```

And the row template (around line 1213-1224), after the `address` `<td>`:

```html
 <td class="py-1.5 px-2" :class="importCellClass(row, 'guardianEmail')" :title="row.fieldErrors.guardianEmail || ''" x-text="row.guardianEmail"></td>
                                    <td class="py-1.5 px-2" :class="importCellClass(row, 'address')" :title="row.fieldErrors.address || ''" x-text="row.address"></td>
 </tr>
```

becomes:

```html
 <td class="py-1.5 px-2" :class="importCellClass(row, 'guardianEmail')" :title="row.fieldErrors.guardianEmail || ''" x-text="row.guardianEmail"></td>
                                    <td class="py-1.5 px-2" :class="importCellClass(row, 'address')" :title="row.fieldErrors.address || ''" x-text="row.address"></td>
                                    <td class="py-1.5 px-2" dir="rtl" :class="importCellClass(row, 'fullNameAr')" :title="row.fieldErrors.fullNameAr || ''" x-text="row.fullNameAr"></td>
                                    <td class="py-1.5 px-2" dir="rtl" :class="importCellClass(row, 'guardianNameAr')" :title="row.fieldErrors.guardianNameAr || ''" x-text="row.guardianNameAr"></td>
 </tr>
```

(Note the exact `:class="importCellClass(row, 'guardianEmail')"`/etc. attribute values above must match line-for-line what is already in the file — re-read the surrounding lines before editing if this snippet doesn't match exactly, since minor whitespace drift is possible.)

- [ ] **Step 8: Manual verification of the import screen**

With the app still running from Step 6: open **Élèves → Importer**, download the template (confirm it now has 11 columns with the two new Arabic headers and example values), fill a row with a valid Arabic name and one with a 201-character Arabic name, upload as dry run, confirm: the valid row's Arabic columns display correctly (RTL), and the invalid row's `Nom (arabe)` cell is highlighted red with the length-error tooltip on hover.

- [ ] **Step 9: Commit**

```bash
git add src/SamaEcole.Web/Views/Students/Index.cshtml src/SamaEcole.Web/wwwroot/js/students.js
git commit -m "feat(students): UI de saisie/affichage de FullNameAr/GuardianNameAr"
```

---

## Task 8: Final verification

**Files:** none (verification only).

- [ ] **Step 1: Full solution build**

Run: `dotnet build`
Expected: `0 Erreur(s)` (warnings pre-existing/unrelated are acceptable, as established by the Task 0 baseline before this plan started).

- [ ] **Step 2: Run every test touched by this plan together**

Run: `dotnet test --filter "FullyQualifiedName~Students"`
Expected: all Students-namespace tests across `SamaEcole.UnitTests`, `SamaEcole.IntegrationTests`, and `SamaEcole.FunctionalTests` PASS (requires `docker compose up -d` to already be running for the latter two — see Global Constraints).

- [ ] **Step 3: Full regression run**

Run: `dotnet test`
Expected: no new failures anywhere in the solution beyond the pre-existing baseline (this plan's changes are additive only — nothing it touches should affect an unrelated test; if something does fail, treat it as a genuine regression to fix before closing the lot, per AGENTS.md règle #5/#8 discipline).

- [ ] **Step 4: Spec coverage re-check**

Re-read `docs/superpowers/specs/2026-09-29-student-guardian-arabic-names-design.md` §3-§8 against the tasks above and confirm every bullet has a corresponding task:
- §3 (modèle de données + migration) → Task 1.
- §4.1 (écriture) → Tasks 3, 4.
- §4.2 (lecture, `StudentIdentityDto` + `StudentListItem` incl. the corrected `GuardianNameAr`) → Tasks 1, 2.
- §4.3 (import CSV) → Tasks 5, 6.
- §4.4 (explicitement non modifié) → confirmed untouched (no task references `StudentsExportModel`, `GetStudentsExportPdfQueryHandler`, or `ReportCardDocument`).
- §5 (UI) → Task 7.
- §7 (sécurité — `.NoHtml()`) → Tasks 3, 4, 6.
- §8 (tests) → every task's Step 1/2 + this task's Steps 1-3.

No commit for this task — it is a verification gate only.
