# Périodes d'évaluation dynamiques (Évolution N°2) — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal :** Le Directeur choisit le découpage de l'année (Trimestriel par défaut, Semestriel, Personnalisé) ; les périodes générées, les sélecteurs de l'interface et les documents PDF suivent ce choix.

**Architecture :** Les périodes sont **déjà pilotées par les données** : `Term` porte `Label` + `Order`, les notes et remarques pointent un `TermId`, tous les sélecteurs (`grades.js`, fiche élève) listent `GET /school-years/{id}/terms`, et le récapitulatif annuel du bulletin gère un nombre variable de périodes. Le seul endroit qui code « 3 trimestres » en dur est `TermSchedule` (génération à la création de l'année). Le plan remplace donc `TermSchedule` par un `PeriodSchedule` paramétré par un enum stocké dans `SchoolSettings`, ajoute une action explicite pour rejouer le découpage sur une année existante *sans donnée*, puis balaie le vocabulaire « trimestre » resté en dur dans l'UI et les PDF.

**Tech Stack :** ASP.NET Core 9, EF Core/Npgsql, MediatR + FluentValidation, QuestPDF, Alpine.js, xUnit (+ FluentAssertions), `node --test`.

**Spec :** pas de document de spécification séparé — la spécification est le message de l'utilisateur du 24/09/2026 (« Spécification Évolution N°2 ») :
- enum `EvaluationPeriodType` dans `SchoolSettings` : Trimestriel (défaut), Semestriel, Personnalisé ;
- sélecteurs dynamiques (saisie des notes, calcul des moyennes, saisie des bulletins) : seules les périodes valides s'affichent ;
- moteur QuestPDF : les en-têtes de bulletins et relevés affichent le bon libellé (« BULLETIN DU 1ER SEMESTRE » et non « TRIMESTRE » forcé).

## Global Constraints

- PostgreSQL uniquement ; migration EF Core **nouvelle** (jamais modifier une migration appliquée). — `AGENTS.md` règles « Ne jamais faire ».
- Enum stocké en **string** (`HasConversion<string>()`), comme `SchoolType` — convention actuelle du projet (`SchoolSettingsConfiguration.cs:60-67`).
- `terms` est une table tenant déjà couverte par RLS + Global Query Filter : aucune nouvelle table, donc rien à ajouter à `TenantTables`.
- Aucune suppression physique : les périodes remplacées sont **soft-deleted** (`SoftDelete(actorId.ToString())`). — règle #6.
- Aucune logique métier dans un contrôleur ni une entité. — règle #8. CQRS : une Command pour l'écriture. — règle #7.
- `PUT /schools/current/settings` est un **remplacement complet** : tout nouveau champ doit être ajouté au DTO, à la commande, au handler, à `GetSchoolSettingsQuery` **et** à `settings.js` (qui relit avant d'écrire depuis le 24/09/2026 : un champ absent du DTO serait perdu à chaque enregistrement).
- Le bulletin suit `docs/design-references/` à l'identique (règle #12) : toute modification de sa mise en page passe par l'arbitrage **D2** ci-dessous.
- Tests obligatoires pour tout ce qui touche notes/bulletins/isolation. Convention de commits : Conventional Commits, un commit par tâche, terminé par `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`.
- **Le propriétaire lance lui-même la suite complète `dotnet test`** (consigne du 17/09/2026). Les étapes ci-dessous n'exécutent que des tests **ciblés** (`--filter`), à lancer sur demande ; `dotnet build` et `node --test` restent libres.
- Libellés : « 1er / 2e / 3e trimestre », « 1er / 2e semestre », « 1re / 2e / … période » (féminin pour « période »).

## Arbitrages à confirmer avant de commencer

| # | Question | Recommandation (appliquée par ce plan) |
|---|---|---|
| D1 | Que signifie « Personnalisé » ? La spec ne le définit pas. | **Nombre de périodes choisi par le Directeur, de 2 à 6** (`CustomPeriodCount`), dates réparties à parts égales, libellés « 1re période »… Les libellés/dates éditables période par période sont **hors périmètre** (YAGNI ; à rouvrir si une école le demande). |
| D2 | Titre du bulletin. Aujourd'hui `"BULLETIN DE NOTES"` (fixe, conforme à `design-references/README.md` §2 point 2) et le libellé de période s'imprime déjà dynamiquement dans l'en-tête droit. La spec demande « BULLETIN DU 1ER SEMESTRE ». | Implémenter la spec (Tâche 6, étape « titre ») **et mettre à jour `design-references/README.md`** dans le même commit, puisque la règle #12 fait de ce README la référence. Si l'arbitrage est refusé, sauter uniquement cette étape : l'en-tête droit reste correct. |
| D3 | Changer le type quand l'année en cours a déjà des périodes. | **Jamais automatique.** Le changement n'affecte que les années créées ensuite ; l'année existante se met à jour par une action explicite (Tâche 4), refusée dès qu'une note ou une appréciation de bulletin pointe l'une de ses périodes. |
| D4 | Faut-il un code court « S1 / T1 » ? | Non : les sélecteurs affichent déjà `Term.Label` en toutes lettres. Aucune colonne `ShortLabel` (YAGNI). |

## File Structure

| Fichier | Rôle |
|---|---|
| `src/SamaEcole.Domain/Enums/EvaluationPeriodType.cs` (créer) | L'enum. |
| `src/SamaEcole.Domain/Entities/SchoolSettings.cs` (modifier) | `EvaluationPeriodType`, `CustomPeriodCount` + défauts/bornes dans `SchoolSettingsDefaults`. |
| `src/SamaEcole.Application/SchoolYears/PeriodSchedule.cs` (créer, remplace `TermSchedule.cs`) | Pur : nombre, libellés, dates des périodes. |
| `src/SamaEcole.Application/SchoolYears/Commands/ApplyEvaluationPeriods/*` (créer) | Rejouer le découpage sur une année sans donnée. |
| `CreateSchoolYearCommandHandler.cs`, `UpdateSchoolYearCommandHandler.cs` (modifier) | Consomment `PeriodSchedule`. |
| `SchoolSettingsDto.cs`, `UpdateSchoolSettingsCommand*.cs`, `GetSchoolSettingsQuery.cs`, `SchoolSettingsController.cs`, `SchoolSettingsConfiguration.cs` (modifier) | Plomberie du réglage. |
| `src/SamaEcole.Persistence/Migrations/*_AddEvaluationPeriodType.cs` (générer) | Colonnes + défauts. |
| `src/SamaEcole.Infrastructure/Documents/GradeSheetDocument.cs`, `ReportCardDocument.cs` + nouveau `BulletinTitle.cs` (modifier/créer) | Libellés PDF. |
| `wwwroot/js/settings.js`, `Views/Settings/Index.cshtml`, `wwwroot/js/school-years.js`, `Views/Shared/_SchoolYearsPanel.cshtml`, `Views/Grades/Index.cshtml`, `Views/Students/Index.cshtml`, `wwwroot/js/help.js` (modifier) | Interface et vocabulaire. |
| `tests/SamaEcole.UnitTests/SchoolYears/PeriodScheduleTests.cs` (créer) et voisins | Tests. |

---

### Task 1 : L'enum et `PeriodSchedule` (pur, sans base)

**Files:**
- Create: `src/SamaEcole.Domain/Enums/EvaluationPeriodType.cs`
- Create: `src/SamaEcole.Application/SchoolYears/PeriodSchedule.cs`
- Delete: `src/SamaEcole.Application/SchoolYears/TermSchedule.cs` (à la Tâche 3, une fois ses deux appelants migrés — jusque-là il reste, pour garder le build vert)
- Test: `tests/SamaEcole.UnitTests/SchoolYears/PeriodScheduleTests.cs`

**Interfaces:**
- Produces:
  - `enum EvaluationPeriodType { Trimester = 0, Semester = 1, Custom = 2 }`
  - `PeriodSchedule.MinCustomCount = 2`, `PeriodSchedule.MaxCustomCount = 6`
  - `static int PeriodSchedule.CountFor(EvaluationPeriodType type, int customCount)`
  - `static string PeriodSchedule.LabelFor(EvaluationPeriodType type, int order)` (`order` commence à 1)
  - `static IReadOnlyList<(DateOnly Start, DateOnly End)> PeriodSchedule.SplitDates(DateOnly start, DateOnly end, int count)`
  - `static IReadOnlyList<(string Label, DateOnly Start, DateOnly End)> PeriodSchedule.Split(DateOnly start, DateOnly end, EvaluationPeriodType type, int customCount)`

- [ ] **Step 1 : écrire les tests qui échouent**

```csharp
// tests/SamaEcole.UnitTests/SchoolYears/PeriodScheduleTests.cs
using FluentAssertions;
using SamaEcole.Application.SchoolYears;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.SchoolYears;

public class PeriodScheduleTests
{
    private static readonly DateOnly Start = new(2026, 10, 1);
    private static readonly DateOnly End = new(2027, 6, 30);

    [Theory]
    [InlineData(EvaluationPeriodType.Trimester, 9, 3)]   // customCount ignoré hors Personnalisé
    [InlineData(EvaluationPeriodType.Semester, 9, 2)]
    [InlineData(EvaluationPeriodType.Custom, 4, 4)]
    public void CountFor_Follows_The_Type(EvaluationPeriodType type, int custom, int expected)
        => PeriodSchedule.CountFor(type, custom).Should().Be(expected);

    [Fact]
    public void Trimester_Labels_Are_The_Historical_Ones()
    {
        PeriodSchedule.Split(Start, End, EvaluationPeriodType.Trimester, 3)
            .Select(p => p.Label)
            .Should().Equal("1er trimestre", "2e trimestre", "3e trimestre");
    }

    [Fact]
    public void Semester_Labels()
    {
        PeriodSchedule.Split(Start, End, EvaluationPeriodType.Semester, 3)
            .Select(p => p.Label)
            .Should().Equal("1er semestre", "2e semestre");
    }

    [Fact]
    public void Custom_Labels_Use_The_Feminine_Ordinal()
    {
        PeriodSchedule.Split(Start, End, EvaluationPeriodType.Custom, 4)
            .Select(p => p.Label)
            .Should().Equal("1re période", "2e période", "3e période", "4e période");
    }

    [Theory]
    [InlineData(EvaluationPeriodType.Trimester, 3)]
    [InlineData(EvaluationPeriodType.Semester, 2)]
    [InlineData(EvaluationPeriodType.Custom, 6)]
    public void Periods_Are_Consecutive_Without_Gap_Or_Overlap_And_Cover_The_Whole_Year(
        EvaluationPeriodType type, int custom)
    {
        var periods = PeriodSchedule.Split(Start, End, type, custom);

        periods[0].Start.Should().Be(Start);
        periods[^1].End.Should().Be(End);
        for (var i = 1; i < periods.Count; i++)
        {
            periods[i].Start.Should().Be(periods[i - 1].End.AddDays(1));
        }
    }

    [Fact]
    public void Trimester_Dates_Are_Identical_To_The_Historical_TermSchedule()
    {
        // Non-régression : les années déjà créées avec l'ancien découpage doivent retomber sur les
        // mêmes bornes quand leurs dates sont modifiées (UpdateSchoolYear).
        var legacy = TermSchedule.Split(Start, End);
        var current = PeriodSchedule.Split(Start, End, EvaluationPeriodType.Trimester, 3);

        current.Select(p => (p.Start, p.End)).Should().Equal(legacy.Select(p => (p.Start, p.End)));
    }

    [Fact]
    public void The_Last_Period_Absorbs_The_Remainder()
    {
        // 10 jours en 3 périodes : 3 + 3 + 4.
        var periods = PeriodSchedule.SplitDates(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10), 3);

        periods.Select(p => p.End.DayNumber - p.Start.DayNumber + 1).Should().Equal(3, 3, 4);
    }
}
```

`TermSchedule` est `internal` dans `SamaEcole.Application` : pour que ce test compile, **rendre `TermSchedule` `public` temporairement** (supprimé à la Tâche 3, donc sans effet durable).

- [ ] **Step 2 : constater l'échec**

Run : `dotnet test tests/SamaEcole.UnitTests --filter "FullyQualifiedName~PeriodScheduleTests"`
Expected : erreur de compilation (`PeriodSchedule`/`EvaluationPeriodType` introuvables).

- [ ] **Step 3 : implémenter**

```csharp
// src/SamaEcole.Domain/Enums/EvaluationPeriodType.cs
namespace SamaEcole.Domain.Enums;

/// <summary>
/// Découpage de l'année scolaire en périodes d'évaluation, choisi par le Directeur (SchoolSettings).
/// Ne s'applique qu'aux années CRÉÉES ensuite ou explicitement rejouées : les notes pointent un
/// TermId, une année déjà notée garde son découpage. Stocké en string (convention du projet).
/// </summary>
public enum EvaluationPeriodType
{
    /// <summary>Trois trimestres — le système sénégalais standard, et le défaut.</summary>
    Trimester = 0,

    /// <summary>Deux semestres.</summary>
    Semester = 1,

    /// <summary>Un nombre de périodes choisi par l'école (SchoolSettings.CustomPeriodCount).</summary>
    Custom = 2
}
```

```csharp
// src/SamaEcole.Application/SchoolYears/PeriodSchedule.cs
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.SchoolYears;

/// <summary>
/// Découpage d'une année scolaire en périodes d'évaluation — remplace TermSchedule (3 trimestres en
/// dur). Tranches consécutives, sans trou ni chevauchement, la dernière absorbant le reste de la
/// division entière.
///
/// PARTAGÉ, à dessein, par les chemins qui posent ces dates : CreateSchoolYearCommandHandler
/// (crée les lignes), UpdateSchoolYearCommandHandler (recale les dates existantes, via SplitDates,
/// sans jamais recréer — les notes pointent un TermId) et ApplyEvaluationPeriodsCommandHandler.
/// </summary>
public static class PeriodSchedule
{
    public const int MinCustomCount = 2;
    public const int MaxCustomCount = 6;

    public static int CountFor(EvaluationPeriodType type, int customCount) => type switch
    {
        EvaluationPeriodType.Semester => 2,
        EvaluationPeriodType.Custom => customCount,
        _ => 3
    };

    /// <param name="order">Rang de la période, à partir de 1.</param>
    public static string LabelFor(EvaluationPeriodType type, int order)
    {
        var noun = type switch
        {
            EvaluationPeriodType.Semester => "semestre",
            EvaluationPeriodType.Custom => "période",
            _ => "trimestre"
        };

        // « période » est féminin : 1re. « trimestre » / « semestre » : 1er.
        var first = type == EvaluationPeriodType.Custom ? "1re" : "1er";
        var ordinal = order == 1 ? first : $"{order}e";

        return $"{ordinal} {noun}";
    }

    /// <summary>Bornes seules, sans libellé : ce dont le recalage d'une année existante a besoin.</summary>
    public static IReadOnlyList<(DateOnly Start, DateOnly End)> SplitDates(DateOnly start, DateOnly end, int count)
    {
        var chunk = (end.DayNumber - start.DayNumber + 1) / count;

        var periods = new List<(DateOnly Start, DateOnly End)>(count);
        var cursor = start;
        for (var i = 1; i <= count; i++)
        {
            var periodEnd = i == count ? end : cursor.AddDays(chunk - 1); // la dernière absorbe le reste
            periods.Add((cursor, periodEnd));
            cursor = periodEnd.AddDays(1);
        }

        return periods;
    }

    public static IReadOnlyList<(string Label, DateOnly Start, DateOnly End)> Split(
        DateOnly start, DateOnly end, EvaluationPeriodType type, int customCount)
        => SplitDates(start, end, CountFor(type, customCount))
            .Select((p, index) => (LabelFor(type, index + 1), p.Start, p.End))
            .ToList();
}
```

`CreateSchoolYearCommandValidator` impose une durée de 3 à 18 mois : `chunk` vaut au moins 15 jours même à 6 périodes, aucune garde supplémentaire n'est nécessaire.

- [ ] **Step 4 : constater le succès**

Run : `dotnet test tests/SamaEcole.UnitTests --filter "FullyQualifiedName~PeriodScheduleTests"`
Expected : PASS (9 cas).

- [ ] **Step 5 : commit**

```bash
git add src/SamaEcole.Domain/Enums/EvaluationPeriodType.cs src/SamaEcole.Application/SchoolYears tests/SamaEcole.UnitTests/SchoolYears/PeriodScheduleTests.cs
git commit -m "feat(periods): enum EvaluationPeriodType et PeriodSchedule paramétré"
```

---

### Task 2 : Le réglage dans `SchoolSettings` (entité, migration, API)

**Files:**
- Modify: `src/SamaEcole.Domain/Entities/SchoolSettings.cs` (propriétés + `SchoolSettingsDefaults`)
- Modify: `src/SamaEcole.Persistence/Configurations/SchoolSettingsConfiguration.cs` (après la ligne 67)
- Modify: `src/SamaEcole.Application/Schools/SchoolSettingsDto.cs`, `.../UpdateSchoolSettings/UpdateSchoolSettingsCommand.cs`, `...Handler.cs`, `...Validator.cs`, `.../GetSchoolSettings/GetSchoolSettingsQuery.cs` (`Map` **et** `Defaults`)
- Modify: `src/SamaEcole.Web/Controllers/SchoolSettingsController.cs` (record `UpdateSettingsRequest` + construction de la commande, ~ligne 108)
- Create (généré) : migration `AddEvaluationPeriodType`
- Test: `tests/SamaEcole.UnitTests/Schools/UpdateSchoolSettingsCommandValidatorTests.cs` (créer s'il n'existe pas ; sinon ajouter)

**Interfaces:**
- Consumes : `EvaluationPeriodType`, `PeriodSchedule.MinCustomCount/MaxCustomCount` (Tâche 1).
- Produces :
  - `SchoolSettings.EvaluationPeriodType` (défaut `Trimester`), `SchoolSettings.CustomPeriodCount` (défaut `3`)
  - `SchoolSettingsDefaults.EvaluationPeriodType`, `SchoolSettingsDefaults.CustomPeriodCount`
  - DTO / commande / requête : `string EvaluationPeriodType = "Trimester"`, `int CustomPeriodCount = 3` (en **dernière position**, après `GradeEditWindowDays`, avec valeur par défaut — comme les champs précédents)

- [ ] **Step 1 : tests du validateur qui échouent**

```csharp
// tests/SamaEcole.UnitTests/Schools/UpdateSchoolSettingsCommandValidatorTests.cs (ajout)
[Theory]
[InlineData("Trimester")]
[InlineData("Semester")]
[InlineData("Custom")]
public void Known_Period_Types_Are_Accepted(string type)
    => _validator.Validate(ValidCommand() with { EvaluationPeriodType = type, CustomPeriodCount = 4 })
        .IsValid.Should().BeTrue();

[Fact]
public void Unknown_Period_Type_Is_Rejected()
    => _validator.Validate(ValidCommand() with { EvaluationPeriodType = "Quarterly" })
        .IsValid.Should().BeFalse();

[Theory]
[InlineData(1)]
[InlineData(7)]
public void Custom_Count_Outside_2_To_6_Is_Rejected_Only_For_Custom(int count)
{
    _validator.Validate(ValidCommand() with { EvaluationPeriodType = "Custom", CustomPeriodCount = count })
        .IsValid.Should().BeFalse();

    // Hors Personnalisé le nombre est ignoré : un ancien navigateur qui renvoie 3 ne doit pas être refusé.
    _validator.Validate(ValidCommand() with { EvaluationPeriodType = "Semester", CustomPeriodCount = count })
        .IsValid.Should().BeTrue();
}
```

`ValidCommand()` : reprendre le helper du fichier existant (ou en créer un qui construit une `UpdateSchoolSettingsCommand` valide avec les valeurs par défaut de `SchoolSettingsDefaults`).

- [ ] **Step 2 : constater l'échec** — `dotnet test tests/SamaEcole.UnitTests --filter "FullyQualifiedName~UpdateSchoolSettingsCommandValidatorTests"` → erreur de compilation.

- [ ] **Step 3 : entité et défauts**

Dans `SchoolSettings` (après `GradeEditWindowDays`) :

```csharp
    /// <summary>
    /// Découpage de l'année en périodes d'évaluation. N'agit que sur les années CRÉÉES ensuite ou
    /// explicitement rejouées (ApplyEvaluationPeriodsCommand) : jamais sur une année déjà notée.
    /// </summary>
    public EvaluationPeriodType EvaluationPeriodType { get; set; } = SchoolSettingsDefaults.EvaluationPeriodType;

    /// <summary>Nombre de périodes quand <see cref="EvaluationPeriodType"/> vaut Custom (2 à 6) ; ignoré sinon.</summary>
    public int CustomPeriodCount { get; set; } = SchoolSettingsDefaults.CustomPeriodCount;
```

Dans `SchoolSettingsDefaults` :

```csharp
    /// <summary>Trimestriel : le système sénégalais standard, et le comportement de toutes les écoles existantes.</summary>
    public const EvaluationPeriodType EvaluationPeriodType = Enums.EvaluationPeriodType.Trimester;

    public const int CustomPeriodCount = 3;
```

Configuration (après `SchoolType`) :

```csharp
        builder.Property(s => s.EvaluationPeriodType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(SchoolSettingsDefaults.EvaluationPeriodType);

        builder.Property(s => s.CustomPeriodCount)
            .IsRequired()
            .HasDefaultValue(SchoolSettingsDefaults.CustomPeriodCount);
```

- [ ] **Step 4 : plomberie API**

Ajouter en **dernier paramètre** (après `GradeEditWindowDays`) de `SchoolSettingsDto`, `UpdateSchoolSettingsCommand` et `UpdateSettingsRequest` :
`, string EvaluationPeriodType = "Trimester", int CustomPeriodCount = 3`.
Dans `SchoolSettingsController`, passer `request.EvaluationPeriodType, request.CustomPeriodCount` à la commande.
Dans `UpdateSchoolSettingsCommandHandler`, après `GradeEditWindowDays` :

```csharp
        // Valeur inconnue : refusée en amont par le validateur ; le parse ne sert donc que de conversion.
        settings.EvaluationPeriodType = Enum.Parse<EvaluationPeriodType>(request.EvaluationPeriodType, ignoreCase: true);
        settings.CustomPeriodCount = request.CustomPeriodCount;
```

… et `settings.EvaluationPeriodType.ToString(), settings.CustomPeriodCount` en fin du `new SchoolSettingsDto(...)`. Même ajout dans `GetSchoolSettingsQuery.Map` (`settings.EvaluationPeriodType.ToString(), settings.CustomPeriodCount`) et `Defaults()` (`SchoolSettingsDefaults.EvaluationPeriodType.ToString(), SchoolSettingsDefaults.CustomPeriodCount`).
Dans le validateur :

```csharp
        RuleFor(c => c.EvaluationPeriodType)
            .Must(value => Enum.TryParse<EvaluationPeriodType>(value, ignoreCase: true, out _))
            .WithMessage("Découpage de l'année inconnu : « Trimester », « Semester » ou « Custom ».");

        // Le nombre de périodes n'a de sens que pour « Custom » : ailleurs il est ignoré, pas contrôlé.
        RuleFor(c => c.CustomPeriodCount)
            .InclusiveBetween(PeriodSchedule.MinCustomCount, PeriodSchedule.MaxCustomCount)
            .When(c => string.Equals(c.EvaluationPeriodType, nameof(EvaluationPeriodType.Custom), StringComparison.OrdinalIgnoreCase))
            .WithMessage($"Le nombre de périodes doit être compris entre {PeriodSchedule.MinCustomCount} et {PeriodSchedule.MaxCustomCount}.");
```

(usings : `SamaEcole.Domain.Enums`, `SamaEcole.Application.SchoolYears`.)

- [ ] **Step 5 : migration**

Run : `dotnet ef migrations add AddEvaluationPeriodType -p src/SamaEcole.Persistence -s src/SamaEcole.Web`
Vérifier que la migration générée ne contient **que** deux `AddColumn` (`EvaluationPeriodType` `character varying(20)` défaut `'Trimester'`, `CustomPeriodCount` `integer` défaut `3`) et aucun bruit de snapshot. Ne **pas** l'appliquer en production à la main : le job de migration s'en charge (voir `docs/Volume_9_Deployment_Operations.md`).

- [ ] **Step 6 : constater le succès**

Run : `dotnet build` puis `dotnet test tests/SamaEcole.UnitTests --filter "FullyQualifiedName~UpdateSchoolSettingsCommandValidatorTests"` → PASS.

- [ ] **Step 7 : commit**

```bash
git add src tests
git commit -m "feat(periods): réglage EvaluationPeriodType et CustomPeriodCount dans SchoolSettings"
```

---

### Task 3 : Création et recalage d'année via `PeriodSchedule`

**Files:**
- Modify: `src/SamaEcole.Application/SchoolYears/Commands/CreateSchoolYear/CreateSchoolYearCommandHandler.cs` (l.64-73 et `BuildTerms` l.94-103)
- Modify: `src/SamaEcole.Application/SchoolYears/Commands/UpdateSchoolYear/UpdateSchoolYearCommandHandler.cs` (`RescheduleTermsAsync`, l.91-110)
- Delete: `src/SamaEcole.Application/SchoolYears/TermSchedule.cs` (+ retirer le `public` temporaire de la Tâche 1 ; retirer le test « identique à TermSchedule » de `PeriodScheduleTests`, devenu sans objet)
- Test: `tests/SamaEcole.IntegrationTests/SchoolYears/SchoolYearPeriodsTests.cs` (créer)

**Interfaces:**
- Consumes : `PeriodSchedule.Split/SplitDates`, `SchoolSettings.EvaluationPeriodType/CustomPeriodCount`.
- Produces : une année créée porte N `Term` selon le réglage courant de l'école ; une année modifiée recale N = nombre de `Term` **existants**, jamais N d'après le réglage (l'année garde son découpage).

- [ ] **Step 1 : test d'intégration qui échoue.** S'appuyer sur le montage des tests de handlers existants du dossier (`tests/SamaEcole.IntegrationTests/Common`, comme `Grades/`) : école avec `SchoolSettings { EvaluationPeriodType = Semester }`, exécuter `CreateSchoolYearCommandHandler`, puis vérifier :

```csharp
terms.Select(t => t.Label).Should().Equal("1er semestre", "2e semestre");
terms.Select(t => t.Order).Should().Equal(1, 2);
```

Et un second cas : école **sans** ligne `SchoolSettings` (école antérieure à JGK-B02) → 3 trimestres. Et un troisième : année créée en Semestriel, puis `UpdateSchoolYearCommandHandler` avec des dates prolongées après avoir basculé le réglage en Trimestriel → l'année garde **2** périodes aux dates recalées (jamais de troisième période fabriquée).

- [ ] **Step 2 : constater l'échec** — `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~SchoolYearPeriodsTests"`.

- [ ] **Step 3 : implémenter.** Dans `CreateSchoolYearCommandHandler`, avant la boucle des trimestres :

```csharp
        // Réglage de l'école ; une école sans ligne (antérieure à JGK-B02) garde les défauts.
        var settings = await dbContext.SchoolSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var periodType = settings?.EvaluationPeriodType ?? SchoolSettingsDefaults.EvaluationPeriodType;
        var customCount = settings?.CustomPeriodCount ?? SchoolSettingsDefaults.CustomPeriodCount;
```

et remplacer le commentaire « Trois trimestres… » par « Périodes d'évaluation générées selon SchoolSettings.EvaluationPeriodType », l'appel par `BuildTerms(schoolYear.Id, schoolId, request.StartDate, request.EndDate, periodType, customCount)` et :

```csharp
    private static IEnumerable<Term> BuildTerms(
        Guid schoolYearId, Guid schoolId, DateOnly start, DateOnly end,
        EvaluationPeriodType periodType, int customCount) =>
        PeriodSchedule.Split(start, end, periodType, customCount).Select((t, index) => new Term
        {
            SchoolId = schoolId,
            SchoolYearId = schoolYearId,
            Label = t.Label,
            Order = index + 1,
            StartDate = t.Start,
            EndDate = t.End
        });
```

Dans `RescheduleTermsAsync`, remplacer `TermSchedule.Split(...)` par :

```csharp
        // Le NOMBRE de périodes est celui de l'année (ses Term existants), pas celui du réglage
        // actuel : une année créée en Semestriel reste à deux périodes même si l'école est repassée
        // en Trimestriel. Les libellés ne bougent pas — seules les bornes se recalent.
        var ordered = terms.OrderBy(t => t.Order).ToList();
        if (ordered.Count == 0) return;

        var schedule = PeriodSchedule.SplitDates(request.StartDate, request.EndDate, ordered.Count);
        for (var index = 0; index < ordered.Count; index++)
        {
            ordered[index].StartDate = schedule[index].Start;
            ordered[index].EndDate = schedule[index].End;
        }
```

(le `terms.Find(t => t.Order == index + 1)` disparaît : on itère sur les périodes réelles, ce qui reste correct si un rang manque). Mettre à jour le commentaire de `RescheduleTermsAsync` (« Order 1, 2, 3 » → « rangs »).

- [ ] **Step 4 : constater le succès** — mêmes commandes ; **et** `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~SchoolYear"` (non-régression des tests d'année existants) → PASS.

- [ ] **Step 5 : commit**

```bash
git add -A src/SamaEcole.Application/SchoolYears tests
git commit -m "feat(periods): la création d'année génère les périodes du réglage, le recalage suit l'année"
```

---

### Task 4 : Rejouer le découpage sur une année existante

Sans cette tâche, une école qui passe en Semestriel en cours d'année ne verrait jamais S1/S2 avant l'année suivante. Action **explicite**, refusée dès qu'une donnée dépend des périodes (arbitrage D3).

**Files:**
- Create: `src/SamaEcole.Application/SchoolYears/Commands/ApplyEvaluationPeriods/ApplyEvaluationPeriodsCommand.cs`
- Create: `src/SamaEcole.Application/SchoolYears/Commands/ApplyEvaluationPeriods/ApplyEvaluationPeriodsCommandHandler.cs`
- Modify: `src/SamaEcole.Web/Controllers/SchoolYearsController.cs` (nouvelle route)
- Test: `tests/SamaEcole.IntegrationTests/SchoolYears/ApplyEvaluationPeriodsTests.cs`

**Interfaces:**
- Consumes : `PeriodSchedule.Split`, `SchoolSettings.EvaluationPeriodType/CustomPeriodCount`.
- Produces : `record ApplyEvaluationPeriodsCommand(Guid SchoolYearId) : IRequest<IReadOnlyList<TermDto>>, IAuditableRequest` ; route `POST /api/v1/school-years/{id}/apply-evaluation-periods` (Directeur) → 200 `TermDto[]` ; 404 année inconnue ; 422 année terminée ou périodes déjà utilisées.

- [ ] **Step 1 : tests d'intégration qui échouent.** Quatre cas :
  1. année sans donnée, réglage Semestriel → l'année expose 2 `Term` non supprimés (« 1er semestre », « 2e semestre »), les 3 anciens sont soft-deleted, `Order` 1-2 sans violer l'index unique ;
  2. année avec **une note** sur un de ses trimestres → `ValidationException` (422), rien ne change ;
  3. année avec **une `ReportCardRemark`** seule → même refus ;
  4. année terminée (`EndDate < aujourd'hui`) → 422 ; année d'une autre école → 404 (Global Query Filter).
  Plus un cas d'idempotence : réglage identique au découpage actuel → aucune ligne créée ni supprimée.

- [ ] **Step 2 : constater l'échec** (compilation).

- [ ] **Step 3 : implémenter**

```csharp
// ApplyEvaluationPeriodsCommand.cs
using MediatR;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.SchoolYears.Queries.GetTerms;

namespace SamaEcole.Application.SchoolYears.Commands.ApplyEvaluationPeriods;

/// <summary>
/// Rejoue le découpage du réglage courant (SchoolSettings.EvaluationPeriodType) sur UNE année
/// scolaire. Refusé dès qu'une note ou une appréciation de bulletin pointe l'une de ses périodes :
/// elles perdraient leur période. Voir arbitrage D3 du plan 2026-09-24-evaluation-periods.
/// </summary>
public record ApplyEvaluationPeriodsCommand(Guid SchoolYearId)
    : IRequest<IReadOnlyList<TermDto>>, IAuditableRequest;
```

Handler (mêmes injections que `DeleteSchoolYearCommandHandler` : `IApplicationDbContext`, `ITenantProvider`, `ICurrentUserService`, `TimeProvider`) :

```csharp
public async Task<IReadOnlyList<TermDto>> Handle(ApplyEvaluationPeriodsCommand request, CancellationToken ct)
{
    _ = tenantProvider.CurrentSchoolId
        ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");
    var actorId = currentUser.UserId
        ?? throw new UnauthorizedAccessException("Aucun utilisateur associé à la requête.");
    var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    var year = await dbContext.SchoolYears.FirstOrDefaultAsync(y => y.Id == request.SchoolYearId, ct)
        ?? throw new KeyNotFoundException($"Année scolaire {request.SchoolYearId} introuvable.");

    if (year.IsClosedOn(today))
    {
        throw new ValidationException([new ValidationFailure(nameof(request.SchoolYearId),
            $"L'année scolaire « {year.Label} » est terminée : les années passées sont en lecture seule.")]);
    }

    var current = await dbContext.Terms.Where(t => t.SchoolYearId == year.Id).OrderBy(t => t.Order).ToListAsync(ct);
    var currentIds = current.Select(t => t.Id).ToList();

    var hasGrades = await dbContext.Grades.AnyAsync(g => currentIds.Contains(g.TermId), ct);
    var hasRemarks = await dbContext.ReportCardRemarks.AnyAsync(r => currentIds.Contains(r.TermId), ct);
    if (hasGrades || hasRemarks)
    {
        throw new ValidationException([new ValidationFailure(nameof(request.SchoolYearId),
            $"Des notes ou des appréciations de bulletin sont déjà saisies sur l'année « {year.Label} » : " +
            "son découpage ne peut plus changer. Le nouveau découpage s'appliquera à la prochaine année créée.")]);
    }

    var settings = await dbContext.SchoolSettings.AsNoTracking().FirstOrDefaultAsync(ct);
    var wanted = PeriodSchedule.Split(
        year.StartDate, year.EndDate,
        settings?.EvaluationPeriodType ?? SchoolSettingsDefaults.EvaluationPeriodType,
        settings?.CustomPeriodCount ?? SchoolSettingsDefaults.CustomPeriodCount);

    var unchanged = current.Count == wanted.Count
        && current.Zip(wanted, (t, w) => t.Label == w.Label && t.StartDate == w.Start && t.EndDate == w.End).All(x => x);

    if (!unchanged)
    {
        foreach (var term in current) term.SoftDelete(actorId.ToString());

        for (var i = 0; i < wanted.Count; i++)
        {
            dbContext.Terms.Add(new Term
            {
                SchoolId = year.SchoolId, SchoolYearId = year.Id, Label = wanted[i].Label,
                Order = i + 1, StartDate = wanted[i].Start, EndDate = wanted[i].End
            });
        }

        await dbContext.SaveChangesAsync(ct);
    }

    return await dbContext.Terms.AsNoTracking().Where(t => t.SchoolYearId == year.Id).OrderBy(t => t.Order)
        .Select(t => new TermDto(t.Id, t.Label, t.Order, t.StartDate, t.EndDate)).ToListAsync(ct);
}
```

(usings identiques à `DeleteSchoolYearCommandHandler` : `FluentValidation.Results`, `Microsoft.EntityFrameworkCore`, `SamaEcole.Domain.Entities`.) L'index unique `(SchoolId, SchoolYearId, Order, IsDeleted)` autorise les nouvelles lignes puisque les anciennes ont `IsDeleted = true`.

Contrôleur :

```csharp
    /// <summary>
    /// Rejoue sur cette année le découpage choisi dans Paramètres (Trimestriel / Semestriel /
    /// Personnalisé). Réservé au Directeur ; 422 si des notes ou appréciations existent déjà.
    /// </summary>
    [HttpPost("{id:guid}/apply-evaluation-periods")]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType<IReadOnlyList<TermDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ApplyEvaluationPeriods(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new ApplyEvaluationPeriodsCommand(id), cancellationToken));
```

- [ ] **Step 4 : constater le succès** — `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~ApplyEvaluationPeriodsTests"` → PASS. Ajouter un test `Category=MultiTenant` si le montage RLS de la Tâche 3 s'y prête (année d'un autre tenant introuvable).

- [ ] **Step 5 : commit**

```bash
git add src tests
git commit -m "feat(periods): action « appliquer le découpage » sur une année sans note"
```

---

### Task 5 : Interface — réglage, action, sélecteurs, vocabulaire

Les sélecteurs (`grades.js`, fiche élève) lisent déjà `Term.Label` : ils deviennent dynamiques sans autre code. Reste : le réglage, l'action, et le mot « Trimestre » écrit en dur.

**Files:**
- Modify: `src/SamaEcole.Web/wwwroot/js/settings.js` (état `config`, `load()`, `saveConfig()`)
- Modify: `src/SamaEcole.Web/Views/Settings/Index.cshtml` (spoke « Pédagogie », près du réglage du délai de correction, l.~800)
- Modify: `src/SamaEcole.Web/wwwroot/js/school-years.js` + `Views/Shared/_SchoolYearsPanel.cshtml` (bouton par année + l.189)
- Modify (vocabulaire) : `Views/Grades/Index.cshtml` (l.15, 40-43, 66, 101-102, 120, 243), `Views/Students/Index.cshtml` (l.539, 815, 854), `wwwroot/js/setup-assistant.js` l.53, `wwwroot/js/help.js` (25 occurrences), `Content/MarketingCatalog.cs` l.111/204/553
- Test: `src/SamaEcole.Web/tests/js/settings-save-preserves-fields.test.mjs` (compléter), `tests/js/school-years-apply-periods.test.mjs` (créer)

**Interfaces:**
- Consumes : `GET/PUT /schools/current/settings` (`evaluationPeriodType`, `customPeriodCount`), `POST /school-years/{id}/apply-evaluation-periods`.
- Produces : `config.evaluationPeriodType`, `config.customPeriodCount` ; `schoolYearsView.applyPeriods(year)`.

- [ ] **Step 1 : test JS qui échoue** — dans `settings-save-preserves-fields.test.mjs`, ajouter `evaluationPeriodType: 'Semester', customPeriodCount: 3` à `SERVER_SETTINGS` et le test :

```js
test('le découpage de l\'année choisi à l\'écran est envoyé, et celui du serveur est conservé sinon', async () => {
    const { view, puts } = await settingsView(SERVER_SETTINGS);

    assert.equal(view.config.evaluationPeriodType, 'Semester', 'chargé depuis le serveur');

    view.config.evaluationPeriodType = 'Custom';
    view.config.customPeriodCount = 4;
    await view.saveConfig();

    assert.equal(puts[0].evaluationPeriodType, 'Custom');
    assert.equal(puts[0].customPeriodCount, 4);
});
```

Run : `node --test tests/js/settings-save-preserves-fields.test.mjs` (depuis `src/SamaEcole.Web`) → FAIL.

- [ ] **Step 2 : `settings.js`.** Ajouter `evaluationPeriodType: 'Trimester', customPeriodCount: 3` à l'état `config`, aux trois blocs (`load()`, corps du PUT — `Number(this.config.customPeriodCount)` —, réaffectation après PUT). Run `node --test tests/js/*.test.mjs` → PASS.

- [ ] **Step 3 : réglage dans la vue.** Dans le spoke « Pédagogie » (à côté du bloc « délai de correction »), un `<select>` lié à `config.evaluationPeriodType` (options Trimestriel / Semestriel / Personnalisé), un champ numérique 2-6 lié à `config.customPeriodCount` visible seulement si `config.evaluationPeriodType === 'Custom'`, et la note :
  « Le découpage s'applique aux années scolaires créées ensuite. Pour l'année en cours, utilisez « Appliquer le découpage » dans Années scolaires — possible tant qu'aucune note n'est saisie. »
  Champs en lecture seule pour les non-Directeurs (même mécanique que les champs voisins), erreurs via `configErrors.evaluationPeriodType` / `configErrors.customPeriodCount`.

- [ ] **Step 4 : action par année.** Test JS d'abord : `applyPeriods(year)` appelle `window.api.post('/school-years/<id>/apply-evaluation-periods', {})`, recharge la liste en cas de succès, et place le message serveur (422) dans l'erreur affichée au lieu de le masquer. Puis implémenter dans `school-years.js` et ajouter, dans `_SchoolYearsPanel.cshtml`, un bouton « Appliquer le découpage » sur les années non terminées (même `canEdit` que « Modifier »). Réutiliser `ConfirmDialog` (`confirm-dialog` tag helper, voir `docs/…` conventions modales) : « Les périodes de cette année seront remplacées par le découpage <libellé du réglage>. Aucune note ni appréciation ne doit y être saisie. »

- [ ] **Step 5 : balayage du vocabulaire.** Remplacer par **« Période »** les libellés *génériques* (sélecteur « Trimestre » de `Grades/Index.cshtml`, placeholder « Sélectionnez une période… », `title="PV de délibération … pour cette période"`, `Students/Index.cshtml` « regroupées par période », « imprimées sur le bulletin »), **sans** toucher aux libellés de données (`term.termLabel`, `t.label`) ni aux commentaires. Le placeholder d'exemple `« Bon trimestre, poursuivez vos efforts. »` devient `« Bonne période, poursuivez vos efforts. »`. Dans `help.js`, remplacer les phrases qui énoncent « trois trimestres » par « les périodes de l'année (trimestres par défaut ; semestres ou périodes personnalisées selon Paramètres › Pédagogie) ». Vérifier au préalable avec :
  `grep -n -i "trimestre" src/SamaEcole.Web/Views/Grades/Index.cshtml src/SamaEcole.Web/Views/Students/Index.cshtml src/SamaEcole.Web/wwwroot/js/help.js src/SamaEcole.Web/wwwroot/js/setup-assistant.js`
  Si `help.test.mjs` / `help-render.test.mjs` échouent sur un texte modifié, mettre à jour l'attendu (le texte d'aide **est** le contrat) — pas l'inverse.

- [ ] **Step 6 : constater** — `node --test tests/js/*.test.mjs` → tout vert.

- [ ] **Step 7 : commit**

```bash
git add src/SamaEcole.Web
git commit -m "feat(periods): réglage du découpage, action par année et vocabulaire « période » dans l'interface"
```

---

### Task 6 : Documents PDF (QuestPDF)

**Files:**
- Modify: `src/SamaEcole.Infrastructure/Documents/GradeSheetDocument.cs:88`
- Create: `src/SamaEcole.Infrastructure/Documents/BulletinTitle.cs`
- Modify: `src/SamaEcole.Infrastructure/Documents/ReportCardDocument.cs:288`
- Modify: messages d'erreur `$"Trimestre {…} introuvable dans votre établissement."` (8 occurrences au 24/09/2026 — `grep -rn "Trimestre {" src/SamaEcole.Application`) → `$"Période {…} introuvable dans votre établissement."` ; `SendReportCardCommandHandler.cs:52` `?? "ce trimestre"` → `?? "cette période"`
- Modify: `docs/design-references/README.md` §2 point 2 (voir D2)
- Test: `tests/SamaEcole.UnitTests/ReportCards/BulletinTitleTests.cs` (créer), `tests/SamaEcole.UnitTests/ReportCards/ReportCardDocumentTests.cs` et `Grades/GradeSheetPdfTests.cs` (compléter)

**Interfaces:**
- Consumes : `ReportCardDto.TermLabel` (déjà rempli depuis `Term.Label`, donc « 1er semestre » pour une école semestrielle).
- Produces : `internal static string BulletinTitle.For(string? termLabel)`.

- [ ] **Step 1 : tests qui échouent**

```csharp
// tests/SamaEcole.UnitTests/ReportCards/BulletinTitleTests.cs
using FluentAssertions;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.ReportCards;

public class BulletinTitleTests
{
    [Theory]
    [InlineData("1er semestre", "BULLETIN DU 1ER SEMESTRE")]
    [InlineData("2e trimestre", "BULLETIN DU 2E TRIMESTRE")]
    [InlineData("1re période", "BULLETIN DE LA 1RE PÉRIODE")]
    [InlineData("4e période", "BULLETIN DE LA 4E PÉRIODE")]
    public void Title_Follows_The_Period_Label(string label, string expected)
        => BulletinTitle.For(label).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_Label_Falls_Back_To_The_Historical_Title(string? label)
        => BulletinTitle.For(label).Should().Be("BULLETIN DE NOTES");
}
```

Ajouter dans `GradeSheetPdfTests` un cas dont `TermLabel = "1er semestre"` : le texte extrait du PDF contient « Période » et « 1er semestre », et **pas** « Trimestre ». Reprendre la méthode d'extraction de texte déjà utilisée par ce fichier.

- [ ] **Step 2 : constater l'échec** — `dotnet test tests/SamaEcole.UnitTests --filter "FullyQualifiedName~BulletinTitleTests|FullyQualifiedName~GradeSheetPdfTests"`.

- [ ] **Step 3 : implémenter**

```csharp
// src/SamaEcole.Infrastructure/Documents/BulletinTitle.cs
namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Titre du bulletin, déduit du libellé de la période (« 1er semestre » → « BULLETIN DU 1ER SEMESTRE »).
/// « période » est féminin : « BULLETIN DE LA 1RE PÉRIODE ». Sans libellé, le titre historique.
/// </summary>
internal static class BulletinTitle
{
    public const string Fallback = "BULLETIN DE NOTES";

    public static string For(string? termLabel)
    {
        if (string.IsNullOrWhiteSpace(termLabel)) return Fallback;

        var upper = termLabel.Trim().ToUpperInvariant();
        return upper.Contains("PÉRIODE") ? $"BULLETIN DE LA {upper}" : $"BULLETIN DU {upper}";
    }
}
```

`ReportCardDocument.cs:288` : `.Text("BULLETIN DE NOTES")` → `.Text(BulletinTitle.For(reportCard.TermLabel))` (et `IsBilingualArabic` : le titre arabe reste `BulletinArabicLabels.BulletinTitle`, inchangé). `GradeSheetDocument.cs:88` : `InfoRow(right, "Trimestre", …)` → `InfoRow(right, "Période", sheet.TermLabel)`. Rechercher les autres documents qui imprimeraient le titre : `grep -rn "BULLETIN DE NOTES" src tests --include=*.cs` (au 24/09/2026 : uniquement `ReportCardDocument.cs:288`) — le PV de délibération (`ClassDeliberationDocument.cs:61`), le livret de compétences et le bulletin fusionné (`ClassBulletinsDocument`, qui réutilise `ReportCardDocument`) suivent sans changement puisqu'ils impriment `TermLabel`.

- [ ] **Step 4 : vérifier la mise en page.** Générer un bulletin A5 de test avec les trois libellés (« 1er semestre », « 3e trimestre », « 4e période ») : le titre doit tenir sur une ligne à 12 pt (A5 portrait) et ne jamais chevaucher les doubles filets. Si `ReportCardDocumentTests` contient un test de hauteur/pagination (bulletin sur **une** page), il doit rester vert.

- [ ] **Step 5 : `docs/design-references/README.md` §2 point 2** — remplacer `Titre centré, gras, souligné : **"BULLETIN DE NOTES"**.` par : `Titre centré, gras : **"BULLETIN DU {période}"** (ex. « BULLETIN DU 1ER SEMESTRE », « BULLETIN DU 2E TRIMESTRE », « BULLETIN DE LA 1RE PÉRIODE »), le libellé suivant le découpage choisi par l'école ; « BULLETIN DE NOTES » uniquement si la période n'a pas de libellé.` (ne toucher à aucune autre ligne du README.)

- [ ] **Step 6 : constater le succès** — `dotnet test tests/SamaEcole.UnitTests --filter "FullyQualifiedName~ReportCards|FullyQualifiedName~GradeSheetPdfTests"` → PASS.

- [ ] **Step 7 : commit**

```bash
git add src tests docs/design-references/README.md
git commit -m "feat(periods): titre du bulletin et relevé de saisie suivent le libellé de la période"
```

---

### Task 7 : Documentation et contexte actif

**Files:**
- Modify: `openapi.yaml` (schéma `SchoolSettings` : `evaluationPeriodType` enum `[Trimester, Semester, Custom]`, `customPeriodCount` 2-6 ; nouvelle route `POST /school-years/{id}/apply-evaluation-periods` avec 200/404/422)
- Modify: `docs/Volume_4_API_Design.md` (section années scolaires ; section réglages)
- Modify: `docs/Volume_1_Cahier_des_Charges.md` §8 (« trois trimestres » → découpage paramétrable) et `docs/Volume_3_DDS.md` (colonnes de `school_settings`)
- Modify: `ACTIVE_CONTEXT.md` §2 — nouvelle sous-section « Périodes d'évaluation dynamiques (Évolution N°2) », avec les arbitrages D1-D4, la règle « le réglage n'agit que sur les années créées ou explicitement rejouées », et l'invariant « une année garde son nombre de périodes » (Tâche 3)

- [ ] **Step 1 :** rédiger les cinq mises à jour ci-dessus, chacune avec le texte final (pas de « à compléter »).
- [ ] **Step 2 :** `dotnet build` puis `node --test tests/js/*.test.mjs` (depuis `src/SamaEcole.Web`) → verts.
- [ ] **Step 3 : commit**

```bash
git add openapi.yaml docs ACTIVE_CONTEXT.md
git commit -m "docs(periods): routes, réglage et contexte actif de l'évolution périodes d'évaluation"
```

---

## Vérification finale (à lancer par le propriétaire)

- `dotnet test` (suite complète) et `dotnet test --filter Category=MultiTenant` ;
- `node --test tests/js/*.test.mjs` ;
- Recette manuelle : école neuve en **Semestriel** → créer l'année → `/notes` propose « 1er semestre / 2e semestre » ; saisir une note ; PDF bulletin « BULLETIN DU 1ER SEMESTRE », récapitulatif annuel à deux colonnes ; repasser en Trimestriel → l'année existante garde ses deux semestres, « Appliquer le découpage » est refusé (422 lisible) ; créer l'année suivante → trois trimestres.

## Self-review (spec ↔ tâches)

| Exigence | Tâche |
|---|---|
| Enum `EvaluationPeriodType` (Trimestriel défaut, Semestriel, Personnalisé) dans `SchoolSettings` | 1, 2 |
| Périodes valides seulement dans les sélecteurs (notes, moyennes, bulletins) | 3 (génération) + 5 (les sélecteurs lisent `Term.Label` ; vocabulaire) — vérifié : `grades.js:152`, `GetTermsQueryHandler`, `GetStudentDetail` |
| Moteur QuestPDF : libellés dynamiques (bulletin, relevé) | 6 |
| Rétrocompatibilité des écoles existantes | 2 (défaut colonne = `Trimester`), 1 (test d'égalité avec l'ancien découpage), 3 (l'année garde son nombre de périodes) |
| Aucun orphelinage de notes | 3 (recalage sans recréation), 4 (refus si notes/appréciations) |

Points de vigilance connus, non traités par ce plan : (1) un bulletin d'une année **semestrielle** dont le récapitulatif annuel comporte deux colonnes est déjà prévu par le gabarit (`ReportCardDocument`, `GetReportCardPdfQuery.cs:240-242`) — à vérifier visuellement à la Tâche 6 ; (2) `Term.Label` est limité à 30 caractères (`TermConfiguration`) — largement suffisant pour « 6e période ».
