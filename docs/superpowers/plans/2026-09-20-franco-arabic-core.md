# Socle de données Franco-Arabe / Daara — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Poser le socle de données du futur module Coran/Franco-Arabe — deux colonnes descriptives (`Subject.SectionType`, `SchoolSettings.SchoolType`) et deux entités préparatoires (`QuranProgress`, `QuranEvaluation`) — sans CQRS, contrôleur ni écran, et sans toucher au comportement existant.

**Architecture:** Suivre exactement le patron déjà établi par chaque module précédent (Cahier de texte, Internat, Inventaire) : entités `AuditableEntity` + `ITenantEntity`, configuration EF avec `xmin` pour les tables sensibles, une migration additive unique qui pose ses policies RLS à la main, et une suite de tests qui prouve l'isolation au niveau de la base (pas seulement du filtre EF).

**Tech Stack:** ASP.NET Core 9 / EF Core / Npgsql / PostgreSQL 16 / xUnit + FluentAssertions + Testcontainers.

**Spec:** `docs/superpowers/specs/2026-09-20-franco-arabic-core-design.md`

## Global Constraints

- PostgreSQL uniquement, aucun provider EF Core hors Npgsql (AGENTS.md règle #1).
- Toute table tenant porte `SchoolId` + Global Query Filter EF (automatique via `ITenantEntity`) + policy RLS PostgreSQL — les deux, jamais une seule (règle #2). `RlsCoverageTests` échoue sinon.
- Aucune suppression physique : soft delete uniquement, héritée d'`AuditableEntity` (règle #6). Les migrations n'accordent jamais `DELETE` au rôle `sama_ecole_app`.
- Verrouillage optimiste (`xmin`) sur toute donnée sensible équivalente à une note (règle #5) : `QuranEvaluation` et `QuranProgress` en portent un.
- Tous les enums du modèle sont persistés `HasConversion<string>().HasMaxLength(N)` — jamais en entier, jamais en type PostgreSQL natif (convention uniforme du projet, voir `GradeConfiguration`, `EnrollmentConfiguration`, etc.).
- Aucun code métier dans un contrôleur ni une entité (règle #8) — sans objet ici, aucun contrôleur n'est créé.
- Ne jamais committer avec `--no-verify` ; conventional commits (`feat:`, `test:`, `docs:`).
- Branche : `feature/franco-arabic-core`, créée depuis `main` avant toute modification.
- Migrations : `dotnet ef migrations add <Nom> -p src/SamaEcole.Persistence -s src/SamaEcole.Web`.

---

### Task 1 : Branche + enums du module

**Files:**
- Create: `src/SamaEcole.Domain/Enums/QuranEnums.cs`
- Test: `tests/SamaEcole.UnitTests/Quran/QuranEnumsTests.cs`

**Interfaces:**
- Produces : `SamaEcole.Domain.Enums.SectionType` (`French`/`Arabic`/`IslamicStudies`), `SamaEcole.Domain.Enums.SchoolType` (`Standard`/`FrancoArabic`/`Daara`), `SamaEcole.Domain.Enums.QuranMemorizationStatus` (`InProcess`/`Memorized`/`Revised`) — consommés par les Tasks 2, 3 et 4.

- [ ] **Step 1 : Créer la branche**

```bash
git checkout main
git pull
git checkout -b feature/franco-arabic-core
```

- [ ] **Step 2 : Écrire le test (qui échoue à la compilation — les enums n'existent pas encore)**

```csharp
// tests/SamaEcole.UnitTests/Quran/QuranEnumsTests.cs
using SamaEcole.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace SamaEcole.UnitTests.Quran;

/// <summary>
/// Garde le premier membre de chaque enum aligné sur la valeur par défaut annoncée dans la spec
/// (docs/superpowers/specs/2026-09-20-franco-arabic-core-design.md §3.1/3.2/3.3) : c'est ce membre
/// que le CLR pose par défaut ET celui que la migration doit écrire en dur pour les lignes
/// existantes — un décalage entre les deux a déjà cassé la lecture des ParentSummons en
/// production (ACTIVE_CONTEXT.md, incident du 02/09/2026).
/// </summary>
public class QuranEnumsTests
{
    [Fact]
    public void SectionType_Default_Should_Be_French()
    {
        default(SectionType).Should().Be(SectionType.French);
    }

    [Fact]
    public void SchoolType_Default_Should_Be_Standard()
    {
        default(SchoolType).Should().Be(SchoolType.Standard);
    }

    [Fact]
    public void QuranMemorizationStatus_Default_Should_Be_InProcess()
    {
        default(QuranMemorizationStatus).Should().Be(QuranMemorizationStatus.InProcess);
    }
}
```

- [ ] **Step 2b : Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test tests/SamaEcole.UnitTests --filter FullyQualifiedName~QuranEnumsTests`
Expected: FAIL (compilation error — `SamaEcole.Domain.Enums.SectionType` n'existe pas)

- [ ] **Step 3 : Créer les enums**

```csharp
// src/SamaEcole.Domain/Enums/QuranEnums.cs
namespace SamaEcole.Domain.Enums;

/// <summary>
/// Regroupement pédagogique d'une matière (module Coran/Franco-Arabe, docs/superpowers/specs/
/// 2026-09-20-franco-arabic-core-design.md §3.1). Purement descriptif : ne pilote aucune règle de
/// calcul de moyenne, aucun contrôle d'accès — <see cref="Entities.Subject.Coefficient"/> et le
/// calcul du bulletin restent inchangés. <see cref="French"/> en premier membre et valeur par
/// défaut : toute matière existante avant cette migration reste tacitement "française", sans
/// changement de comportement.
/// </summary>
public enum SectionType
{
    French,
    Arabic,
    IslamicStudies
}

/// <summary>
/// Classification d'établissement (module Coran/Franco-Arabe, spec §3.2). PUREMENT INFORMATIF :
/// n'active rien seul, ne remplace pas <see cref="Entities.SchoolSettings.IsCoranModuleEnabled"/>
/// (le seul interrupteur consommé par <c>[RequireModule(SchoolModule.Coran)]</c>) ni
/// <see cref="TypeEtablissement"/> (axe Privé/Public, sans rapport). <see cref="Standard"/> en
/// premier membre et valeur par défaut : toute école existante reste "standard" sans migration
/// manuelle, comme <see cref="TypeEtablissement.Prive"/>.
/// </summary>
public enum SchoolType
{
    Standard,
    FrancoArabic,
    Daara
}

/// <summary>
/// Statut de mémorisation d'une portion du Coran (<see cref="Entities.QuranProgress"/>, spec §3.3).
/// <see cref="InProcess"/> en premier membre et valeur par défaut : une ligne de suivi commence
/// toujours en cours de mémorisation.
/// </summary>
public enum QuranMemorizationStatus
{
    InProcess,
    Memorized,
    Revised
}
```

- [ ] **Step 4 : Lancer le test pour vérifier qu'il passe**

Run: `dotnet test tests/SamaEcole.UnitTests --filter FullyQualifiedName~QuranEnumsTests`
Expected: PASS (3/3)

- [ ] **Step 5 : Commit**

```bash
git add src/SamaEcole.Domain/Enums/QuranEnums.cs tests/SamaEcole.UnitTests/Quran/QuranEnumsTests.cs
git commit -m "feat(quran): ajoute les enums du socle Franco-Arabe/Daara"
```

---

### Task 2 : `Subject.SectionType`

**Files:**
- Modify: `src/SamaEcole.Domain/Entities/Subject.cs`
- Modify: `src/SamaEcole.Persistence/Configurations/SubjectConfiguration.cs`
- Test: `tests/SamaEcole.UnitTests/Quran/SubjectSectionTypeTests.cs`

**Interfaces:**
- Consumes : `SamaEcole.Domain.Enums.SectionType` (Task 1).
- Produces : `Subject.SectionType` (propriété publique, défaut `SectionType.French`) — consommé par la future spec écran (hors périmètre ici) et par Task 6 (migration).

- [ ] **Step 1 : Écrire le test (échoue — la propriété n'existe pas)**

```csharp
// tests/SamaEcole.UnitTests/Quran/SubjectSectionTypeTests.cs
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace SamaEcole.UnitTests.Quran;

public class SubjectSectionTypeTests
{
    [Fact]
    public void A_New_Subject_Defaults_To_French_Section()
    {
        var subject = new Subject
        {
            SchoolId = Guid.NewGuid(),
            Name = "Mathématiques",
            Level = "Primaire",
            Coefficient = 4
        };

        subject.SectionType.Should().Be(SectionType.French);
    }
}
```

- [ ] **Step 2 : Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test tests/SamaEcole.UnitTests --filter FullyQualifiedName~SubjectSectionTypeTests`
Expected: FAIL (compilation error — `Subject.SectionType` n'existe pas)

- [ ] **Step 3 : Ajouter la propriété sur l'entité**

Dans `src/SamaEcole.Domain/Entities/Subject.cs`, ajouter après `Column2Header` (avant l'accolade fermante de la classe, ligne 85) :

```csharp
    /// <summary>
    /// Regroupement pédagogique (module Coran/Franco-Arabe, docs/superpowers/specs/
    /// 2026-09-20-franco-arabic-core-design.md §3.1) — purement descriptif, ne modifie ni
    /// <see cref="Coefficient"/> ni le calcul du bulletin. Défaut <see cref="Enums.SectionType.French"/> :
    /// toute matière existante reste tacitement française sans changement de comportement.
    /// </summary>
    public SectionType SectionType { get; set; } = SectionType.French;
```

Ajouter en tête du fichier : `using SamaEcole.Domain.Enums;`

- [ ] **Step 4 : Mapper la colonne dans la configuration EF**

Dans `src/SamaEcole.Persistence/Configurations/SubjectConfiguration.cs`, ajouter après la ligne `builder.Property(s => s.Level).IsRequired().HasMaxLength(50);` :

```csharp
        builder.Property(s => s.SectionType).HasConversion<string>().HasMaxLength(20).IsRequired();
```

- [ ] **Step 5 : Lancer le test pour vérifier qu'il passe**

Run: `dotnet test tests/SamaEcole.UnitTests --filter FullyQualifiedName~SubjectSectionTypeTests`
Expected: PASS

- [ ] **Step 6 : Commit**

```bash
git add src/SamaEcole.Domain/Entities/Subject.cs src/SamaEcole.Persistence/Configurations/SubjectConfiguration.cs tests/SamaEcole.UnitTests/Quran/SubjectSectionTypeTests.cs
git commit -m "feat(quran): ajoute Subject.SectionType"
```

---

### Task 3 : `SchoolSettings.SchoolType`

**Files:**
- Modify: `src/SamaEcole.Domain/Entities/SchoolSettings.cs`
- Modify: `src/SamaEcole.Persistence/Configurations/SchoolSettingsConfiguration.cs`
- Test: `tests/SamaEcole.UnitTests/Quran/SchoolSettingsSchoolTypeTests.cs`

**Interfaces:**
- Consumes : `SamaEcole.Domain.Enums.SchoolType` (Task 1).
- Produces : `SchoolSettings.SchoolType` (défaut `SchoolSettingsDefaults.SchoolType` = `SchoolType.Standard`).

- [ ] **Step 1 : Écrire le test (échoue)**

```csharp
// tests/SamaEcole.UnitTests/Quran/SchoolSettingsSchoolTypeTests.cs
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace SamaEcole.UnitTests.Quran;

public class SchoolSettingsSchoolTypeTests
{
    [Fact]
    public void New_School_Settings_Default_To_Standard_School_Type()
    {
        var settings = new SchoolSettings { SchoolId = Guid.NewGuid() };

        settings.SchoolType.Should().Be(SchoolType.Standard);
    }
}
```

- [ ] **Step 2 : Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test tests/SamaEcole.UnitTests --filter FullyQualifiedName~SchoolSettingsSchoolTypeTests`
Expected: FAIL (compilation error)

- [ ] **Step 3 : Ajouter la constante par défaut et la propriété**

Dans `src/SamaEcole.Domain/Entities/SchoolSettings.cs`, ajouter la propriété juste après `IsCoranModuleEnabled` (avant l'accolade fermante de la classe `SchoolSettings`, ligne 132) :

```csharp

    /// <summary>
    /// Classification d'établissement (module Coran/Franco-Arabe, spec §3.2) — PUREMENT
    /// INFORMATIF. N'active rien : <see cref="IsCoranModuleEnabled"/> reste le seul interrupteur
    /// consommé par ModuleAuthorizationHandler. Défaut <see cref="Enums.SchoolType.Standard"/>,
    /// comme <see cref="TypeEtablissement"/> reste à <see cref="Enums.TypeEtablissement.Prive"/>
    /// pour toute école existante.
    /// </summary>
    public SchoolType SchoolType { get; set; } = SchoolSettingsDefaults.SchoolType;
```

Puis, dans `SchoolSettingsDefaults` (juste après la ligne `public const TypeEtablissement TypeEtablissement = Enums.TypeEtablissement.Prive;`) :

```csharp

    /// <summary>
    /// Classification par défaut : Standard. Franco-Arabe et Daara restent un choix explicite du
    /// Directeur, jamais déduit — même philosophie que TypeEtablissement.
    /// </summary>
    public const SchoolType SchoolType = Enums.SchoolType.Standard;
```

- [ ] **Step 4 : Mapper la colonne dans la configuration EF**

Trouver `src/SamaEcole.Persistence/Configurations/SchoolSettingsConfiguration.cs`, repérer la ligne qui mappe `TypeEtablissement` (`builder.Property(s => s.TypeEtablissement).HasConversion<string>()...`) et ajouter juste après :

```csharp
        builder.Property(s => s.SchoolType).HasConversion<string>().HasMaxLength(20).IsRequired();
```

- [ ] **Step 5 : Lancer le test pour vérifier qu'il passe**

Run: `dotnet test tests/SamaEcole.UnitTests --filter FullyQualifiedName~SchoolSettingsSchoolTypeTests`
Expected: PASS

- [ ] **Step 6 : Commit**

```bash
git add src/SamaEcole.Domain/Entities/SchoolSettings.cs src/SamaEcole.Persistence/Configurations/SchoolSettingsConfiguration.cs tests/SamaEcole.UnitTests/Quran/SchoolSettingsSchoolTypeTests.cs
git commit -m "feat(quran): ajoute SchoolSettings.SchoolType"
```

---

### Task 4 : Entité `QuranProgress`

**Files:**
- Create: `src/SamaEcole.Domain/Entities/QuranProgress.cs`
- Create: `src/SamaEcole.Persistence/Configurations/QuranProgressConfiguration.cs`
- Modify: `src/SamaEcole.Persistence/ApplicationDbContext.cs`
- Modify: `src/SamaEcole.Application/Common/Interfaces/IApplicationDbContext.cs`
- Test: `tests/SamaEcole.UnitTests/Quran/QuranProgressTests.cs`

**Interfaces:**
- Consumes : `SamaEcole.Domain.Enums.QuranMemorizationStatus` (Task 1), `SamaEcole.Domain.Common.AuditableEntity`/`ITenantEntity`.
- Produces : `QuranProgress` (Id, SchoolId, StudentId, JuzNumber, HizbNumber, SurahNumber, Status, EvaluationDate, Notes), `IApplicationDbContext.QuranProgresses` — consommé par Task 6 (migration) et Task 7 (tests d'intégration).

- [ ] **Step 1 : Écrire le test (échoue — l'entité n'existe pas)**

```csharp
// tests/SamaEcole.UnitTests/Quran/QuranProgressTests.cs
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace SamaEcole.UnitTests.Quran;

public class QuranProgressTests
{
    [Fact]
    public void A_New_Progress_Entry_Defaults_To_InProcess()
    {
        var entry = new QuranProgress
        {
            SchoolId = Guid.NewGuid(),
            StudentId = Guid.NewGuid(),
            JuzNumber = 1,
            HizbNumber = 1,
            SurahNumber = 1
        };

        entry.Status.Should().Be(QuranMemorizationStatus.InProcess);
        entry.IsDeleted.Should().BeFalse();
    }
}
```

- [ ] **Step 2 : Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test tests/SamaEcole.UnitTests --filter FullyQualifiedName~QuranProgressTests`
Expected: FAIL (compilation error — `SamaEcole.Domain.Entities.QuranProgress` n'existe pas)

- [ ] **Step 3 : Créer l'entité**

```csharp
// src/SamaEcole.Domain/Entities/QuranProgress.cs
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Suivi individuel de mémorisation coranique (module Coran/Franco-Arabe, docs/superpowers/specs/
/// 2026-09-20-franco-arabic-core-design.md §3.3). Une ligne par observation — plusieurs lignes
/// peuvent exister pour le même (StudentId, SurahNumber) au fil du temps, aucune contrainte
/// d'unicité n'est posée dans ce lot.
///
/// VERROU OPTIMISTE (AGENTS.md règle #5) : plusieurs enseignants peuvent suivre la mémorisation du
/// même élève. Le jeton est xmin, configuré dans QuranProgressConfiguration — comme Grade.
/// </summary>
public class QuranProgress : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid StudentId { get; set; }

    /// <summary>Numéro de Juz (1 à 30) concerné par cette observation.</summary>
    public int JuzNumber { get; set; }

    /// <summary>Numéro de Hizb (1 à 60) concerné par cette observation.</summary>
    public int HizbNumber { get; set; }

    /// <summary>Numéro de sourate (1 à 114) concernée par cette observation.</summary>
    public int SurahNumber { get; set; }

    /// <summary>Défaut <see cref="QuranMemorizationStatus.InProcess"/> : une ligne commence toujours en cours.</summary>
    public QuranMemorizationStatus Status { get; set; } = QuranMemorizationStatus.InProcess;

    /// <summary>Date de la dernière observation. Nullable : une ligne peut être créée avant toute évaluation formelle.</summary>
    public DateOnly? EvaluationDate { get; set; }

    public string? Notes { get; set; }
}
```

- [ ] **Step 4 : Créer la configuration EF**

```csharp
// src/SamaEcole.Persistence/Configurations/QuranProgressConfiguration.cs
using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL équivalente est posée par la migration AddQuranCoreModule
/// (AGENTS.md règle #2 : les DEUX protections, jamais une seule).
/// </summary>
public class QuranProgressConfiguration : IEntityTypeConfiguration<QuranProgress>
{
    public void Configure(EntityTypeBuilder<QuranProgress> builder)
    {
        builder.ToTable("quran_progress");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.SchoolId).IsRequired();

        // Verrou optimiste xmin (AGENTS.md règle #5), comme Grade et Subject.
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Notes).HasMaxLength(2000);

        builder.HasIndex(p => new { p.SchoolId, p.StudentId });

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(p => p.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK COMPOSITE (SchoolId, StudentId) : même défense qu'ailleurs (Grade → Student) contre un
        // suivi qui pointerait l'élève d'une AUTRE école.
        builder.HasOne<Student>()
            .WithMany()
            .HasForeignKey(p => new { p.SchoolId, p.StudentId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 5 : Exposer le DbSet**

Dans `src/SamaEcole.Persistence/ApplicationDbContext.cs`, ajouter après `public DbSet<StudentMutationCertificate> ...` (ou toute autre ligne de `DbSet<>`, l'ordre n'a pas d'importance) :

```csharp
    public DbSet<QuranProgress> QuranProgresses => Set<QuranProgress>();
```

Dans `src/SamaEcole.Application/Common/Interfaces/IApplicationDbContext.cs`, ajouter :

```csharp
    /// <summary>Suivi individuel de mémorisation coranique (module Coran/Franco-Arabe). Verrou optimiste xmin.</summary>
    DbSet<QuranProgress> QuranProgresses { get; }
```

- [ ] **Step 6 : Lancer le test pour vérifier qu'il passe**

Run: `dotnet test tests/SamaEcole.UnitTests --filter FullyQualifiedName~QuranProgressTests`
Expected: PASS

- [ ] **Step 7 : Vérifier que le projet compile dans son ensemble**

Run: `dotnet build`
Expected: 0 erreur (l'implémentation concrète `ApplicationDbContext` doit satisfaire le nouveau membre de l'interface).

- [ ] **Step 8 : Commit**

```bash
git add src/SamaEcole.Domain/Entities/QuranProgress.cs src/SamaEcole.Persistence/Configurations/QuranProgressConfiguration.cs src/SamaEcole.Persistence/ApplicationDbContext.cs src/SamaEcole.Application/Common/Interfaces/IApplicationDbContext.cs tests/SamaEcole.UnitTests/Quran/QuranProgressTests.cs
git commit -m "feat(quran): ajoute l'entité préparatoire QuranProgress"
```

---

### Task 5 : Entité `QuranEvaluation`

**Files:**
- Create: `src/SamaEcole.Domain/Entities/QuranEvaluation.cs`
- Create: `src/SamaEcole.Persistence/Configurations/QuranEvaluationConfiguration.cs`
- Modify: `src/SamaEcole.Persistence/ApplicationDbContext.cs`
- Modify: `src/SamaEcole.Application/Common/Interfaces/IApplicationDbContext.cs`
- Test: `tests/SamaEcole.UnitTests/Quran/QuranEvaluationTests.cs`

**Interfaces:**
- Consumes : `SamaEcole.Domain.Common.AuditableEntity`/`ITenantEntity`.
- Produces : `QuranEvaluation` (Id, SchoolId, StudentId, EvaluationDate, MemoryMistakes, TajwidMistakes, Hesitations, FinalScore), `IApplicationDbContext.QuranEvaluations` — consommé par Task 6 et Task 7.

- [ ] **Step 1 : Écrire le test (échoue)**

```csharp
// tests/SamaEcole.UnitTests/Quran/QuranEvaluationTests.cs
using SamaEcole.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace SamaEcole.UnitTests.Quran;

public class QuranEvaluationTests
{
    [Fact]
    public void A_New_Evaluation_Starts_With_Zero_Mistakes_And_No_Score()
    {
        var evaluation = new QuranEvaluation
        {
            SchoolId = Guid.NewGuid(),
            StudentId = Guid.NewGuid(),
            EvaluationDate = new DateOnly(2026, 9, 20)
        };

        evaluation.MemoryMistakes.Should().Be(0);
        evaluation.TajwidMistakes.Should().Be(0);
        evaluation.Hesitations.Should().Be(0);
        evaluation.FinalScore.Should().Be(0);
    }
}
```

- [ ] **Step 2 : Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test tests/SamaEcole.UnitTests --filter FullyQualifiedName~QuranEvaluationTests`
Expected: FAIL (compilation error)

- [ ] **Step 3 : Créer l'entité**

```csharp
// src/SamaEcole.Domain/Entities/QuranEvaluation.cs
using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Note d'examen oral de récitation coranique (module Coran/Franco-Arabe, docs/superpowers/specs/
/// 2026-09-20-franco-arabic-core-design.md §3.4). <see cref="EvaluationDate"/> remplace le
/// <c>ExamId</c> envisagé initialement : aucune entité "session d'examen Coran" n'existe ni n'est
/// justifiée dans ce lot (décision #4 de la spec).
///
/// VERROU OPTIMISTE (AGENTS.md règle #5) : c'est une note, au même titre que <see cref="Grade"/>.
/// Le jeton est xmin, configuré dans QuranEvaluationConfiguration.
/// </summary>
public class QuranEvaluation : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid StudentId { get; set; }

    public DateOnly EvaluationDate { get; set; }

    public int MemoryMistakes { get; set; }

    public int TajwidMistakes { get; set; }

    public int Hesitations { get; set; }

    /// <summary>Note finale de l'examen oral, sur le barème que l'école choisit pour cet exercice.</summary>
    public decimal FinalScore { get; set; }
}
```

- [ ] **Step 4 : Créer la configuration EF**

```csharp
// src/SamaEcole.Persistence/Configurations/QuranEvaluationConfiguration.cs
using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL équivalente est posée par la migration AddQuranCoreModule
/// (AGENTS.md règle #2 : les DEUX protections, jamais une seule).
/// </summary>
public class QuranEvaluationConfiguration : IEntityTypeConfiguration<QuranEvaluation>
{
    public void Configure(EntityTypeBuilder<QuranEvaluation> builder)
    {
        builder.ToTable("quran_evaluations");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.SchoolId).IsRequired();

        // Verrou optimiste xmin (AGENTS.md règle #5) : c'est une note, comme Grade.
        builder.Property<uint>("xmin").IsRowVersion();

        // Même précision que Grade.Value : le barème d'un examen oral n'a aucune raison de dépasser 999,99.
        builder.Property(e => e.FinalScore).IsRequired().HasPrecision(5, 2);

        builder.HasIndex(e => new { e.SchoolId, e.StudentId });

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(e => e.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Student>()
            .WithMany()
            .HasForeignKey(e => new { e.SchoolId, e.StudentId })
            .HasPrincipalKey(s => new { s.SchoolId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 5 : Exposer le DbSet**

Dans `src/SamaEcole.Persistence/ApplicationDbContext.cs` :

```csharp
    public DbSet<QuranEvaluation> QuranEvaluations => Set<QuranEvaluation>();
```

Dans `src/SamaEcole.Application/Common/Interfaces/IApplicationDbContext.cs` :

```csharp
    /// <summary>Notes d'examen oral de récitation coranique (module Coran/Franco-Arabe). Verrou optimiste xmin.</summary>
    DbSet<QuranEvaluation> QuranEvaluations { get; }
```

- [ ] **Step 6 : Lancer le test pour vérifier qu'il passe**

Run: `dotnet test tests/SamaEcole.UnitTests --filter FullyQualifiedName~QuranEvaluationTests`
Expected: PASS

- [ ] **Step 7 : Vérifier que le projet compile dans son ensemble**

Run: `dotnet build`
Expected: 0 erreur

- [ ] **Step 8 : Commit**

```bash
git add src/SamaEcole.Domain/Entities/QuranEvaluation.cs src/SamaEcole.Persistence/Configurations/QuranEvaluationConfiguration.cs src/SamaEcole.Persistence/ApplicationDbContext.cs src/SamaEcole.Application/Common/Interfaces/IApplicationDbContext.cs tests/SamaEcole.UnitTests/Quran/QuranEvaluationTests.cs
git commit -m "feat(quran): ajoute l'entité préparatoire QuranEvaluation"
```

---

### Task 6 : Migration `AddQuranCoreModule`

**Files:**
- Create (générée puis éditée à la main) : `src/SamaEcole.Persistence/Migrations/<timestamp>_AddQuranCoreModule.cs`
- Modify (générée automatiquement) : `src/SamaEcole.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs`

**Interfaces:**
- Consumes : le modèle EF complet des Tasks 2-5.
- Produces : colonnes `subjects.section_type`, `school_settings.school_type`, tables `quran_progress`/`quran_evaluations` avec RLS — consommé par Task 7 et Task 8 (les tests migrent la base via `RlsTestDatabase.InitializeAsync`, qui appelle `Database.MigrateAsync()`).

- [ ] **Step 1 : Générer la migration**

```bash
dotnet ef migrations add AddQuranCoreModule -p src/SamaEcole.Persistence -s src/SamaEcole.Web
```

- [ ] **Step 2 : Vérifier les valeurs par défaut générées pour les deux nouvelles colonnes d'enum**

Ouvrir le fichier généré. Chercher les lignes `Up()` qui ajoutent `section_type` sur `subjects` et
`school_type` sur `school_settings`. **Vérifier — et corriger si besoin** — qu'elles portent bien :

```csharp
migrationBuilder.AddColumn<string>(
    name: "section_type",
    table: "subjects",
    type: "character varying(20)",
    maxLength: 20,
    nullable: false,
    defaultValue: "French");

migrationBuilder.AddColumn<string>(
    name: "school_type",
    table: "school_settings",
    type: "character varying(20)",
    maxLength: 20,
    nullable: false,
    defaultValue: "Standard");
```

C'est le point de vigilance de cette tâche : EF Core propose parfois `defaultValue: ""` pour une
colonne enum-string plutôt que le nom du premier membre — c'est exactement l'écart qui a cassé la
lecture de `ParentSummonsStatus` en production (ACTIVE_CONTEXT.md, incident du 02/09/2026, `""`
n'étant pas une valeur valide de l'enum). Si le texte généré diffère de `"French"`/`"Standard"`,
le corriger à la main avant de continuer.

- [ ] **Step 3 : Ajouter le bloc RLS**

Toujours dans le fichier généré, transformer la classe en `partial class AddQuranCoreModule : Migration`
avec le motif exact des migrations précédentes (voir `AddClassJournal`, `AddInternatBoarding`). Ajouter
en tête de classe :

```csharp
        private static readonly string[] TenantTables = ["quran_progress", "quran_evaluations"];

        private const string AppRole = "sama_ecole_app";
```

À la toute fin de la méthode `Up(MigrationBuilder migrationBuilder)` générée (après la dernière
`CreateIndex`/`CreateTable`), ajouter :

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

Et au DÉBUT de la méthode `Down(MigrationBuilder migrationBuilder)` générée (avant les `DropTable`) :

```csharp
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS {table}_tenant_isolation ON \"{table}\";");
            }
```

- [ ] **Step 4 : Vérifier la compilation**

Run: `dotnet build`
Expected: 0 erreur

- [ ] **Step 5 : Commit**

```bash
git add src/SamaEcole.Persistence/Migrations/
git commit -m "feat(quran): migration AddQuranCoreModule (RLS incluse)"
```

---

### Task 7 : Tests d'intégration — schéma et concurrence

**Files:**
- Create: `tests/SamaEcole.IntegrationTests/Quran/QuranCoreSchemaTests.cs`

**Interfaces:**
- Consumes : `RlsTestDatabase` (`tests/SamaEcole.IntegrationTests/Common/RlsTestDatabase.cs`), `ApplicationDbContext.QuranProgresses`/`QuranEvaluations`/`Subjects`/`SchoolSettings` (Tasks 2-5), `ApplicationDbContext` role applicatif via `NewAppContext`.

- [ ] **Step 1 : Écrire les tests**

```csharp
// tests/SamaEcole.IntegrationTests/Quran/QuranCoreSchemaTests.cs
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using Xunit;

namespace SamaEcole.IntegrationTests.Quran;

/// <summary>
/// Socle Franco-Arabe/Daara (docs/superpowers/specs/2026-09-20-franco-arabic-core-design.md) :
/// les valeurs par défaut résistent-elles à un aller-retour PAR LA BASE (pas seulement en mémoire),
/// et le verrou optimiste xmin tient-il RÉELLEMENT sur les deux nouvelles tables ?
/// </summary>
[Trait("Category", "MultiTenant")]
public class QuranCoreSchemaTests : IAsyncLifetime
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
        owner.Students.Add(new Student
        {
            Id = Eleve,
            SchoolId = Ecole,
            Matricule = "ELEV-2026-0001",
            FullName = "Élève de test",
            BirthDate = new DateOnly(2015, 1, 1),
            BirthPlace = "Dakar",
            Gender = "M",
            ClassroomId = Classe
        });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task A_Subject_Inserted_Without_SectionType_Reads_Back_As_French()
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var subject = new Subject { SchoolId = Ecole, Name = "Mathématiques", Level = "Primaire", Coefficient = 4 };
        ctx.Subjects.Add(subject);
        await ctx.SaveChangesAsync(CancellationToken.None);

        await using var reload = _db.NewAppContext(Ecole);
        var reloaded = await reload.Subjects.FirstAsync(s => s.Id == subject.Id);

        reloaded.SectionType.Should().Be(SectionType.French);
    }

    [Fact]
    public async Task School_Settings_Inserted_Without_SchoolType_Reads_Back_As_Standard()
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var settings = new SchoolSettings { SchoolId = Ecole };
        ctx.SchoolSettings.Add(settings);
        await ctx.SaveChangesAsync(CancellationToken.None);

        await using var reload = _db.NewAppContext(Ecole);
        var reloaded = await reload.SchoolSettings.FirstAsync(s => s.SchoolId == Ecole);

        reloaded.SchoolType.Should().Be(SchoolType.Standard);
    }

    [Fact]
    public async Task Two_Concurrent_Corrections_On_The_Same_Progress_Entry_The_Second_Is_Refused()
    {
        Guid entryId;
        await using (var seed = _db.NewAppContext(Ecole))
        {
            var entry = new QuranProgress
            {
                SchoolId = Ecole, StudentId = Eleve, JuzNumber = 1, HizbNumber = 1, SurahNumber = 1
            };
            seed.QuranProgresses.Add(entry);
            await seed.SaveChangesAsync(CancellationToken.None);
            entryId = entry.Id;
        }

        await using var ctxA = _db.NewAppContext(Ecole);
        await using var ctxB = _db.NewAppContext(Ecole);

        var entryA = await ctxA.QuranProgresses.FirstAsync(p => p.Id == entryId);
        var entryB = await ctxB.QuranProgresses.FirstAsync(p => p.Id == entryId);

        entryA.Status = QuranMemorizationStatus.Memorized;
        await ctxA.SaveChangesAsync(CancellationToken.None);

        entryB.Status = QuranMemorizationStatus.Revised;
        var act = async () => await ctxB.SaveChangesAsync(CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>(
            "deux corrections concurrentes sur le même suivi ne doivent jamais s'écraser en silence (règle #5)");
    }

    [Fact]
    public async Task Two_Concurrent_Corrections_On_The_Same_Evaluation_The_Second_Is_Refused()
    {
        Guid evaluationId;
        await using (var seed = _db.NewAppContext(Ecole))
        {
            var evaluation = new QuranEvaluation
            {
                SchoolId = Ecole, StudentId = Eleve, EvaluationDate = new DateOnly(2026, 9, 20), FinalScore = 15
            };
            seed.QuranEvaluations.Add(evaluation);
            await seed.SaveChangesAsync(CancellationToken.None);
            evaluationId = evaluation.Id;
        }

        await using var ctxA = _db.NewAppContext(Ecole);
        await using var ctxB = _db.NewAppContext(Ecole);

        var evalA = await ctxA.QuranEvaluations.FirstAsync(e => e.Id == evaluationId);
        var evalB = await ctxB.QuranEvaluations.FirstAsync(e => e.Id == evaluationId);

        evalA.FinalScore = 17;
        await ctxA.SaveChangesAsync(CancellationToken.None);

        evalB.FinalScore = 12;
        var act = async () => await ctxB.SaveChangesAsync(CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>(
            "deux corrections concurrentes sur la même évaluation ne doivent jamais s'écraser en silence (règle #5)");
    }
}
```

- [ ] **Step 2 : Lancer les tests**

Run: `dotnet test tests/SamaEcole.IntegrationTests --filter FullyQualifiedName~QuranCoreSchemaTests`
Expected: PASS (4/4). Nécessite Docker (Testcontainers démarre un PostgreSQL réel).

- [ ] **Step 3 : Commit**

```bash
git add tests/SamaEcole.IntegrationTests/Quran/QuranCoreSchemaTests.cs
git commit -m "test(quran): valeurs par défaut et verrou optimiste du socle Franco-Arabe"
```

---

### Task 8 : Isolation RLS dédiée

**Files:**
- Create: `tests/SamaEcole.IntegrationTests/Quran/QuranModuleIsolationTests.cs`

**Interfaces:**
- Consumes : `RlsTestDatabase.OpenRawAppConnectionAsync` (contourne EF Core, prouve l'isolation au niveau base — même patron que `ClassJournalIsolationTests`).

- [ ] **Step 1 : Écrire les tests**

```csharp
// tests/SamaEcole.IntegrationTests/Quran/QuranModuleIsolationTests.cs
using FluentAssertions;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Npgsql;
using Xunit;

namespace SamaEcole.IntegrationTests.Quran;

/// <summary>
/// Socle Franco-Arabe/Daara — l'isolation de quran_progress et quran_evaluations tient-elle dans
/// la BASE ? Tout en SQL BRUT avec le rôle applicatif, sans EF Core, pour que seule la policy RLS
/// fasse foi — même patron que ClassJournalIsolationTests.
/// </summary>
[Trait("Category", "MultiTenant")]
public class QuranModuleIsolationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid EleveA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid EleveB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });
        owner.Students.AddRange(
            new Student { Id = EleveA, SchoolId = EcoleA, Matricule = "ELEV-2026-0001", FullName = "Élève A", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Dakar", Gender = "M" },
            new Student { Id = EleveB, SchoolId = EcoleB, Matricule = "ELEV-2026-0001", FullName = "Élève B", BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Thiès", Gender = "F" });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Raw_Query_On_Quran_Progress_Should_Never_Return_Other_School_Rows()
    {
        await InsertProgressAsync(EcoleA, EleveA);
        await InsertProgressAsync(EcoleB, EleveB);

        var rows = await ReadProgressStudentsAsync(EcoleA);

        rows.Should().ContainSingle().Which.Should().Be(EleveA);
    }

    [Fact]
    public async Task Raw_Query_On_Quran_Evaluations_Should_Never_Return_Other_School_Rows()
    {
        await InsertEvaluationAsync(EcoleA, EleveA);
        await InsertEvaluationAsync(EcoleB, EleveB);

        var rows = await ReadEvaluationStudentsAsync(EcoleA);

        rows.Should().ContainSingle().Which.Should().Be(EleveA);
    }

    [Fact]
    public async Task Writing_A_Progress_Entry_Into_Another_School_Should_Be_Rejected()
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO quran_progress
                ("Id", "SchoolId", "StudentId", "JuzNumber", "HizbNumber", "SurahNumber", "Status", "CreatedAt", "IsDeleted")
            VALUES (gen_random_uuid(), @schoolId, @studentId, 1, 1, 1, 'InProcess', NOW(), FALSE);
            """;
        command.Parameters.AddWithValue("schoolId", EcoleB);
        command.Parameters.AddWithValue("studentId", EleveB);

        var act = async () => await command.ExecuteNonQueryAsync();

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task Deleting_A_Quran_Evaluation_Row_Is_Refused_By_Privilege_Not_Just_Policy()
    {
        var id = await InsertEvaluationAsync(EcoleA, EleveA);

        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);
        await using var command = connection.CreateCommand();
        command.CommandText = """DELETE FROM quran_evaluations WHERE "Id" = @id;""";
        command.Parameters.AddWithValue("id", id);

        var act = async () => await command.ExecuteNonQueryAsync();

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    private async Task<Guid> InsertProgressAsync(Guid schoolId, Guid studentId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO quran_progress
                ("Id", "SchoolId", "StudentId", "JuzNumber", "HizbNumber", "SurahNumber", "Status", "CreatedAt", "IsDeleted")
            VALUES (@id, @schoolId, @studentId, 1, 1, 1, 'InProcess', NOW(), FALSE);
            """;
        var id = Guid.NewGuid();
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("schoolId", schoolId);
        command.Parameters.AddWithValue("studentId", studentId);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<Guid> InsertEvaluationAsync(Guid schoolId, Guid studentId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO quran_evaluations
                ("Id", "SchoolId", "StudentId", "EvaluationDate", "MemoryMistakes", "TajwidMistakes", "Hesitations", "FinalScore", "CreatedAt", "IsDeleted")
            VALUES (@id, @schoolId, @studentId, DATE '2026-09-20', 0, 0, 0, 15, NOW(), FALSE);
            """;
        var id = Guid.NewGuid();
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("schoolId", schoolId);
        command.Parameters.AddWithValue("studentId", studentId);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<List<Guid>> ReadProgressStudentsAsync(Guid schoolId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "StudentId" FROM quran_progress ORDER BY "StudentId";""";
        return await ReadGuidColumnAsync(command);
    }

    private async Task<List<Guid>> ReadEvaluationStudentsAsync(Guid schoolId)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "StudentId" FROM quran_evaluations ORDER BY "StudentId";""";
        return await ReadGuidColumnAsync(command);
    }

    private static async Task<List<Guid>> ReadGuidColumnAsync(NpgsqlCommand command)
    {
        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetGuid(0));
        }
        return ids;
    }
}
```

- [ ] **Step 2 : Lancer la suite d'isolation et la couverture RLS générique**

Run: `dotnet test --filter Category=MultiTenant`
Expected: PASS pour `QuranModuleIsolationTests` (4/4) ET pour `RlsCoverageTests` (qui découvre
automatiquement `quran_progress`/`quran_evaluations` via `ITenantEntity` et vérifie que la policy
posée en Task 6 est bien active).

- [ ] **Step 3 : Commit**

```bash
git add tests/SamaEcole.IntegrationTests/Quran/QuranModuleIsolationTests.cs
git commit -m "test(quran): isolation RLS de quran_progress et quran_evaluations"
```

---

### Task 9 : Suite complète et clôture

**Files:** aucun changement de fichier — vérification finale uniquement.

- [ ] **Step 1 : Lancer la suite complète**

Run: `dotnet test`
Expected: 0 échec, aucune régression sur les modules existants (changements strictement additifs :
deux colonnes NOT NULL avec défaut sur des tables déjà en production, deux nouvelles tables sans
consommateur).

- [ ] **Step 2 : Si un test préexistant échoue**

Ne pas modifier son code sans comprendre pourquoi — un échec inattendu ici signale une régression,
pas un test à ajuster. Chercher en priorité un conflit de nommage (ex. un test qui énumère déjà
tous les membres de `SchoolModule` ou toutes les tables tenant en dur) avant de suspecter le
schéma lui-même.

- [ ] **Step 3 : Récapitulatif final**

Vérifier que l'arbre de travail est propre (`git status`) et que les 9 commits de ce plan sont bien
sur `feature/franco-arabic-core`. Ne PAS pousser la branche ni ouvrir de Pull Request sans
confirmation explicite de l'utilisateur.

---

## Self-Review

**1. Couverture de la spec** — chaque section de `2026-09-20-franco-arabic-core-design.md` a une tâche :
§3.1 → Task 2, §3.2 → Task 3, §3.3 → Task 4, §3.4 → Task 5, §3.5 (migration + RLS) → Task 6,
décision #6 (xmin) → Tasks 4/5/7, §5 (tests) → Tasks 7/8/9. §3.6 et §6 (« hors périmètre ») n'ont
délibérément aucune tâche — c'est leur rôle.

**2. Placeholders** — aucun « TBD »/« TODO » ; chaque étape de code contient le code réel à écrire.

**3. Cohérence des types** — `SectionType`/`SchoolType`/`QuranMemorizationStatus` (Task 1) sont
utilisés avec l'orthographe et les valeurs identiques dans toutes les tâches suivantes ;
`QuranProgress`/`QuranEvaluation` gardent les mêmes noms de propriétés entre leur définition
(Tasks 4/5) et leur usage dans les tests (Tasks 7/8) ; `IApplicationDbContext.QuranProgresses`/
`QuranEvaluations` correspondent aux DbSet ajoutés sur `ApplicationDbContext`.
