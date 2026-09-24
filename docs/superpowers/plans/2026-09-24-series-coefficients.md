# Coefficients par série et surcharge du Directeur (Évolution N°4) — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **STATUT : PLAN POUR VALIDATION — aucun code n'est écrit.** Les arbitrages A1 à A11 ci-dessous doivent être validés avant la Tâche 1. **La Tâche 6 est en outre bloquée par une donnée que ce plan ne peut pas inventer** (les coefficients nationaux par série, voir A10).

**Goal :** Un lycée peut donner à une même matière un coefficient différent selon la série de la classe (L1, L2, S1, S2, Techniques), partir de modèles par défaut, et le Directeur peut surcharger ce coefficient pour une série ou pour une classe précise. Les moyennes pondérées, la fiche élève et les bulletins utilisent la valeur surchargée quand elle existe, sinon la valeur de la série, sinon celle de la matière.

**Architecture :** Une table `subject_coefficient_overrides` (portée « série » ou « classe », par année scolaire) et une colonne `Classroom.Series`. Une fonction pure `SubjectCoefficients.Resolve` (classe → série → matière) est appelée par les DEUX seuls endroits qui lisent aujourd'hui `Subject.Coefficient` pour calculer : `GetGradeSummaryQueryHandler` (source des bulletins, des bulletins de classe, de la délibération et du PDF) et `GetStudentDetailQueryHandler` (fiche élève). Les modèles nationaux ne servent jamais de repli au calcul : ils sont **matérialisés** en lignes de surcharge « série » par une action explicite (« Appliquer le modèle »), donc visibles et modifiables.

**Tech Stack :** ASP.NET Core 9, EF Core/Npgsql (RLS + Global Query Filter, `xmin`), MediatR + FluentValidation, Alpine.js, xUnit + FluentAssertions, `node --test`.

**Spec :** pas de document séparé — la spécification est le message de l'utilisateur du 24/09/2026 (« Spécification Évolution N°4 ») :
1. coefficients par série (L1, L2, S1, S2, Outils/Techniques) : coefficients différenciés par matière selon la série de l'élève au lycée ; modèles par défaut pour les séries nationales courantes ;
2. droit de surcharge du Directeur : personnaliser le coefficient d'une matière pour une classe ou une série, dans `SchoolSettings` ou le paramétrage des classes ; les moyennes et les bulletins utilisent la valeur surchargée quand elle existe, sinon la valeur par défaut de la série.

## Global Constraints

- PostgreSQL uniquement ; migration EF Core **nouvelle** (jamais modifier une migration appliquée). — `AGENTS.md`.
- Toute nouvelle table tenant : `SchoolId`, **Global Query Filter + policy RLS** (les deux), ajoutée à `TenantTables` de sa migration, `GRANT SELECT, INSERT, UPDATE` (jamais `DELETE` : suppression logique), **et** ajoutée à la fonction `reset_school_data` par une migration à part (piège déjà rencontré : voir `AddClassJournalToResetSchoolData`).
- Aucune suppression physique (`IsDeleted`, `DeletedAt`, `DeletedBy`) ; verrou optimiste `xmin` sur la nouvelle table (elle pilote les notes → 409, jamais un écrasement silencieux). — règles #5 et #6.
- CQRS MediatR ; aucune logique métier dans un contrôleur ni une entité — elle vit dans `SamaEcole.Application`. Erreurs au format normalisé (`ValidationException` → 422). — règles #7 à #9.
- `schoolId` jamais lu depuis un paramètre client ; l'**année scolaire des écritures est l'année ACTIVE résolue serveur** (même convention que `Enrollment`).
- Primaire et Maternelle : **coefficient neutralisé à 1** dans les calculs (`UsesSimplifiedGrading`) — inchangé, jamais surchargeable.
- Le PDF du bulletin ne change pas de mise en page (règle #12) : la colonne « Coefficient » existe déjà et lit `SubjectGradeDto.Coefficient`, qui devient la valeur effective.
- Bornes d'un coefficient de surcharge : **identiques à `CreateSubjectCommandValidator`** (> 0, ≤ 20 ; colonne `numeric(4,2)`).
- Le propriétaire lance lui-même la suite complète `dotnet test` (consigne du 17/09/2026) ; ce plan n'exécute que des tests **ciblés** (`--filter`). `dotnet build` et `node --test` restent libres.
- Migration en local : si `SamaEcole.Web` tourne, générer avec `--configuration Release`. **Appliquer ensuite `dotnet ef database update` avant de relancer l'app** (sinon « Une erreur inattendue » partout — constat du 24/09/2026).
- Code de production et tests de Finance/Notes/isolation : jamais l'un sans l'autre (`AGENTS.md`).
- Conventional Commits, un commit par tâche, terminé par `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`. Branche : `feature/series-coefficients`, **empilée sur `feature/working-days`** (mêmes fichiers `SchoolSettings*`/`help.js`/`ACTIVE_CONTEXT.md`, migrations à la suite).

## Constat sur ce que le code fait aujourd'hui (à connaître avant de valider)

- **Le coefficient vit sur la matière** : `Subject.Coefficient` (`numeric(4,2)`), et une matière est unique par (niveau, domaine parent, nom). Aujourd'hui, un lycée qui veut « Maths = 8 en S1, 5 en L2 » crée **une matière par niveau texte** (« Terminale S1 », « Terminale L2 »…) — c'est le contournement documenté dans `Subject.cs`. Il continue de fonctionner tel quel : sans surcharge, le calcul est **strictement celui d'avant** (rétrocompatibilité par construction).
- **La série n'existe nulle part sur la classe ni sur l'élève.** Elle ne se lit que dans le nom (« Terminale S2 A »). `ExamSession.Series` est un texte libre propre aux examens.
- **Deux — et seulement deux — lecteurs de coefficient pour calculer** : `GetGradeSummaryQueryHandler` (groupe par `s.Coefficient`, l. ~60-95) et `GetStudentDetailQueryHandler.BuildTermReportsAsync` (l. ~410-465, copie de la même logique). Bulletins PDF, bulletins de classe, délibération et `GetReportCardRemark` passent tous par `ReportCardDataService` → `GetGradeSummaryQuery` : **corriger le résumé corrige tout**. La fiche élève, elle, doit être alignée à la main, sinon elle contredirait le bulletin.
- **Aucun instantané** : moyennes et bulletins sont recalculés à la demande. Changer un coefficient réécrit donc rétroactivement les bulletins déjà imprimés d'une année — c'est déjà vrai pour `Subject.Coefficient`, mais un coefficient **de classe** rendrait le phénomène invisible. D'où A6/A7.
- **La classe de référence d'un élève** est aujourd'hui `Student.ClassroomId` (sa classe ACTUELLE), y compris pour le cycle des années passées. Pour les coefficients, on utilisera l'**inscription de l'année** (`Enrollment`) avec repli sur la classe actuelle (A11).
- `Subject.Level` est un texte libre que `ClassroomCycle.CycleFor` replie sur **Collège** quand il ne le reconnaît pas (« Terminale S2 » → Collège). On ne peut donc **pas** filtrer « les matières du lycée » par cycle : on filtre « matières hors primaire/maternelle ».

## Arbitrages à valider avant de commencer

| # | Question | Recommandation (appliquée par ce plan) |
|---|---|---|
| A1 | **Où vit la série** : sur l'élève ou sur la classe ? | **Sur la classe** (`Classroom.Series`, nullable). L'élève l'hérite de sa classe. Au lycée sénégalais on est réparti en classes par série ; les bulletins, l'appel et les affectations d'enseignants sont déjà par classe. Une série par élève casserait ces flux (un élève L2 dans une classe S1 ?) pour aucun cas réel. |
| A2 | **Catalogue de séries** : liste fermée ou texte libre ? | **Liste fermée de 5 codes** : `L1`, `L2`, `S1`, `S2`, `TECH` (« Techniques / Outils »), stockée en texte. Un texte libre casserait la correspondance avec les modèles et les surcharges (« s2 » ≠ « S2 »). L'ajout d'une série (S3, T1…) = une ligne dans `LyceeSeries` + un test. `Classroom.Series` n'est renseignable que si `Cycle = Lycee` ; **null = classe sans série** (Seconde commune, par ex.) et le calcul reste celui de la matière. |
| A3 | **Où stocker la surcharge** : `SchoolSettings` ou table dédiée ? | **Table dédiée** `subject_coefficient_overrides`. Un JSON dans `SchoolSettings` ne se protège ni par RLS fine, ni par `xmin`, ni par contrainte d'unicité, et le `PUT` des réglages est un remplacement complet (le piège de l'Évolution N°1). Ce choix respecte l'esprit de la spécification (« ou le paramétrage des classes ») : la classe porte la série, le Directeur règle les coefficients dans l'écran Matières. |
| A4 | **Précédence** | **Surcharge de classe › surcharge de série › coefficient de la matière.** Le « modèle par défaut de la série » est la surcharge de série une fois matérialisée (A5). Primaire/Maternelle : 1, inchangé. |
| A5 | **Modèles par défaut : repli au calcul ou matérialisés ?** | **Matérialisés** par « Appliquer le modèle S2 » (crée des surcharges de série pour l'année active). Un repli au calcul, par correspondance de noms (« Maths » vs « Mathématiques »), ferait changer les bulletins d'un lycée existant le jour où l'on renseigne une série, sans que personne ne l'ait demandé, et avec des noms qui ne correspondent pas. Avec la matérialisation, le Directeur voit le résultat, l'édite, et un rapport dit ce qui n'a pas été trouvé. Les lignes existantes ne sont jamais écrasées sans `overwrite=true`. |
| A6 | **Portée temporelle** | **Par année scolaire** (`SchoolYearId`). Sinon un changement de coefficient en 2027 réécrirait les bulletins de 2026. Une nouvelle année démarre **sans** surcharge : action « Reconduire l'année précédente » (Tâche 5) — jamais copie silencieuse. |
| A7 | **Effet rétroactif sur une année déjà notée** | **Autorisé, journalisé, avec avertissement à l'écran** (« Ce changement recalcule les moyennes et bulletins de cette année pour les classes concernées »). Un blocage strict comme l'Évolution N°2 interdirait au Directeur de corriger un coefficient faux en cours d'année, ce qui est précisément le besoin. Chaque écriture est auditée (`IAuditableRequest`). |
| A8 | **Périmètre par cycle** | Surcharge de **série** : classes Lycée seulement. Surcharge de **classe** : Collège et Lycée. **Refusée (422)** pour une classe de Primaire/Maternelle (coefficient neutralisé à 1 : une valeur saisie serait sans effet et trompeuse). |
| A9 | **Qui** | **Écriture : Directeur seul** (spec). **Lecture : Directeur et Secrétariat.** Aucune délégation. Module `Pedagogy` requis (`[RequireModule]`). |
| A10 | **Source des coefficients nationaux** | **À fournir par vous (bloquant pour la Tâche 6 uniquement).** Je n'invente pas de coefficients officiels : le plan livre la mécanique et un jeu de test ; les valeurs réelles par série (annexe A ci-dessous) doivent venir du texte officiel en vigueur (arrêté/circulaire sur les coefficients du BAC et des classes de Première/Terminale). Les Tâches 1 à 5 et 7 à 8 ne dépendent pas de cette donnée. |
| A11 | **Classe de référence d'un élève pour l'historique** | **Inscription de l'année** (`Enrollment`, hors `Cancelled`), à défaut `Student.ClassroomId`. Sans cela, un élève passé de Première S2 à Terminale L2 verrait les bulletins de l'an passé recalculés avec les coefficients de sa nouvelle série. Le calcul du **cycle** (barème /10 ou /20) garde son comportement actuel (hors périmètre, à signaler). |

## Annexe A — données nationales à fournir (bloquant Tâche 6)

Format attendu, une ligne par matière et par série : `série ; libellé officiel ; alias acceptés ; coefficient`. Exemple de forme (les valeurs ci-dessous sont **fictives et servent uniquement aux tests**, jamais à la production) :

```
S2 ; Mathématiques ; Maths, Mathematiques ; 5
S2 ; Français ; Francais ; 2
L2 ; Mathématiques ; Maths ; 2
```

Le fichier réel vit dans `src/SamaEcole.Application/Coefficients/SeriesCoefficientTemplates.cs`, en **une seule table de données** — corriger un coefficient national = modifier une ligne et son test, rien d'autre.

## File Structure

| Fichier | Rôle |
|---|---|
| `src/SamaEcole.Application/Coefficients/LyceeSeries.cs` (créer) | Catalogue fermé des séries (code, libellé), `IsValid`, `Parse`. |
| `src/SamaEcole.Application/Coefficients/SubjectCoefficients.cs` (créer) | Fonction pure `Resolve` + `CoefficientOverrides` (recherche par matière). |
| `src/SamaEcole.Application/Coefficients/CoefficientOverrideLoader.cs` (créer) | Service scoped : charge les surcharges d'un (élève, année), mémoïsé. Enregistré dans `DependencyInjection.cs`. |
| `src/SamaEcole.Application/Coefficients/SeriesCoefficientTemplates.cs` (créer, Tâche 6) | Table de données des modèles nationaux + correspondance de noms. |
| `src/SamaEcole.Application/Coefficients/{Commands,Queries}/…` (créer) | `UpsertCoefficientOverride`, `DeleteCoefficientOverride`, `ApplySeriesTemplate`, `CarryOverCoefficients`, `GetCoefficientGrid`, `GetSeriesCatalog`. |
| `src/SamaEcole.Domain/Entities/SubjectCoefficientOverride.cs` (créer) | Entité de surcharge. |
| `src/SamaEcole.Domain/Entities/Classroom.cs` (modifier) | `Series`. |
| `src/SamaEcole.Persistence/Configurations/{SubjectCoefficientOverride,Classroom}Configuration.cs`, `Migrations/*` | Table, colonne, RLS, reset. |
| `GetGradeSummaryQueryHandler.cs`, `GetStudentDetailQuery.cs` (modifier) | Utilisent la valeur effective. |
| `Classrooms/Commands/{Create,Update}Classroom/*` (modifier) | Série de la classe. |
| `src/SamaEcole.Web/Controllers/CoefficientsController.cs` (créer) | Routes fines. |
| `wwwroot/js/{coefficients.js (créer), classrooms.js, subjects.js, help.js}`, `Views/Subjects/*`, `Views/Classrooms/*` (modifier) | Interface. |
| `tests/SamaEcole.UnitTests/Coefficients/*`, `tests/SamaEcole.IntegrationTests/Coefficients/*`, `src/SamaEcole.Web/tests/js/coefficients.test.mjs` | Tests. |

---

### Task 1 : `LyceeSeries` et `SubjectCoefficients.Resolve` (purs, sans base)

**Files:**
- Create: `src/SamaEcole.Application/Coefficients/LyceeSeries.cs`, `src/SamaEcole.Application/Coefficients/SubjectCoefficients.cs`
- Test: `tests/SamaEcole.UnitTests/Coefficients/SubjectCoefficientsTests.cs`, `…/LyceeSeriesTests.cs`

**Interfaces:**
- Produces (namespace `SamaEcole.Application.Coefficients`) :
  - `static class LyceeSeries` : `IReadOnlyList<(string Code, string Label)> All` ; `bool IsValid(string? code)` ; `string? Normalize(string? code)` (trim + majuscules, `null` si vide) ; `string LabelOf(string code)`
  - `static class SubjectCoefficients` : `decimal Resolve(decimal baseCoefficient, decimal? classroomOverride, decimal? seriesOverride)`
  - `sealed record CoefficientOverrides(IReadOnlyDictionary<Guid, decimal> ByClassroom, IReadOnlyDictionary<Guid, decimal> BySeries)` avec `decimal Effective(Guid subjectId, decimal baseCoefficient)`, `static CoefficientOverrides None`

- [ ] **Step 1 : écrire les tests qui échouent**

```csharp
// tests/SamaEcole.UnitTests/Coefficients/SubjectCoefficientsTests.cs
using FluentAssertions;
using SamaEcole.Application.Coefficients;
using Xunit;

namespace SamaEcole.UnitTests.Coefficients;

public class SubjectCoefficientsTests
{
    [Theory]
    [InlineData(4, null, null, 4)]      // rien : la matière, comportement historique
    [InlineData(4, null, 6, 6)]         // série
    [InlineData(4, 8, null, 8)]         // classe
    [InlineData(4, 8, 6, 8)]            // la classe l'emporte sur la série
    public void Precedence_Is_Classroom_Then_Series_Then_Subject(
        double baseValue, double? classroom, double? series, double expected)
        => SubjectCoefficients.Resolve((decimal)baseValue, (decimal?)classroom, (decimal?)series)
            .Should().Be((decimal)expected);

    [Fact]
    public void An_Override_Of_Another_Subject_Never_Applies()
    {
        var mathsId = Guid.NewGuid();
        var overrides = new CoefficientOverrides(
            new Dictionary<Guid, decimal> { [mathsId] = 9m }, new Dictionary<Guid, decimal>());

        overrides.Effective(Guid.NewGuid(), 3m).Should().Be(3m);
        overrides.Effective(mathsId, 3m).Should().Be(9m);
    }

    [Fact]
    public void None_Changes_Nothing()
        => CoefficientOverrides.None.Effective(Guid.NewGuid(), 2.5m).Should().Be(2.5m);
}
```

```csharp
// tests/SamaEcole.UnitTests/Coefficients/LyceeSeriesTests.cs
using FluentAssertions;
using SamaEcole.Application.Coefficients;
using Xunit;

namespace SamaEcole.UnitTests.Coefficients;

public class LyceeSeriesTests
{
    [Fact]
    public void The_Catalogue_Holds_The_Five_Agreed_Series()
        => LyceeSeries.All.Select(s => s.Code).Should().Equal("L1", "L2", "S1", "S2", "TECH");

    [Theory]
    [InlineData("S2", true)]
    [InlineData("s2", true)]      // Normalize d'abord : la casse ne fait pas une autre série
    [InlineData(" tech ", true)]
    [InlineData("S3", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_Follows_The_Catalogue(string? code, bool expected)
        => LyceeSeries.IsValid(LyceeSeries.Normalize(code)).Should().Be(expected);

    [Fact]
    public void Normalize_Trims_And_Uppercases_And_Maps_Blank_To_Null()
    {
        LyceeSeries.Normalize(" s1 ").Should().Be("S1");
        LyceeSeries.Normalize("   ").Should().BeNull();
    }
}
```

- [ ] **Step 2 : constater l'échec** — `dotnet test tests/SamaEcole.UnitTests --filter "FullyQualifiedName~Coefficients"` → erreur de compilation.

- [ ] **Step 3 : implémenter**

```csharp
// src/SamaEcole.Application/Coefficients/LyceeSeries.cs
namespace SamaEcole.Application.Coefficients;

/// <summary>
/// Catalogue FERMÉ des séries de lycée (Évolution N°4, arbitrage A2). Fermé à dessein : une série
/// est la clé des surcharges et des modèles, un texte libre (« s2 », « S 2 ») les casserait. Ajouter une
/// série = une ligne ici et un test.
/// </summary>
public static class LyceeSeries
{
    public static readonly IReadOnlyList<(string Code, string Label)> All =
    [
        ("L1", "Série L1"),
        ("L2", "Série L2"),
        ("S1", "Série S1"),
        ("S2", "Série S2"),
        ("TECH", "Séries techniques (outils)")
    ];

    public static string? Normalize(string? code)
        => string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();

    public static bool IsValid(string? code) => code is not null && All.Any(s => s.Code == code);

    public static string LabelOf(string code) => All.First(s => s.Code == code).Label;
}
```

```csharp
// src/SamaEcole.Application/Coefficients/SubjectCoefficients.cs
namespace SamaEcole.Application.Coefficients;

/// <summary>
/// Coefficient EFFECTIF d'une matière (Évolution N°4, arbitrage A4) : surcharge de classe, sinon
/// surcharge de série, sinon coefficient de la matière. Pur — aucun accès base.
/// Le primaire neutralise le coefficient à 1 EN AMONT (GetGradeSummaryQueryHandler) : cette fonction
/// n'a pas à le savoir.
/// </summary>
public static class SubjectCoefficients
{
    public static decimal Resolve(decimal baseCoefficient, decimal? classroomOverride, decimal? seriesOverride)
        => classroomOverride ?? seriesOverride ?? baseCoefficient;
}

/// <summary>Surcharges applicables à UN élève pour UNE année : par matière, côté classe et côté série.</summary>
public sealed record CoefficientOverrides(
    IReadOnlyDictionary<Guid, decimal> ByClassroom,
    IReadOnlyDictionary<Guid, decimal> BySeries)
{
    public static readonly CoefficientOverrides None =
        new(new Dictionary<Guid, decimal>(), new Dictionary<Guid, decimal>());

    public decimal Effective(Guid subjectId, decimal baseCoefficient)
        => SubjectCoefficients.Resolve(
            baseCoefficient,
            ByClassroom.TryGetValue(subjectId, out var c) ? c : null,
            BySeries.TryGetValue(subjectId, out var s) ? s : null);
}
```

- [ ] **Step 4 : constater le succès** — même commande → PASS.
- [ ] **Step 5 : commit** — `feat(coefficients): catalogue des séries et résolution pure du coefficient effectif`

---

### Task 2 : La série de la classe (`Classroom.Series`)

**Files:**
- Modify: `src/SamaEcole.Domain/Entities/Classroom.cs`, `src/SamaEcole.Persistence/Configurations/ClassroomConfiguration.cs`
- Modify: `src/SamaEcole.Application/Classrooms/Commands/CreateClassroom/*`, `…/UpdateClassroom/*` (commande, validateur, handler), la requête de liste des classes et son DTO
- Modify: `src/SamaEcole.Web/Controllers/ClassroomsController.cs` (requêtes `Create`/`Update`)
- Create (généré) : migration `AddClassroomSeries`
- Test: `tests/SamaEcole.UnitTests/Classrooms/ClassroomSeriesValidatorTests.cs`

**Interfaces:**
- Consumes : `LyceeSeries.Normalize/IsValid` (Tâche 1).
- Produces : `Classroom.Series` (`string?`, `HasMaxLength(10)`, aucune valeur par défaut) ; `Series` en **dernier paramètre optionnel** (`= null`) des commandes Create/Update et du DTO de classe. Règle : `Series` non nul ⇒ `Cycle == Lycee` **et** `LyceeSeries.IsValid`, sinon 422. `Series` absent en `PUT` = **effacée** (la série est un attribut de la classe, pas un réglage verrouillant — à la différence de `WorkingDays`) ; un ancien client qui ne l'envoie pas efface une série : voir le point de vigilance en fin de plan.

- [ ] **Step 1 : tests du validateur qui échouent** (montage de `CreateClassroomCommandValidator`, cas : `Lycee + "S2"` valide ; `Lycee + "s2"` valide (normalisée) ; `Lycee + "S9"` refusée ; `College + "S2"` refusée ; `Lycee + null` valide).
- [ ] **Step 2 : constater l'échec** (compilation).
- [ ] **Step 3 : implémenter** — `Classroom.Series` (commentaire : « Série du lycée, liste fermée `LyceeSeries` ; null = classe sans série ; l'élève l'hérite de sa classe — arbitrage A1 ») ; `ClassroomConfiguration.Property(c => c.Series).HasMaxLength(10)` ; règle de validateur `RuleFor(c => c.Series).Must(...).When(c => c.Series is not null)` avec message « La série n'est possible que pour une classe de Lycée, parmi L1, L2, S1, S2, TECH. » ; handlers : `classroom.Series = LyceeSeries.Normalize(request.Series)`.
- [ ] **Step 4 : migration** — `dotnet ef migrations add AddClassroomSeries -p src/SamaEcole.Persistence -s src/SamaEcole.Web --configuration Release` ; vérifier **un seul `AddColumn`** (`Series`, `character varying(10)`, nullable) et un diff de snapshot limité à cette colonne.
- [ ] **Step 5 : constater le succès** — `dotnet build` puis `dotnet test tests/SamaEcole.UnitTests --filter "FullyQualifiedName~Classroom"` ; **appliquer la migration au dev** (`dotnet ef database update …`).
- [ ] **Step 6 : commit** — `feat(coefficients): série de la classe (Classroom.Series)`

---

### Task 3 : La table des surcharges (entité, RLS, purge)

**Files:**
- Create: `src/SamaEcole.Domain/Entities/SubjectCoefficientOverride.cs`, `src/SamaEcole.Persistence/Configurations/SubjectCoefficientOverrideConfiguration.cs`
- Modify: `IApplicationDbContext.cs` (`DbSet<SubjectCoefficientOverride> SubjectCoefficientOverrides`), `ApplicationDbContext.cs`
- Create (générés/écrits) : migration `AddSubjectCoefficientOverrides` (table + RLS + grants + CHECK + index uniques partiels), migration `AddSubjectCoefficientOverridesToResetSchoolData`
- Modify: le service de purge d'une année en mode test (`SchoolYearPurgeService`) — supprimer d'abord les surcharges de l'année
- Test: `tests/SamaEcole.IntegrationTests/Coefficients/CoefficientOverrideIsolationTests.cs`

**Interfaces:**
- Produces : `SubjectCoefficientOverride : AuditableEntity, ITenantEntity` — `SchoolId`, `SchoolYearId`, `SubjectId`, `Guid? ClassroomId`, `string? Series`, `decimal Coefficient`. Exactement **un** de `ClassroomId`/`Series` non nul (contrainte `CHECK (num_nonnulls("ClassroomId","Series") = 1)`). Index uniques partiels (`WHERE NOT "IsDeleted"`) : `(SchoolYearId, SubjectId, ClassroomId)` où `ClassroomId IS NOT NULL` ; `(SchoolYearId, SubjectId, Series)` où `Series IS NOT NULL`. `xmin` en `IsRowVersion`. Précision `numeric(4,2)`.

- [ ] **Step 1 : tests d'intégration qui échouent** (`[Trait("Category","MultiTenant")]`, SQL **brut** sous le rôle applicatif, comme `AttendanceIsolationTests`) :
  1. une école ne lit jamais les surcharges d'une autre (RLS) ;
  2. une session sans tenant ne voit aucune ligne ;
  3. écrire une ligne au `SchoolId` d'une autre école est refusé (`InsufficientPrivilege`) ;
  4. deux surcharges de **série** identiques (même année, matière, série) → `UniqueViolation` ; une surcharge supprimée logiquement laisse recréer la même ;
  5. une ligne avec **les deux** portées (ou aucune) → `CheckViolation`.
- [ ] **Step 2 : constater l'échec.**
- [ ] **Step 3 : implémenter** l'entité, la configuration (`ToTable("subject_coefficient_overrides")`, `xmin`, `HasPrecision(4,2)`, `HasMaxLength(10)` sur `Series`, FK vers `subjects`/`school_years`/`classrooms` en `Restrict`) ; la migration suit `20260919091445_AddClassJournal.cs` : `TenantTables = ["subject_coefficient_overrides"]`, `ENABLE ROW LEVEL SECURITY`, policy `subject_coefficient_overrides_tenant_isolation`, `GRANT SELECT, INSERT, UPDATE … TO sama_ecole_app` ; puis une **migration séparée** reprenant `AddClassJournalToResetSchoolData` (périmètre complet de la dernière version de la fonction + la nouvelle table, libellé « Surcharges de coefficients »).
- [ ] **Step 4 : purge d'année** — ajouter la suppression des surcharges de l'année dans `SchoolYearPurgeService` **avant** celle de l'année (FK), et un test (`SchoolYearPurgeTests`, s'il existe, sinon dans le fichier de la Tâche 3) : supprimer une année en mode test supprime ses surcharges et ne touche pas celles d'une autre année.
- [ ] **Step 5 : constater le succès** — `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~CoefficientOverrideIsolationTests"` → PASS ; appliquer les migrations au dev.
- [ ] **Step 6 : commit** — `feat(coefficients): table des surcharges de coefficient (RLS, xmin, reset, purge)`

---

### Task 4 : Les calculs utilisent le coefficient effectif

**Files:**
- Create: `src/SamaEcole.Application/Coefficients/CoefficientOverrideLoader.cs` ; enregistrement `services.AddScoped<Coefficients.CoefficientOverrideLoader>();`
- Modify: `GetGradeSummaryQueryHandler.cs`, `GetStudentDetailQuery.cs` (constructeurs : ajout de `CoefficientOverrideLoader`)
- Modify: les tests existants qui construisent ces handlers à la main (`grep -rn "new GetGradeSummaryQueryHandler\|new GetStudentDetailQueryHandler" tests`) → `new CoefficientOverrideLoader(db)`
- Test: `tests/SamaEcole.IntegrationTests/Coefficients/EffectiveCoefficientTests.cs`

**Interfaces:**
- Consumes : `SubjectCoefficients`, `CoefficientOverrides`, `SubjectCoefficientOverrides` (Tâches 1 et 3), `Classroom.Series` (Tâche 2).
- Produces : `CoefficientOverrideLoader.LoadAsync(Guid studentId, Guid schoolYearId, CancellationToken ct) : Task<CoefficientOverrides>` — résout la classe de référence (A11), charge en **une requête** les lignes de l'année dont `ClassroomId == classe` ou `Series == classe.Series` (si non nulle), mémoïse par (élève, année) pour la durée de vie du scope (les bulletins d'une classe appellent le résumé élève par élève).

- [ ] **Step 1 : tests d'intégration qui échouent** (montage : école, année active, deux classes Lycée « Terminale S2 A » (`Series = "S2"`) et « Terminale L2 A » (`Series = "L2"`), une matière « Mathématiques » (coeff 4), un élève par classe avec les mêmes notes) :
  1. **sans surcharge**, résumé identique à celui d'avant (coefficient 4) ;
  2. surcharge **série S2 = 6** : l'élève S2 pèse 6, l'élève L2 reste à 4 ; `TotalCoefficients`, `TotalPoints`, `GeneralAverage` cohérents ;
  3. surcharge **classe = 8** en plus : la classe l'emporte (8) pour cette classe uniquement ;
  4. **portée année** : une surcharge posée sur l'année N ne s'applique pas à un trimestre de l'année N+1 ;
  5. **Primaire** : une (fausse) surcharge en base sur une classe de primaire est ignorée, coefficient 1 ;
  6. **historique (A11)** : l'élève change de classe après l'année N ; le résumé d'un trimestre de l'année N utilise la classe de son **inscription** N ;
  7. **fiche élève = bulletin** : `GetStudentDetail` renvoie le même coefficient et la même moyenne générale que `GetGradeSummary` pour le même élève et le même trimestre (le test qui empêche la dérive des deux copies) ;
  8. `[Trait("Category","MultiTenant")]` : la surcharge de l'école A ne s'applique jamais à l'école B, même matière homonyme.
- [ ] **Step 2 : constater l'échec.**
- [ ] **Step 3 : implémenter le loader**

```csharp
// src/SamaEcole.Application/Coefficients/CoefficientOverrideLoader.cs
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Coefficients;

/// <summary>
/// Charge les surcharges de coefficient applicables à un élève pour une année (Évolution N°4).
/// Classe de référence : celle de l'INSCRIPTION de l'année (l'historique ne suit pas les changements
/// de classe), à défaut la classe actuelle (arbitrage A11). Mémoïsé : un bulletin de classe appelle le
/// résumé élève par élève, on ne relit pas les mêmes lignes trente fois.
/// </summary>
public class CoefficientOverrideLoader(IApplicationDbContext dbContext)
{
    private readonly Dictionary<(Guid Student, Guid Year), CoefficientOverrides> _cache = [];

    public async Task<CoefficientOverrides> LoadAsync(Guid studentId, Guid schoolYearId, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue((studentId, schoolYearId), out var cached)) return cached;

        var classroomId = await dbContext.Enrollments.AsNoTracking()
            .Where(e => e.StudentId == studentId && e.SchoolYearId == schoolYearId && e.Status != EnrollmentStatus.Cancelled)
            .Select(e => (Guid?)e.ClassroomId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? await dbContext.Students.AsNoTracking()
                .Where(s => s.Id == studentId)
                .Select(s => (Guid?)s.ClassroomId)
                .FirstOrDefaultAsync(cancellationToken);

        if (classroomId is null) return _cache[(studentId, schoolYearId)] = CoefficientOverrides.None;

        var classroom = await dbContext.Classrooms.AsNoTracking()
            .Where(c => c.Id == classroomId)
            .Select(c => new { c.Id, c.Series })
            .FirstOrDefaultAsync(cancellationToken);
        if (classroom is null) return _cache[(studentId, schoolYearId)] = CoefficientOverrides.None;

        var rows = await dbContext.SubjectCoefficientOverrides.AsNoTracking()
            .Where(o => o.SchoolYearId == schoolYearId
                        && (o.ClassroomId == classroom.Id || (classroom.Series != null && o.Series == classroom.Series)))
            .Select(o => new { o.SubjectId, o.ClassroomId, o.Coefficient })
            .ToListAsync(cancellationToken);

        var result = new CoefficientOverrides(
            rows.Where(r => r.ClassroomId != null).ToDictionary(r => r.SubjectId, r => r.Coefficient),
            rows.Where(r => r.ClassroomId == null).ToDictionary(r => r.SubjectId, r => r.Coefficient));

        return _cache[(studentId, schoolYearId)] = result;
    }
}
```

  Puis, dans `GetGradeSummaryQueryHandler` : lire `SchoolYearId` du trimestre (le `Terms.AnyAsync` devient une projection), `var overrides = await loader.LoadAsync(request.StudentId, yearId, ct);`, et remplacer `var coefficient = isPrimaire ? 1m : g.Key.Coefficient;` par `var coefficient = isPrimaire ? 1m : overrides.Effective(g.Key.SubjectId, g.Key.Coefficient);`. **Même remplacement** dans `GetStudentDetailQueryHandler.BuildTermReportsAsync` (l'année est déjà dans la ligne : `SchoolYearId`) ; le loader y est appelé une fois par année distincte. Aucune autre ligne de calcul ne change.
- [ ] **Step 4 : constater le succès** — `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~EffectiveCoefficientTests|FullyQualifiedName~GetReportCardPdfTests|FullyQualifiedName~GradeSummary|FullyQualifiedName~StudentDetail"` (non-régression des bulletins et de la fiche) → PASS.
- [ ] **Step 5 : commit** — `feat(coefficients): moyennes et bulletins sur le coefficient effectif (classe › série › matière)`

---

### Task 5 : API des surcharges (grille, écriture, rétablir, reconduire)

**Files:**
- Create: `src/SamaEcole.Application/Coefficients/Queries/{GetCoefficientGrid,GetSeriesCatalog}/*`, `Commands/{UpsertCoefficientOverride,DeleteCoefficientOverride,CarryOverCoefficients}/*`
- Create: `src/SamaEcole.Web/Controllers/CoefficientsController.cs`
- Test: `tests/SamaEcole.IntegrationTests/Coefficients/CoefficientOverrideCommandsTests.cs`, `tests/SamaEcole.UnitTests/Coefficients/UpsertCoefficientOverrideValidatorTests.cs`

**Interfaces:**
- Consumes : tout ce qui précède.
- Produces (routes, `[Authorize]` ; écriture `Roles = Directeur`, lecture `Directeur,Secretariat` ; `[RequireModule(SchoolModule.Pedagogy)]`) :
  - `GET  /api/v1/coefficients/catalog` → `[{code, label}]`
  - `GET  /api/v1/coefficients?series=S2` ou `?classroomId={id}` (+ `schoolYearId` optionnel, défaut année active) → `[{subjectId, subjectName, level, baseCoefficient, overrideId?, overrideCoefficient?, effectiveCoefficient, source: "Subject"|"Series"|"Classroom", rowVersion?}]` — matières **hors primaire/maternelle** (le niveau texte est libre : voir « Constat »)
  - `PUT  /api/v1/coefficients` corps `{subjectId, series? | classroomId?, coefficient, rowVersion?}` → upsert sur l'année ACTIVE ; 422 (bornes, portée invalide par A8, matière primaire), 409 (`xmin`)
  - `DELETE /api/v1/coefficients/{id}` → suppression logique (« Rétablir »)
  - `POST /api/v1/coefficients/carry-over` corps `{fromSchoolYearId}` → copie les surcharges de l'année source vers l'année active **sans écraser** l'existant ; renvoie `{copied, skipped}`
- Toutes les commandes implémentent `IAuditableRequest`.

- [ ] **Step 1 : tests qui échouent** — validateur (borne > 0, ≤ 20 ; exactement une portée) ; intégration : upsert crée puis met à jour ; **409** sur `rowVersion` périmé ; portée « série » refusée pour une série hors catalogue ; portée « classe » refusée pour une classe de primaire (A8) ; « série » sans classe Lycée existante acceptée (la série vit indépendamment des classes) ; `DELETE` puis recréation possible (index partiel) ; `carry-over` ne touche pas l'existant ; un Secrétariat lit mais n'écrit pas (403) ; entrée d'audit créée ; isolation entre écoles.
- [ ] **Step 2 : constater l'échec.**
- [ ] **Step 3 : implémenter** ; la grille calcule `effectiveCoefficient` et `source` avec `SubjectCoefficients.Resolve` (jamais une seconde formule) ; le contrôleur reste mince.
- [ ] **Step 4 : constater le succès** — `dotnet test … --filter "FullyQualifiedName~Coefficient"`.
- [ ] **Step 5 : commit** — `feat(coefficients): API de surcharge des coefficients (Directeur)`

---

### Task 6 : Modèles nationaux par série — **bloquée par A10 (Annexe A)**

**Files:**
- Create: `src/SamaEcole.Application/Coefficients/SeriesCoefficientTemplates.cs`, `Commands/ApplySeriesTemplate/*`
- Modify: `CoefficientsController.cs` (route)
- Test: `tests/SamaEcole.UnitTests/Coefficients/SeriesCoefficientTemplatesTests.cs`, `tests/SamaEcole.IntegrationTests/Coefficients/ApplySeriesTemplateTests.cs`

**Interfaces:**
- Produces : `SeriesCoefficientTemplates.For(string series) : IReadOnlyList<TemplateLine(string Label, string[] Aliases, decimal Coefficient)>` ; `POST /api/v1/coefficients/apply-template` corps `{series, overwrite}` (Directeur) → `{applied, updated, skippedExisting, unmatchedTemplateLines[], uncoveredSubjects[]}`. Correspondance : `TextFolding.Fold(subject.Name)` égal à un alias plié ; candidates = matières dont `ClassroomCycle.CycleFor(level)` n'est ni Primaire ni Maternelle. **Ne modifie jamais `Subject.Coefficient`.** Sans `overwrite`, une surcharge de série existante est conservée.

- [ ] **Step 1 : tests qui échouent** — unitaires : chaque série du catalogue a un modèle non vide, aucun doublon d'alias dans une série, tous les coefficients dans (0 ; 20] ; correspondance insensible à la casse et aux accents (`Mathematiques` = `Mathématiques`). Intégration, **avec un modèle de test injecté** (le service prend le fournisseur de modèle en dépendance pour ne pas coupler les tests aux valeurs nationales) : le rapport distingue « appliquée », « existante conservée », « ligne de modèle sans matière », « matière non couverte » ; `overwrite=true` remplace ; `Subject.Coefficient` inchangé ; l'appel est idempotent.
- [ ] **Step 2 : constater l'échec.**
- [ ] **Step 3 : implémenter** la mécanique avec les données de l'**Annexe A telle que fournie** ; si l'annexe n'est pas encore là, **s'arrêter ici et le dire** — ne rien inventer.
- [ ] **Step 4 : constater le succès** — `--filter "FullyQualifiedName~SeriesCoefficientTemplates|FullyQualifiedName~ApplySeriesTemplate"`.
- [ ] **Step 5 : commit** — `feat(coefficients): modèles de coefficients par série et action « Appliquer le modèle »`

---

### Task 7 : Interface — série de la classe et onglet « Coefficients »

**Files:**
- Modify: `wwwroot/js/classrooms.js`, `Views/Classrooms/Index.cshtml` (champ « Série », visible seulement pour un cycle Lycée ; pastille de série dans la liste)
- Create: `wwwroot/js/coefficients.js`, `Views/Subjects/_CoefficientsTab.cshtml`
- Modify: `wwwroot/js/subjects.js` et `Views/Subjects/Index.cshtml` (3e onglet, composant partagé `.tab-nav`/`.tab-btn`, voir la mémoire du 09/2026 sur les barres d'onglets), `wwwroot/js/help.js`
- Test: `src/SamaEcole.Web/tests/js/coefficients.test.mjs`

**Interfaces:**
- Consumes : routes de la Tâche 5 et 6.
- Produces : `coefficientsView` — état `scope` (`'series' | 'classroom'`), `series`, `classroomId`, `rows`, `catalog` ; `load()`, `startEdit(row)`, `save(row)`, `restore(row)` (« Rétablir »), `applyTemplate(overwrite)`, `carryOver()`. Aucune règle métier côté client : l'écran affiche `effectiveCoefficient` et `source` **reçus du serveur**.

- [ ] **Step 1 : tests JS qui échouent** (harnais `harness.mjs`, comme `working-days-config.test.mjs`) : le chargement d'une série remplit la grille ; `save` envoie `{subjectId, series, coefficient, rowVersion}` ; une réponse 409 affiche « modifié entre-temps » et recharge ; `restore` appelle `DELETE` puis recharge ; le sélecteur « Classe » n'offre que les classes Collège/Lycée ; l'avertissement rétroactif est présent quand l'année compte des notes ; bouton « Appliquer le modèle » désactivé pour la portée « classe ».
- [ ] **Step 2 : constater l'échec.**
- [ ] **Step 3 : implémenter** l'onglet : sélecteur de portée (Série | Classe), tableau (Matière · Niveau · Base · Surcharge (champ) · Effectif · Origine (pastille Matière/Série/Classe) · « Rétablir »), boutons « Appliquer le modèle » (portée série) et « Reprendre l'année précédente », bandeau d'avertissement (arbitrage A7) : « Modifier un coefficient recalcule les moyennes et les bulletins de cette année pour les classes concernées. » Fiche d'aide « Coefficients par série » dans `help.js` (procédure, impacts, recommandations ; mettre à jour `help.test.mjs`/`help-render.test.mjs` si le texte d'aide est le contrat).
- [ ] **Step 4 : constater le succès** — `node --test tests/js/*.test.mjs` ; `dotnet build src/SamaEcole.Web -c Release` (vues Razor) ; `npm run build:css --prefix src/SamaEcole.Web`.
- [ ] **Step 5 : commit** — `feat(coefficients): série de la classe et onglet Coefficients de l'écran Matières`

---

### Task 8 : Documentation et contexte actif

**Files:**
- Modify: `openapi.yaml` (schémas `Classroom`/`ClassroomCreateRequest` : `series` ; nouvelles routes `/coefficients*`), `docs/Volume_4_API_Design.md`, `docs/Volume_1_Cahier_des_Charges.md` §8 (Bulletins : coefficients par série et surcharge), `docs/Volume_3_DDS.md` (colonne `classrooms.Series`, table `subject_coefficient_overrides`), `docs/Volume_7_Security.md` (matrice : Directeur écrit, Secrétariat lit), `docs/design-references/README.md` (une ligne : la colonne « Coefficient » affiche la valeur effective, mise en page inchangée), `ACTIVE_CONTEXT.md` §2 (nouvelle sous-section, arbitrages A1-A11 **tels que validés**, invariant « sans surcharge, le calcul est celui d'avant »).

- [ ] **Step 1 :** rédiger les mises à jour avec leur texte final.
- [ ] **Step 2 :** `python -c "import yaml; yaml.safe_load(open('openapi.yaml', encoding='utf-8'))"`, `dotnet build`, `node --test tests/js/*.test.mjs` (depuis `src/SamaEcole.Web`) → verts.
- [ ] **Step 3 : commit** — `docs(coefficients): routes, modèle de données et contexte actif de l'évolution coefficients par série`

---

## Vérification finale (à lancer par le propriétaire)

- `dotnet test` (suite complète) et `dotnet test --filter Category=MultiTenant` ; `node --test tests/js/*.test.mjs`.
- Recette manuelle : Classes → créer « Terminale S2 A » (cycle Lycée), série **S2** ; Matières › Coefficients → série **S2**, régler « Mathématiques » à 6 → l'écran montre Base 4 · Surcharge 6 · Effectif 6 · origine « Série » ; ouvrir le bulletin d'un élève de cette classe : coefficient **6**, total des coefficients et moyenne générale recalculés ; le même élève dans une classe **L2** : toujours **4** ; poser 8 sur **la classe seule** : 8 pour elle, 6 pour les autres classes S2 ; « Rétablir » → retour à 6 puis 4 ; une classe de **primaire** : aucune surcharge proposée ; **Secrétariat** : grille visible, champs non modifiables ; **nouvelle année** : grille vide, « Reprendre l'année précédente » recopie sans écraser.

## Self-review (spec ↔ tâches)

| Exigence | Tâche |
|---|---|
| 1a. Coefficients différenciés par matière selon la série au lycée | 2 (série de la classe), 3 (table), 4 (calcul), 5 (API) |
| 1b. Modèles par défaut pour les séries nationales courantes | 1 (catalogue L1, L2, S1, S2, TECH), 6 (modèles + « Appliquer le modèle ») — **données : A10** |
| 2a. Le Directeur surcharge pour une classe ou une série | 3 (portées classe/série), 5 (écriture Directeur seul, audit, 409), 7 (écran) |
| 2b. Moyennes et bulletins : valeur surchargée sinon défaut de la série | 4 (`Resolve` : classe › série › matière ; fiche élève alignée sur le bulletin par test) |
| Rétrocompatibilité des lycées existants | 4 (test 1 : sans surcharge, résultat identique) ; A5 (pas de repli implicite) |
| Isolation multi-tenant / notes | 3 (RLS testée en SQL brut), 4 (test 8) |

Points de vigilance connus, non traités par ce plan : (1) `Classroom.Series` est effacée par un `PUT` de classe qui l'omet — un ancien client de l'écran Classes pourrait effacer une série en silence ; à durcir en « absent = inchangé » si le risque est jugé réel (contrairement à `WorkingDays`, effacer une série ne verrouille pas d'écriture, mais change des coefficients) ; (2) le **cycle** de notation d'un élève (barème /10 ou /20) continue d'utiliser sa classe actuelle, y compris pour les années passées — incohérence préexistante que l'arbitrage A11 ne corrige que pour les coefficients ; (3) le niveau texte des matières étant libre, l'écran liste « toutes les matières hors primaire/maternelle » : un lycée aux nombreux niveaux verra une longue grille (filtre par nom fourni, pas de regroupement par niveau) ; (4) les exports Excel de notes n'impriment pas de coefficient : rien à changer, à re-vérifier si cela évolue.
