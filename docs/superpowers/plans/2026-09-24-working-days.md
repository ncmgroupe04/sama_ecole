# Jours ouvrés et week-ends configurables (Évolution N°3) — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal :** Chaque établissement définit ses jours ouvrés (ex. repos le jeudi et le vendredi pour une école franco-arabe ou un daara) ; la grille d'emploi du temps n'affiche que ces jours, et ni l'appel des élèves, ni le pointage des enseignants, ni un créneau d'emploi du temps ne peuvent être saisis un jour de repos.

**Architecture :** Un réglage `SchoolSettings.WorkingDays` (liste de jours, stockée en texte) lu par un unique garde `WorkingDayGuard` (Application) que les commandes d'écriture concernées appellent, et par le store client `schoolConfig` qui alimente la grille et l'écran d'appel. Toute la logique des jours (analyse, ordre d'affichage, noms français, « est-ce un jour ouvré ») vit dans un helper pur `SchoolWeek`, testé sans base. Les taux de présence sont **déjà** calculés sur les appels réellement saisis (voir « Constat sur les taux ») : le verrou garantit qu'aucun appel ne peut plus être créé un jour de repos, et des tests figent ce comportement.

**Tech Stack :** ASP.NET Core 9, EF Core/Npgsql, MediatR + FluentValidation, Alpine.js, xUnit (+ FluentAssertions), `node --test`.

**Spec :** pas de document de spécification séparé — la spécification est le message de l'utilisateur du 24/09/2026 (« Spécification Évolution N°3 ») :
1. paramètre `SchoolSettings` définissant les jours ouvrés / de week-end (ex. repos jeudi et vendredi au lieu de samedi/dimanche) ;
2. emploi du temps et vie scolaire : la grille et le générateur affichent les jours ouvrés configurés (ex. du samedi au mercredi) ; le registre d'appel et de présence est verrouillé sur les jours de repos ;
3. taux de présence : les week-ends spécifiques ne comptent pas comme des jours de classe.

## Global Constraints

- PostgreSQL uniquement ; migration EF Core **nouvelle** (jamais modifier une migration appliquée). — `AGENTS.md`.
- Toute nouvelle colonne de `school_settings` reçoit un **défaut en base** (`HasDefaultValue`) : la fonction `provision_school_director` insère une ligne de réglages sans lister toutes les colonnes ; le défaut couvre les écoles créées ensuite et les écoles existantes. Vérifier à l'étape migration qu'elle n'a pas de liste de colonnes exhaustive.
- `school_settings` est une table tenant (RLS + Global Query Filter) : le garde lit les réglages de **l'école courante** uniquement ; un test `Category=MultiTenant` le verrouille.
- Aucune logique métier dans un contrôleur ni une entité (règle #8) : les règles vivent dans `SamaEcole.Application` (`SchoolWeek`, `WorkingDayGuard`). Erreurs au format normalisé : `ValidationException` → 422, jamais une exception brute (règle #9).
- `PUT /schools/current/settings` est un remplacement **complet** : `workingDays` est ajouté au DTO, à la commande, au handler, à `GetSchoolSettingsQuery` **et** à `settings.js`. Par prudence, `workingDays` **absent ou `null` = « inchangé »** (jamais « remettre le défaut ») : c'est ce réglage qui verrouille des écritures, un ancien client ne doit pas pouvoir le réinitialiser en silence.
- Jours transportés par l'API en **noms anglais du BCL** (`"Monday"` … `"Sunday"`, comme `DayOfWeek`), stockés en texte (convention du projet : jamais un entier qui se briserait si un enum changeait), affichés en français côté client.
- **Le propriétaire lance lui-même la suite complète `dotnet test`** (consigne du 17/09/2026) ; ce plan n'exécute que des tests **ciblés** (`--filter`). `dotnet build` et `node --test` restent libres.
- Migration en local : si `SamaEcole.Web` tourne, ses DLL `Debug` sont verrouillées — générer avec `dotnet ef migrations add … --configuration Release` (voir `ACTIVE_CONTEXT.md`).
- Conventional Commits, un commit par tâche, terminé par `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`. Branche : `feature/working-days`, **empilée sur `feature/evaluation-periods`** (mêmes fichiers `SchoolSettings*`, migrations à la suite) — à fusionner après elle.

## Constat sur ce que le code fait aujourd'hui (à connaître avant de valider)

- **Il n'existe aucun « générateur » d'emploi du temps.** Les créneaux se créent un par un (formulaire de `Views/Teachers/Index.cshtml`, `CreateScheduleSlotCommand`). « Adapter le générateur » se traduit donc par : le sélecteur « Jour » du formulaire et le **validateur/handler** des créneaux n'acceptent que les jours ouvrés.
- **Les jours sont écrits en dur** : `teachers.js:89-96` (Lundi→Samedi) et `Views/Teachers/Index.cshtml:890` (mêmes six jours). Le dimanche n'est proposé nulle part dans l'interface, mais l'API l'accepte (`RuleFor(v => v.DayOfWeek).IsInEnum()`).
- **Aucun verrou de jour n'existe** : `SubmitAttendanceSheetCommandValidator` refuse seulement les dates futures ; `CreateTeacherAttendanceCommandHandler` n'a aucun contrôle de date.
- **Constat sur les taux de présence (exigence 3).** Tous les taux sont des rapports « présences ÷ lignes d'appel réellement saisies » — `AttendanceReportAggregator.ComputeAsync` (rapport + exports), `GetDirectorDashboardQueryHandler` (taux du mois) et `GetReportCardPdfQuery.CountAttendanceAsync` (assiduité du bulletin). **Aucun ne compte de jours calendaires** : un samedi ou un dimanche sans appel n'entre dans aucun dénominateur. Le seul moyen de « polluer » un taux avec un jour de repos est qu'un appel y ait été **saisi** — ce que le verrou (exigence 2) interdit désormais. L'exigence 3 se réduit donc à : (a) le verrou, (b) des tests qui figent le comportement, (c) l'arbitrage D2 sur les appels **déjà** saisis un jour devenu jour de repos.
- **Ce qui suit le réglage sans code** : « prochains cours du jour » du dashboard (filtre sur `ScheduleSlot.DayOfWeek`), `ClassJournalScopeAuthorizer` (apparie la date de séance au créneau), suggestion d'heures de paie (`GetSuggestedPayrollHoursQueryHandler`, heures planifiées = 0 un jour sans créneau → un écart est déjà signalé).

## Arbitrages à confirmer avant de commencer

| # | Question | Recommandation (appliquée par ce plan) |
|---|---|---|
| D1 | **Jours ouvrés par défaut.** | **Lundi → Samedi** : c'est exactement la grille actuelle (`teachers.js`). Un défaut Lundi → Vendredi ferait disparaître les créneaux du samedi de toutes les écoles existantes. Seul le dimanche devient jour de repos par défaut (l'API l'acceptait, l'interface ne l'a jamais proposé). |
| D2 | **Appels déjà saisis un jour devenu jour de repos** (ex. l'école passe en repos jeudi/vendredi en cours d'année alors que le premier trimestre a eu classe le jeudi). | **Ne pas les exclure rétroactivement des taux.** Ce sont de vrais appels, faits un jour où l'école ouvrait ; les retirer effacerait une partie du trimestre déjà bulletiné. Le verrou ne joue que sur les **nouvelles** saisies. Alternative, non planifiée : filtrer les trois calculs sur le réglage courant (à demander explicitement — elle réécrit l'historique à chaque changement de réglage). |
| D3 | **Créneaux d'emploi du temps existants sur un jour qui devient repos.** | **Souple** : le réglage se change librement ; la grille affiche les jours ouvrés **plus** toute colonne portant encore un créneau, marquée « jour de repos » ; créer ou déplacer un créneau vers un jour de repos est refusé (422). Alternative : refuser le changement de réglage tant que de tels créneaux existent (« blocage strict » comme l'Évolution N°2) — mais une école qui bascule de samedi/dimanche à jeudi/vendredi devrait alors supprimer à la main tous ses créneaux du jeudi et du vendredi avant de pouvoir changer le réglage. |
| D4 | **Périmètre du verrou.** | Verrouillés : **appel des élèves** (soumission **et** ouverture de la feuille), **pointage des enseignants** (`TeacherAttendance`), **créneaux d'emploi du temps** (création et déplacement). **Non verrouillés, à dessein** : billets d'entrée/sortie et retards/départs anticipés (`Absences/*`, émis le jour réel où l'élève se présente — y compris en internat), justificatifs d'absence (portent sur une absence passée), journal de classe (déjà borné par le créneau), heures déclarées de paie (l'écart avec l'emploi du temps est déjà signalé). |
| D5 | **Ordre des jours affichés.** | La grille suit la **semaine de l'école** : elle commence le lendemain du bloc de repos (repos jeudi/vendredi → Samedi, Dimanche, Lundi, Mardi, Mercredi ; repos samedi/dimanche → Lundi…Vendredi). Repos non contigus (ex. mercredi et dimanche) : ordre Lundi → Dimanche. Un seul calcul, côté serveur (`SchoolWeek.DisplayOrder`) ; le client affiche l'ordre reçu. |
| D6 | **Calendrier de jours fériés / vacances.** | Hors périmètre (non demandé). Un jour férié isolé = pas d'appel ce jour-là, sans verrou. |

## File Structure

| Fichier | Rôle |
|---|---|
| `src/SamaEcole.Application/Schools/SchoolWeek.cs` (créer) | Helper pur : analyse, sérialisation canonique, ordre d'affichage, noms français, « jour ouvré ? ». |
| `src/SamaEcole.Application/Schools/WorkingDayGuard.cs` (créer) | Service scoped : lit `SchoolSettings.WorkingDays`, lève `ValidationException` (422) sur un jour de repos. Enregistré dans `DependencyInjection.cs` avec les autres gardes. |
| `src/SamaEcole.Domain/Entities/SchoolSettings.cs` (modifier) | `WorkingDays` + `SchoolSettingsDefaults.WorkingDays`. |
| `SchoolSettingsConfiguration.cs`, `SchoolSettingsDto.cs`, `UpdateSchoolSettingsCommand{,Handler,Validator}.cs`, `GetSchoolSettingsQuery.cs`, `SchoolSettingsController.cs` (modifier) | Plomberie du réglage. |
| `src/SamaEcole.Persistence/Migrations/*_AddSchoolWorkingDays.cs` (générer) | Une colonne. |
| `SubmitAttendanceSheetCommandHandler.cs`, `InitializeAttendanceSheetQueryHandler.cs`, `CreateTeacherAttendanceCommandHandler.cs`, `CreateScheduleSlotCommand.cs`, `UpdateScheduleSlotCommand.cs` (modifier) | Appellent le garde. |
| `wwwroot/js/auth.js` (store `schoolConfig`), `settings.js`, `Views/Settings/Index.cshtml`, `teachers.js`, `Views/Teachers/Index.cshtml`, `attendance.js`, `Views/Attendance/*.cshtml`, `help.js` (modifier) | Interface. |
| `tests/SamaEcole.UnitTests/Schools/SchoolWeekTests.cs` et voisins ; `tests/SamaEcole.IntegrationTests/Attendance/WorkingDayLockTests.cs`, `…/Schedules/ScheduleWorkingDaysTests.cs`, `…/Reports/AttendanceRateWorkingDaysTests.cs` | Tests. |

---

### Task 1 : `SchoolWeek` (helper pur, sans base)

**Files:**
- Create: `src/SamaEcole.Application/Schools/SchoolWeek.cs`
- Test: `tests/SamaEcole.UnitTests/Schools/SchoolWeekTests.cs`

**Interfaces:**
- Produces (`public static class SchoolWeek`, namespace `SamaEcole.Application.Schools`) :
  - `const string DefaultStored = "Monday,Tuesday,Wednesday,Thursday,Friday,Saturday"`
  - `IReadOnlyList<DayOfWeek>? TryParse(IEnumerable<string>? names)` — `null` si vide, inconnu, numérique ou en double
  - `IReadOnlyList<DayOfWeek> FromStored(string? stored)` — texte de la base → jours ; défaut si vide/invalide
  - `string Serialize(IEnumerable<DayOfWeek> days)` — forme canonique, ordre lundi→dimanche
  - `IReadOnlyList<DayOfWeek> DisplayOrder(IEnumerable<DayOfWeek> days)` — arbitrage D5
  - `IReadOnlyList<string> ToNames(IEnumerable<DayOfWeek> days)` — noms anglais, dans l'ordre d'affichage (ce que renvoie l'API)
  - `bool IsWorkingDay(IEnumerable<DayOfWeek> days, DateOnly date)` et `bool IsWorkingDay(IEnumerable<DayOfWeek> days, DayOfWeek day)`
  - `string FrenchName(DayOfWeek day)` (« jeudi ») ; `string RestDaysLabel(IEnumerable<DayOfWeek> days)` (« jeudi et vendredi » ; « aucun » si la semaine est pleine)

- [ ] **Step 1 : écrire les tests qui échouent**

```csharp
// tests/SamaEcole.UnitTests/Schools/SchoolWeekTests.cs
using FluentAssertions;
using SamaEcole.Application.Schools;
using Xunit;

namespace SamaEcole.UnitTests.Schools;

public class SchoolWeekTests
{
    private static readonly DayOfWeek[] MonToFri =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    // Repos jeudi et vendredi : école franco-arabe / daara.
    private static readonly DayOfWeek[] SatToWed =
        [DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday];

    [Fact]
    public void Default_Is_Monday_To_Saturday_Like_The_Historical_Grid()
        => SchoolWeek.FromStored(SchoolWeek.DefaultStored).Should().Equal(
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
            DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Funday")]
    [InlineData("1,2,3")]
    [InlineData("Monday,Monday")]
    public void An_Empty_Or_Invalid_Stored_Value_Falls_Back_To_The_Default(string? stored)
        => SchoolWeek.FromStored(stored).Should().Equal(SchoolWeek.FromStored(SchoolWeek.DefaultStored));

    [Fact]
    public void TryParse_Accepts_Names_Case_Insensitively()
        => SchoolWeek.TryParse(["saturday", " SUNDAY ", "Monday"]).Should()
            .Equal(DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday);

    [Theory]
    [InlineData("Monday", "Monday")]   // doublon
    [InlineData("Monday", "Someday")]  // inconnu
    [InlineData("Monday", "3")]        // numérique : refusé, on ne devine pas
    public void TryParse_Rejects_Duplicates_Unknown_And_Numeric_Names(string first, string second)
        => SchoolWeek.TryParse([first, second]).Should().BeNull();

    [Fact]
    public void TryParse_Rejects_An_Empty_Week()
    {
        SchoolWeek.TryParse([]).Should().BeNull();
        SchoolWeek.TryParse(null).Should().BeNull();
    }

    [Fact]
    public void Serialize_Is_Canonical_Whatever_The_Input_Order()
        => SchoolWeek.Serialize(SatToWed).Should().Be("Monday,Tuesday,Wednesday,Saturday,Sunday");

    [Fact]
    public void Display_Order_Starts_The_Day_After_The_Rest_Block()
    {
        SchoolWeek.DisplayOrder(MonToFri).Should().Equal(MonToFri);
        SchoolWeek.DisplayOrder(SatToWed).Should().Equal(SatToWed); // Samedi → Mercredi
        SchoolWeek.DisplayOrder([DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday])
            .Should().Equal(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday);
        // Repos le vendredi seul : la semaine commence le samedi.
        SchoolWeek.DisplayOrder([DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Saturday, DayOfWeek.Sunday])
            .Should().Equal(DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday);
    }

    [Fact]
    public void Non_Contiguous_Rest_Days_Fall_Back_To_Monday_First()
        // Repos le mercredi ET le dimanche : pas de « bloc » unique, donc pas de début de semaine évident.
        => SchoolWeek.DisplayOrder([DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday])
            .Should().Equal(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday);

    [Fact]
    public void A_Full_Week_Has_No_Rest_Day()
    {
        var all = Enum.GetValues<DayOfWeek>();

        SchoolWeek.DisplayOrder(all).Should().HaveCount(7).And.StartWith(DayOfWeek.Monday);
        SchoolWeek.RestDaysLabel(all).Should().Be("aucun");
    }

    [Theory]
    [InlineData("2026-09-24", false)] // jeudi
    [InlineData("2026-09-25", false)] // vendredi
    [InlineData("2026-09-26", true)]  // samedi
    [InlineData("2026-09-27", true)]  // dimanche
    [InlineData("2026-09-23", true)]  // mercredi
    public void IsWorkingDay_Follows_The_Configured_Week(string isoDate, bool expected)
        => SchoolWeek.IsWorkingDay(SatToWed, DateOnly.Parse(isoDate)).Should().Be(expected);

    [Fact]
    public void ToNames_Returns_English_Names_In_Display_Order()
        => SchoolWeek.ToNames(SatToWed).Should().Equal("Saturday", "Sunday", "Monday", "Tuesday", "Wednesday");

    [Theory]
    [InlineData(new[] { DayOfWeek.Thursday, DayOfWeek.Friday }, "jeudi et vendredi")]
    [InlineData(new[] { DayOfWeek.Sunday }, "dimanche")]
    [InlineData(new[] { DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday }, "vendredi, samedi et dimanche")]
    public void RestDaysLabel_Reads_As_French_Prose(DayOfWeek[] rest, string expected)
    {
        var working = Enum.GetValues<DayOfWeek>().Except(rest);

        SchoolWeek.RestDaysLabel(working).Should().Be(expected);
    }
}
```

- [ ] **Step 2 : constater l'échec** — `dotnet test tests/SamaEcole.UnitTests --filter "FullyQualifiedName~SchoolWeekTests"` → erreur de compilation (`SchoolWeek` introuvable).

- [ ] **Step 3 : implémenter**

```csharp
// src/SamaEcole.Application/Schools/SchoolWeek.cs
namespace SamaEcole.Application.Schools;

/// <summary>
/// Semaine de travail d'un établissement (Évolution N°3) : quels jours sont OUVRÉS, dans quel ordre on
/// les affiche, comment on les dit en français. Pur — aucun accès base — pour que chaque règle se
/// teste seule. Le réglage vit dans SchoolSettings.WorkingDays (texte : « Monday,Tuesday,… »).
/// </summary>
public static class SchoolWeek
{
    /// <summary>Lundi → Samedi : la grille historique. Voir SchoolSettingsDefaults.WorkingDays (à garder identique).</summary>
    public const string DefaultStored = "Monday,Tuesday,Wednesday,Thursday,Friday,Saturday";

    private static readonly DayOfWeek[] IsoOrder =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    ];

    private static readonly Dictionary<DayOfWeek, string> French = new()
    {
        [DayOfWeek.Monday] = "lundi", [DayOfWeek.Tuesday] = "mardi", [DayOfWeek.Wednesday] = "mercredi",
        [DayOfWeek.Thursday] = "jeudi", [DayOfWeek.Friday] = "vendredi",
        [DayOfWeek.Saturday] = "samedi", [DayOfWeek.Sunday] = "dimanche"
    };

    /// <summary>
    /// Noms → jours. `null` si la liste est vide, contient un nom inconnu, un nombre (« 3 » : on ne
    /// devine pas) ou un doublon — un réglage douteux ne doit jamais être stocké à moitié.
    /// </summary>
    public static IReadOnlyList<DayOfWeek>? TryParse(IEnumerable<string>? names)
    {
        if (names is null) return null;

        var days = new List<DayOfWeek>();
        foreach (var raw in names)
        {
            var name = raw?.Trim();
            if (string.IsNullOrEmpty(name) || int.TryParse(name, out _)) return null;
            if (!Enum.TryParse<DayOfWeek>(name, ignoreCase: true, out var day) || !Enum.IsDefined(day)) return null;
            if (days.Contains(day)) return null;
            days.Add(day);
        }

        return days.Count == 0 ? null : days;
    }

    /// <summary>Texte de la base → jours. Une valeur vide ou illisible retombe sur le défaut, jamais sur « aucun jour ».</summary>
    public static IReadOnlyList<DayOfWeek> FromStored(string? stored)
        => TryParse(stored?.Split(',', StringSplitOptions.RemoveEmptyEntries))
           ?? TryParse(DefaultStored.Split(','))!;

    /// <summary>Forme canonique stockée : lundi → dimanche, quel que soit l'ordre d'entrée.</summary>
    public static string Serialize(IEnumerable<DayOfWeek> days)
    {
        var set = days.ToHashSet();
        return string.Join(',', IsoOrder.Where(set.Contains));
    }

    /// <summary>
    /// Ordre d'AFFICHAGE : la semaine commence le lendemain du bloc de repos (repos jeudi/vendredi →
    /// samedi, dimanche, lundi, mardi, mercredi). Repos non contigus, ou aucun repos : lundi → dimanche.
    /// </summary>
    public static IReadOnlyList<DayOfWeek> DisplayOrder(IEnumerable<DayOfWeek> days)
    {
        var set = days.ToHashSet();

        // « Fin de bloc de repos » : un jour de repos dont le lendemain est ouvré. Un seul = un bloc contigu.
        var blockEnds = Enumerable.Range(0, 7)
            .Where(i => !set.Contains(IsoOrder[i]) && set.Contains(IsoOrder[(i + 1) % 7]))
            .ToList();

        var start = blockEnds.Count == 1 ? (blockEnds[0] + 1) % 7 : 0;

        return Enumerable.Range(0, 7)
            .Select(offset => IsoOrder[(start + offset) % 7])
            .Where(set.Contains)
            .ToList();
    }

    public static IReadOnlyList<string> ToNames(IEnumerable<DayOfWeek> days)
        => DisplayOrder(days).Select(d => d.ToString()).ToList();

    public static bool IsWorkingDay(IEnumerable<DayOfWeek> days, DayOfWeek day) => days.Contains(day);

    public static bool IsWorkingDay(IEnumerable<DayOfWeek> days, DateOnly date) => days.Contains(date.DayOfWeek);

    public static string FrenchName(DayOfWeek day) => French[day];

    /// <summary>« jeudi et vendredi », « vendredi, samedi et dimanche », « aucun » si la semaine est pleine.</summary>
    public static string RestDaysLabel(IEnumerable<DayOfWeek> days)
    {
        var set = days.ToHashSet();
        var rest = IsoOrder.Where(d => !set.Contains(d)).Select(FrenchName).ToList();

        return rest.Count switch
        {
            0 => "aucun",
            1 => rest[0],
            _ => $"{string.Join(", ", rest.Take(rest.Count - 1))} et {rest[^1]}"
        };
    }
}
```

- [ ] **Step 4 : constater le succès** — même commande → PASS.
- [ ] **Step 5 : commit**

```bash
git add src/SamaEcole.Application/Schools/SchoolWeek.cs tests/SamaEcole.UnitTests/Schools/SchoolWeekTests.cs
git commit -m "feat(working-days): SchoolWeek, helper pur des jours ouvrés"
```

---

### Task 2 : Le réglage `WorkingDays` (entité, migration, API)

**Files:**
- Modify: `src/SamaEcole.Domain/Entities/SchoolSettings.cs` (propriété + `SchoolSettingsDefaults`)
- Modify: `src/SamaEcole.Persistence/Configurations/SchoolSettingsConfiguration.cs`
- Modify: `src/SamaEcole.Application/Schools/SchoolSettingsDto.cs`, `.../UpdateSchoolSettings/UpdateSchoolSettingsCommand.cs`, `...Handler.cs`, `...Validator.cs`, `.../GetSchoolSettings/GetSchoolSettingsQuery.cs` (`Map` **et** `Defaults`)
- Modify: `src/SamaEcole.Web/Controllers/SchoolSettingsController.cs` (record `UpdateSettingsRequest` + construction de la commande)
- Create (généré) : migration `AddSchoolWorkingDays`
- Test: `tests/SamaEcole.UnitTests/Schools/UpdateSchoolSettingsCommandValidatorTests.cs` (ajout)

**Interfaces:**
- Consumes : `SchoolWeek.TryParse`, `SchoolWeek.Serialize`, `SchoolWeek.FromStored`, `SchoolWeek.ToNames`, `SchoolWeek.DefaultStored` (Tâche 1).
- Produces :
  - `SchoolSettings.WorkingDays` (`string`, défaut `SchoolSettingsDefaults.WorkingDays`)
  - DTO / commande / requête : `IReadOnlyList<string>? WorkingDays = null` en **dernière position** (après `CustomPeriodCount`) ; dans le DTO de réponse, toujours renseigné, **dans l'ordre d'affichage**.

- [ ] **Step 1 : tests du validateur qui échouent** (dans `UpdateSchoolSettingsCommandValidatorTests`, réutiliser `ValidCommand()`)

```csharp
[Fact]
public void Working_Days_Left_Out_Are_Valid_And_Mean_Unchanged()
    => _validator.Validate(ValidCommand() with { WorkingDays = null }).IsValid.Should().BeTrue();

[Theory]
[InlineData("Saturday", "Sunday", "Monday", "Tuesday", "Wednesday")]   // repos jeudi/vendredi
[InlineData("Monday", "Tuesday", "Wednesday", "Thursday", "Friday")]
[InlineData("monday")]                                                  // un seul jour, casse ignorée
[InlineData("Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday")] // semaine pleine
public void Valid_Working_Weeks_Are_Accepted(params string[] days)
    => _validator.Validate(ValidCommand() with { WorkingDays = days }).IsValid.Should().BeTrue();

[Theory]
[InlineData(new string[0])]                       // aucun jour : l'école ne pourrait plus rien saisir
[InlineData(new[] { "Monday", "Monday" })]        // doublon
[InlineData(new[] { "Monday", "Funday" })]        // inconnu
[InlineData(new[] { "1", "2" })]                  // numérique
public void Invalid_Working_Weeks_Are_Rejected(string[] days)
    => _validator.Validate(ValidCommand() with { WorkingDays = days }).IsValid.Should().BeFalse();
```

- [ ] **Step 2 : constater l'échec** (compilation : `WorkingDays` inconnu).

- [ ] **Step 3 : entité, défaut, configuration**

`SchoolSettings` (après `CustomPeriodCount`) :

```csharp
    /// <summary>
    /// Jours OUVRÉS de l'établissement (Évolution N°3), noms de <see cref="DayOfWeek"/> séparés par des
    /// virgules, forme canonique lundi → dimanche (SchoolWeek.Serialize). Les autres jours sont des jours
    /// de repos : ni appel, ni pointage enseignant, ni créneau d'emploi du temps n'y sont saisis. Défaut
    /// lundi → samedi : la grille historique, donc aucune école existante ne perd de jour.
    /// </summary>
    public string WorkingDays { get; set; } = SchoolSettingsDefaults.WorkingDays;
```

`SchoolSettingsDefaults` : `public const string WorkingDays = "Monday,Tuesday,Wednesday,Thursday,Friday,Saturday";` (commentaire : « à garder identique à SchoolWeek.DefaultStored »).

`SchoolSettingsConfiguration` :

```csharp
        builder.Property(s => s.WorkingDays)
            .HasMaxLength(80)
            .IsRequired()
            .HasDefaultValue(SchoolSettingsDefaults.WorkingDays);
```

- [ ] **Step 4 : plomberie API**

Ajouter `IReadOnlyList<string>? WorkingDays = null` en **dernier paramètre** de `SchoolSettingsDto`, `UpdateSchoolSettingsCommand` et `UpdateSettingsRequest` (contrôleur : passer `request.WorkingDays` à la commande).

Handler, après `CustomPeriodCount` :

```csharp
        // null = « inchangé » : c'est ce réglage qui verrouille des saisies, un ancien client qui ne
        // l'envoie pas ne doit pas le réinitialiser. Valeur invalide : refusée en amont par le validateur.
        if (request.WorkingDays is not null)
        {
            settings.WorkingDays = SchoolWeek.Serialize(SchoolWeek.TryParse(request.WorkingDays)!);
        }
```

… et `SchoolWeek.ToNames(SchoolWeek.FromStored(settings.WorkingDays))` en fin du `new SchoolSettingsDto(...)`. Même ajout dans `GetSchoolSettingsQuery.Map` (`SchoolWeek.ToNames(SchoolWeek.FromStored(settings.WorkingDays))`) et `Defaults()` (`SchoolWeek.ToNames(SchoolWeek.FromStored(null))`). Validateur :

```csharp
        // Semaine de travail (Évolution N°3) : au moins un jour, sans doublon ni nom inconnu. null = inchangé.
        RuleFor(c => c.WorkingDays)
            .Must(days => SchoolWeek.TryParse(days) is not null)
            .When(c => c.WorkingDays is not null)
            .WithMessage("Les jours ouvrés doivent compter au moins un jour, sans doublon (Monday … Sunday).");
```

- [ ] **Step 5 : migration**

Run : `dotnet ef migrations add AddSchoolWorkingDays -p src/SamaEcole.Persistence -s src/SamaEcole.Web --configuration Release`
Vérifier : un seul `AddColumn` (`WorkingDays`, `character varying(80)`, `nullable: false`, défaut `'Monday,Tuesday,Wednesday,Thursday,Friday,Saturday'`), diff du snapshot limité à cette colonne. Vérifier aussi (`grep -rn "provision_school_director" src/SamaEcole.Persistence/Migrations | tail -3`) que la fonction d'approvisionnement n'énumère pas toutes les colonnes de `school_settings` sans défaut. **Ne pas** appliquer en production à la main.

- [ ] **Step 6 : constater le succès** — `dotnet build` puis `dotnet test tests/SamaEcole.UnitTests --filter "FullyQualifiedName~UpdateSchoolSettingsCommandValidatorTests|FullyQualifiedName~SchoolWeekTests"` → PASS.

- [ ] **Step 7 : commit**

```bash
git add src tests
git commit -m "feat(working-days): réglage WorkingDays dans SchoolSettings (colonne, API, validation)"
```

---

### Task 3 : `WorkingDayGuard` et verrous d'écriture

**Files:**
- Create: `src/SamaEcole.Application/Schools/WorkingDayGuard.cs`
- Modify: `src/SamaEcole.Application/DependencyInjection.cs` (à côté de `AddScoped<AttendanceScopeAuthorizer>()`)
- Modify: `SubmitAttendanceSheetCommandHandler.cs`, `InitializeAttendanceSheetQueryHandler.cs`, `CreateTeacherAttendanceCommandHandler.cs`, `Features/Schedules/CreateScheduleSlotCommand.cs`, `Features/Schedules/UpdateScheduleSlotCommand.cs`
- Test: `tests/SamaEcole.IntegrationTests/Attendance/WorkingDayLockTests.cs`, `tests/SamaEcole.IntegrationTests/Schedules/ScheduleWorkingDaysTests.cs`

**Interfaces:**
- Consumes : `SchoolWeek`, `SchoolSettings.WorkingDays`.
- Produces (`WorkingDayGuard`, ctor `IApplicationDbContext`) :
  - `Task<IReadOnlyList<DayOfWeek>> GetWorkingDaysAsync(CancellationToken ct)`
  - `Task EnsureWorkingDayAsync(DateOnly date, string field, CancellationToken ct)` — lève `ValidationException` (422) sur `field` si `date` tombe un jour de repos
  - `Task EnsureWorkingDayAsync(DayOfWeek day, string field, CancellationToken ct)` — idem pour un créneau (sans date)

- [ ] **Step 1 : tests d'intégration qui échouent.** S'appuyer sur le montage de `tests/SamaEcole.IntegrationTests/Attendance/AttendanceIsolationTests.cs` (école, année active, classe, matière, élève, compte Directeur) et de `Schedules/ScheduleOwnershipTests.cs`. Le réglage se pose par le contexte propriétaire : `owner.SchoolSettings.Add(new SchoolSettings { SchoolId = Ecole, WorkingDays = "Monday,Tuesday,Wednesday,Saturday,Sunday" })` (repos jeudi/vendredi). Choisir des dates **passées** (le validateur refuse le futur) dont le jour est connu : `2026-09-24` = jeudi, `2026-09-26` = samedi.

  Cas de `WorkingDayLockTests` :
  1. `SubmitAttendanceSheetCommandHandler` avec `Date = 2026-09-24` (jeudi, repos) → `ValidationException` dont le message contient « jeudi » et « jour de repos » ; **aucune** `AttendanceSheet` créée (`ctx.AttendanceSheets.CountAsync() == 0`).
  2. Même commande `Date = 2026-09-26` (samedi, ouvré) → succès, une fiche créée.
  3. `InitializeAttendanceSheetQueryHandler` un jeudi → `ValidationException` ; un samedi → feuille renvoyée.
  4. `CreateTeacherAttendanceCommandHandler` avec `Date = new DateTime(2026, 9, 25)` (vendredi) → `ValidationException` ; un dimanche → succès.
  5. **École sans ligne `SchoolSettings`** → défaut lundi→samedi : un samedi passe, un dimanche est refusé.
  6. `[Trait("Category", "MultiTenant")]` — l'école A a le jeudi en repos, l'école B (défaut) : un appel un jeudi passe chez B et est refusé chez A.

  Cas de `ScheduleWorkingDaysTests` :
  7. `CreateScheduleSlotCommandHandler` avec `DayOfWeek.Thursday` (repos) → `ValidationException`, aucun créneau créé ; avec `DayOfWeek.Saturday` → succès.
  8. `UpdateScheduleSlotCommandHandler` déplaçant un créneau existant vers `Friday` (repos) → refusé, le créneau garde son jour.
  9. **Créneau hérité** : un créneau posé un jeudi *avant* le changement de réglage reste lisible (`GetClassroomScheduleQuery`) et peut être **supprimé** ; le modifier **sans changer son jour** (ex. la salle) est refusé — le garde juge le jour du créneau écrit, un jour de repos reste un jour de repos (arbitrage D3).

- [ ] **Step 2 : constater l'échec** — `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~WorkingDayLockTests|FullyQualifiedName~ScheduleWorkingDaysTests"`.

- [ ] **Step 3 : implémenter le garde**

```csharp
// src/SamaEcole.Application/Schools/WorkingDayGuard.cs
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Schools;

/// <summary>
/// Refuse (422) toute saisie datée un JOUR DE REPOS de l'établissement (Évolution N°3, réglage
/// SchoolSettings.WorkingDays). Lit les réglages de l'école COURANTE — le Global Query Filter et la RLS
/// bornent la lecture —, jamais un identifiant fourni par le client (règle #10). Une école sans ligne de
/// réglages, ou dont la valeur est illisible, garde la semaine par défaut (lundi → samedi).
///
/// Ne s'applique qu'aux saisies NOUVELLES : les données déjà enregistrées un jour devenu jour de repos
/// restent lisibles et comptées (arbitrage D2 du plan 2026-09-24-working-days).
/// </summary>
public class WorkingDayGuard(IApplicationDbContext dbContext)
{
    public async Task<IReadOnlyList<DayOfWeek>> GetWorkingDaysAsync(CancellationToken cancellationToken)
    {
        var stored = await dbContext.SchoolSettings.AsNoTracking()
            .Select(s => s.WorkingDays)
            .FirstOrDefaultAsync(cancellationToken);

        return SchoolWeek.FromStored(stored);
    }

    public async Task EnsureWorkingDayAsync(DateOnly date, string field, CancellationToken cancellationToken)
    {
        var days = await GetWorkingDaysAsync(cancellationToken);
        if (SchoolWeek.IsWorkingDay(days, date)) return;

        throw new ValidationException([
            new ValidationFailure(field,
                $"Le {SchoolWeek.FrenchName(date.DayOfWeek)} {date:dd/MM/yyyy} est un jour de repos de l'établissement "
                + $"({SchoolWeek.RestDaysLabel(days)}) : aucune saisie n'y est possible. "
                + "Les jours ouvrés se règlent dans Paramètres › Pédagogie.")
        ]);
    }

    public async Task EnsureWorkingDayAsync(DayOfWeek day, string field, CancellationToken cancellationToken)
    {
        var days = await GetWorkingDaysAsync(cancellationToken);
        if (SchoolWeek.IsWorkingDay(days, day)) return;

        throw new ValidationException([
            new ValidationFailure(field,
                $"Le {SchoolWeek.FrenchName(day)} est un jour de repos de l'établissement ({SchoolWeek.RestDaysLabel(days)}) : "
                + "un créneau d'emploi du temps ne peut pas y être placé.")
        ]);
    }
}
```

Enregistrement : `services.AddScoped<Schools.WorkingDayGuard>();` (comme les autres gardes).

- [ ] **Step 4 : brancher le garde.** Ajouter `WorkingDayGuard workingDayGuard` au constructeur primaire de chaque handler, puis :
  - `SubmitAttendanceSheetCommandHandler` — juste après `scopeAuthorizer.EnsureCanTakeAttendanceAsync(...)` : `await workingDayGuard.EnsureWorkingDayAsync(request.Date, nameof(request.Date), cancellationToken);`
  - `InitializeAttendanceSheetQueryHandler` — au même endroit, après le contrôle de portée : même appel.
  - `CreateTeacherAttendanceCommandHandler` — avant la recherche du pointage existant : `await workingDayGuard.EnsureWorkingDayAsync(DateOnly.FromDateTime(request.Date), nameof(request.Date), cancellationToken);` (`Date` y est un `DateTime`).
  - `CreateScheduleSlotCommandHandler` et `UpdateScheduleSlotCommandHandler` — après `ownershipAuthorizer.EnsureOwnsAsync(...)` : `await workingDayGuard.EnsureWorkingDayAsync(request.DayOfWeek, nameof(request.DayOfWeek), cancellationToken);`

  Les tests unitaires qui construisent ces handlers à la main (`grep -rn "new SubmitAttendanceSheetCommandHandler\|new CreateScheduleSlotCommandHandler\|new UpdateScheduleSlotCommandHandler\|new CreateTeacherAttendanceCommandHandler\|new InitializeAttendanceSheetQueryHandler" tests`) reçoivent `new WorkingDayGuard(ctx)`.

- [ ] **Step 5 : constater le succès** — mêmes commandes que l'étape 2, puis `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~Attendance|FullyQualifiedName~Schedules"` (non-régression) → PASS.

- [ ] **Step 6 : commit**

```bash
git add src tests
git commit -m "feat(working-days): appel, pointage et créneaux refusés un jour de repos (WorkingDayGuard)"
```

---

### Task 4 : Taux de présence — figer le comportement par des tests

Le calcul ne change **pas** (voir « Constat sur les taux » et D2) : ce sont des rapports « présences ÷ appels saisis ». Cette tâche ne modifie donc aucun handler ; elle verrouille les deux propriétés qui rendent l'exigence 3 vraie, pour qu'une future refonte du calcul (par exemple un dénominateur en jours calendaires) ne les casse pas en silence.

**Files:**
- Test: `tests/SamaEcole.IntegrationTests/Reports/AttendanceRateWorkingDaysTests.cs` (créer)

**Interfaces:**
- Consumes : `AttendanceReportAggregator.ComputeAsync(DateOnly, DateOnly, Guid?, CancellationToken)` (`Application/Reports/AttendanceReportAggregator.cs`), `WorkingDayGuard` (Tâche 3).

- [ ] **Step 1 : écrire les tests.** Montage : école avec `WorkingDays = "Monday,Tuesday,Wednesday,Saturday,Sunday"` (repos jeudi/vendredi), une classe, deux élèves, des fiches insérées par le contexte **propriétaire** (les inscrire via le handler serait refusé les jours de repos).
  1. **Un jour de repos sans appel ne pèse pas.** Fiches : samedi 2026-09-19 (2 présents), dimanche 2026-09-20 (1 présent, 1 absent) ; aucune fiche jeudi/vendredi. Sur la période 2026-09-19 → 2026-09-27 : `AverageAttendanceRate == 0.75m` (3 présents ÷ 4 lignes) — les jeudi 24 et vendredi 25, vides, n'entrent dans aucun dénominateur.
  2. **Un appel historique saisi un jour devenu repos reste compté (D2).** Ajouter une fiche jeudi 2026-09-24 (2 absents) → le taux passe à `3/6 = 0.5m` : l'appel existant n'est pas exclu rétroactivement.
  3. **Le verrou empêche d'en ajouter un nouveau** : `SubmitAttendanceSheetCommandHandler` un vendredi → `ValidationException`, et le taux de la période est inchangé.
  4. **Assiduité du bulletin** : `GetReportCardPdfQuery` (ou son handler `CountAttendanceAsync` via un bulletin) ne compte que les fiches existantes du trimestre — jamais de « jours calendaires » : un trimestre sans aucune fiche renvoie `null` (« - »), comme aujourd'hui (non-régression du comportement JGK-D06).

- [ ] **Step 2 : lancer** — `dotnet test tests/SamaEcole.IntegrationTests --filter "FullyQualifiedName~AttendanceRateWorkingDaysTests"` → PASS **sans changement de code** (si un cas échoue, le constat de ce plan est faux : s'arrêter et le signaler avant de poursuivre).

- [ ] **Step 3 : commit**

```bash
git add tests
git commit -m "test(working-days): les taux de présence ignorent les jours de repos sans appel et gardent l'historique"
```

---

### Task 5 : Interface — réglage, grille d'emploi du temps, écran d'appel

**Files:**
- Modify: `src/SamaEcole.Web/wwwroot/js/auth.js` (store `schoolConfig` : `workingDays`, `isWorkingDay`)
- Modify: `src/SamaEcole.Web/wwwroot/js/settings.js` (état `config`, `load()`, `saveConfig()`) et `Views/Settings/Index.cshtml` (spoke « Pédagogie », après le bloc « Découpage de l'année scolaire »)
- Modify: `src/SamaEcole.Web/wwwroot/js/teachers.js` (`daysOfWeek`, sélecteur du formulaire) et `Views/Teachers/Index.cshtml` (l.798, l.819, l.890)
- Modify: `src/SamaEcole.Web/wwwroot/js/attendance.js` et la vue de l'appel (bandeau « jour de repos »)
- Modify: `src/SamaEcole.Web/wwwroot/js/help.js` (fiches Emploi du temps et Présences)
- Test: `src/SamaEcole.Web/tests/js/working-days-config.test.mjs` (créer)

**Interfaces:**
- Consumes : `GET /schools/current/settings` → `workingDays: string[]` (noms anglais, **déjà dans l'ordre d'affichage**), 422 des Tâches 2-3.
- Produces :
  - `Alpine.store('schoolConfig').workingDays` : `number[]` (`DayOfWeek` : dimanche = 0), ordre d'affichage ; défaut `[1,2,3,4,5,6]` tant que la réponse n'est pas arrivée ; `isWorkingDay(isoDate)` → `boolean`
  - `settings.config.workingDays` : `string[]` ; `settings.toggleWorkingDay(name)`, `settings.applyWeekPreset(key)` (`'mon-fri' | 'mon-sat' | 'sat-wed'`)
  - `teachersView.daysOfWeek` : getter — jours ouvrés (ordre serveur) **plus** toute colonne portant encore un créneau hérité, `{ value, label, isRest }`

- [ ] **Step 1 : tests JS qui échouent** (`working-days-config.test.mjs`, montage de `harness.mjs` comme `settings-save-preserves-fields.test.mjs`) :
  1. **Store** : avec un serveur qui renvoie `workingDays: ['Saturday','Sunday','Monday','Tuesday','Wednesday']`, `schoolConfig.workingDays` vaut `[6,0,1,2,3]` ; `isWorkingDay('2026-09-24')` (jeudi) est `false`, `isWorkingDay('2026-09-26')` (samedi) `true`. Réponse en erreur → défaut `[1,2,3,4,5,6]`.
  2. **Réglage** : `load()` remplit `config.workingDays` ; `applyWeekPreset('sat-wed')` donne `['Saturday','Sunday','Monday','Tuesday','Wednesday']` ; `toggleWorkingDay('Saturday')` la retire et la remet ; `saveConfig()` envoie `workingDays` (et **conserve** celui du serveur quand l'écran n'y a pas touché — non-régression de l'Évolution N°1).
  3. **Garde-fou d'écran** : retirer le dernier jour est refusé côté écran (`config.workingDays` garde un jour) — le serveur revalide de toute façon (422).
  4. **Grille** : `daysOfWeek` (école repos jeudi/vendredi) = `[Samedi, Dimanche, Lundi, Mardi, Mercredi]`, tous `isRest: false` ; avec un créneau hérité un jeudi : une sixième entrée `{ value: 4, label: 'Jeudi', isRest: true }` en fin de liste.
  5. **Appel** : `attendance.isRestDay` est vrai quand `filters.date` tombe un jour de repos, et `canLoad` (ou l'équivalent qui déclenche le chargement de la feuille) reste faux — aucune requête n'est envoyée.

  Run : `node --test tests/js/working-days-config.test.mjs` (depuis `src/SamaEcole.Web`) → FAIL.

- [ ] **Step 2 : store `schoolConfig` (`auth.js`).** Ajouter à l'objet du store :

```js
        /**
         * Jours OUVRÉS de l'établissement (DayOfWeek : dimanche = 0), dans l'ordre d'AFFICHAGE calculé par
         * le serveur (SchoolWeek.DisplayOrder). Défaut lundi → samedi tant que la réponse n'est pas
         * arrivée : la grille historique. CONFORT d'affichage — le vrai verrou est WorkingDayGuard (422).
         */
        workingDays: [1, 2, 3, 4, 5, 6],

        isWorkingDay(isoDate) {
            const [y, m, d] = String(isoDate).split('-').map(Number);
            if (!y || !m || !d) return true; // date illisible : on ne bloque pas, le serveur juge
            return this.workingDays.includes(new Date(y, m - 1, d).getDay());
        },
```

  et dans `_load()`, après `internatEnabled` : `this.workingDays = window.dayIndexes(s && s.workingDays);` — avec, en tête du même fichier, la table partagée :

```js
    const DAY_INDEX = { Sunday: 0, Monday: 1, Tuesday: 2, Wednesday: 3, Thursday: 4, Friday: 5, Saturday: 6 };
    /** Noms de jours de l'API → index DayOfWeek, ordre conservé ; défaut lundi → samedi si absent/vide. */
    window.dayIndexes = (names) => {
        const list = Array.isArray(names) ? names.map((n) => DAY_INDEX[n]).filter((i) => i !== undefined) : [];
        return list.length > 0 ? list : [1, 2, 3, 4, 5, 6];
    };
```

  (`catch` : `this.workingDays = [1, 2, 3, 4, 5, 6];`.)

- [ ] **Step 3 : réglage (`settings.js` + vue).** `config.workingDays: ['Monday','Tuesday','Wednesday','Thursday','Friday','Saturday']` dans l'état, `load()` (`workingDays: config.workingDays || [...]`), corps du PUT et réaffectation après PUT (`saved.workingDays`). Méthodes :

```js
        weekPresets: {
            'mon-fri': ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'],
            'mon-sat': ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'],
            'sat-wed': ['Saturday', 'Sunday', 'Monday', 'Tuesday', 'Wednesday']   // repos jeudi/vendredi
        },

        applyWeekPreset(key) {
            this.config.workingDays = [...this.weekPresets[key]];
        },

        toggleWorkingDay(name) {
            const has = this.config.workingDays.includes(name);
            // Un établissement sans aucun jour ouvré ne pourrait plus rien saisir : le dernier jour reste.
            if (has && this.config.workingDays.length === 1) return;
            this.config.workingDays = has
                ? this.config.workingDays.filter((d) => d !== name)
                : [...this.config.workingDays, name];
        },
```

  Vue (spoke « Pédagogie », Directeur seul, même patron que le bloc « Découpage » de l'Évolution N°2) : titre « Jours ouvrés de l'établissement », sept cases à cocher (Lundi → Dimanche) liées à `toggleWorkingDay`, trois boutons de préréglage (« Lundi → Vendredi », « Lundi → Samedi », « Samedi → Mercredi (repos jeudi et vendredi) »), le texte : « Les jours non cochés sont des jours de repos : l'appel, le pointage des enseignants et l'emploi du temps n'y acceptent aucune saisie. Les données déjà enregistrées ne sont pas modifiées. », et le bouton Enregistrer (`saveConfig()`), erreur `configErrors.workingdays`.

- [ ] **Step 4 : grille d'emploi du temps (`teachers.js`).** Remplacer le tableau `daysOfWeek` en dur (l.89-96) par un getter :

```js
        get daysOfWeek() {
            const labels = { 0: 'Dimanche', 1: 'Lundi', 2: 'Mardi', 3: 'Mercredi', 4: 'Jeudi', 5: 'Vendredi', 6: 'Samedi' };
            const working = Alpine.store('schoolConfig').workingDays;
            const columns = working.map((value) => ({ value, label: labels[value], isRest: false }));

            // Un créneau hérité sur un jour devenu repos ne doit pas disparaître : colonne marquée « repos »
            // (arbitrage D3) — on peut la lire et supprimer ses créneaux, pas en créer.
            const legacy = [...new Set(this.scheduleSlots.map((s) => s.dayOfWeek))]
                .filter((value) => !working.includes(value))
                .sort((a, b) => a - b);
            return columns.concat(legacy.map((value) => ({ value, label: labels[value], isRest: true })));
        },

        /** Options du sélecteur « Jour » du formulaire : jours ouvrés uniquement. */
        get workingDayOptions() {
            return this.daysOfWeek.filter((d) => !d.isRest).map((d) => ({ value: d.value, label: d.label }));
        },
```

  Appeler `Alpine.store('schoolConfig').init()` au démarrage de la vue (réentrant, un seul fetch), fixer le jour par défaut du formulaire sur le premier jour ouvré (`scheduleForm.dayOfWeek = this.workingDayOptions[0]?.value`, l.85 et l.799). Dans la vue : `options-expr="workingDayOptions"` (l.890) ; l'en-tête de la colonne (l.798) affiche `day.label` suivi d'un badge « repos » si `day.isRest`.

- [ ] **Step 5 : écran d'appel (`attendance.js` + vue).** Ajouter :

```js
        /** Vrai quand la date choisie est un jour de repos de l'établissement (confort : le serveur refuse en 422). */
        get isRestDay() {
            return Boolean(this.filters.date) && !Alpine.store('schoolConfig').isWorkingDay(this.filters.date);
        },
```

  Faire de `isRestDay` une condition **négative** de `canLoad` (le getter qui vérifie déjà `classroomId && subjectId && date && period`, l.91), et afficher dans la vue un bandeau d'information — « Ce jour est un jour de repos de l'établissement : aucun appel n'y est enregistré. Choisissez un jour ouvré, ou ajustez les jours ouvrés dans Paramètres › Pédagogie. » — sous le champ date. Le message du serveur (422) reste affiché tel quel si l'appel part quand même (`toMessage`).

- [ ] **Step 6 : aide en ligne.** Dans `help.js`, fiches Emploi du temps et Présences : une phrase sur les jours de repos (« … se règlent dans Paramètres › Pédagogie »), sans changer le reste. Si `help.test.mjs` / `help-render.test.mjs` échouent sur un texte modifié, mettre à jour l'attendu (le texte d'aide **est** le contrat).

- [ ] **Step 7 : constater le succès** — `node --test tests/js/*.test.mjs` → tout vert ; `dotnet build src/SamaEcole.Web -c Release` (compile les vues Razor).

- [ ] **Step 8 : commit**

```bash
git add src/SamaEcole.Web
git commit -m "feat(working-days): jours ouvrés dans Paramètres, grille d'emploi du temps et écran d'appel"
```

---

### Task 6 : Documentation et contexte actif

**Files:**
- Modify: `openapi.yaml` (schéma `SchoolSettings` : `workingDays` ; réponses 422 des routes d'appel, de pointage et de créneaux)
- Modify: `docs/Volume_4_API_Design.md` (section Paramètres et sections Présences / Emploi du temps : 422 « jour de repos »)
- Modify: `docs/Volume_1_Cahier_des_Charges.md` §21 (Emploi du temps & Pointage) et la section Présences
- Modify: `docs/Volume_3_DDS.md` (colonne `school_settings.WorkingDays`, à la suite du paragraphe de l'Évolution N°2)
- Modify: `ACTIVE_CONTEXT.md` §2 — nouvelle sous-section « Jours ouvrés configurables (Évolution N°3) »

- [ ] **Step 1 :** rédiger les mises à jour avec leur texte final. `openapi.yaml` :

```yaml
        workingDays:
          type: array
          items: { type: string, enum: [Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday] }
          minItems: 1
          description: >-
            Jours OUVRÉS de l'établissement, dans l'ordre d'affichage (la semaine commence le lendemain du
            bloc de repos). Défaut : lundi → samedi. Absent ou `null` en PUT = inchangé. Les autres jours sont
            des jours de repos : appel des élèves, pointage des enseignants et créneaux d'emploi du temps y
            sont refusés (422) ; les données déjà enregistrées ne sont pas modifiées.
```

  `ACTIVE_CONTEXT.md` : périmètre livré, les arbitrages **D1-D6 tels que validés**, et l'invariant « le verrou ne joue que sur les saisies nouvelles ; aucun calcul de taux n'a changé, ils portent sur les appels réellement saisis ».
- [ ] **Step 2 :** `python -c "import yaml; yaml.safe_load(open('openapi.yaml', encoding='utf-8'))"` (le YAML reste valide), `dotnet build`, `node --test tests/js/*.test.mjs` (depuis `src/SamaEcole.Web`) → verts.
- [ ] **Step 3 : commit**

```bash
git add openapi.yaml docs ACTIVE_CONTEXT.md
git commit -m "docs(working-days): routes, réglage et contexte actif de l'évolution jours ouvrés"
```

---

## Vérification finale (à lancer par le propriétaire)

- `dotnet test` (suite complète) et `dotnet test --filter Category=MultiTenant` ;
- `node --test tests/js/*.test.mjs` ;
- Recette manuelle : Paramètres › Pédagogie → « Samedi → Mercredi » → Enregistrer ; **Enseignants › Emploi du temps** : colonnes Samedi, Dimanche, Lundi, Mardi, Mercredi, sélecteur « Jour » limité à ces jours ; **Présences** : choisir un jeudi → bandeau « jour de repos », feuille non chargée ; choisir un samedi → feuille chargée, appel enregistré ; **API** : `POST /attendance` daté d'un jeudi → 422 lisible ; école restée par défaut (lundi → samedi) : un dimanche est refusé, un samedi accepté ; **créneau hérité** posé un jeudi avant le changement : colonne « Jeudi (repos) » visible, suppression possible.

## Self-review (spec ↔ tâches)

| Exigence | Tâche |
|---|---|
| 1. Paramètre `SchoolSettings` des jours ouvrés / de week-end | 1 (logique), 2 (colonne, API, validation), 5 (écran de réglage + préréglages dont « Samedi → Mercredi ») |
| 2a. Grille (et « générateur ») d'emploi du temps : jours ouvrés configurés, ex. samedi → mercredi | 3 (validateur/handler des créneaux), 5 (grille, sélecteur, ordre D5) |
| 2b. Registre d'appel et de présence verrouillé sur les jours de repos | 3 (appel : soumission **et** ouverture ; pointage enseignants), 5 (bandeau d'écran) |
| 3. Taux de présence : week-ends spécifiques non comptés comme jours de classe | 3 (aucun appel nouveau possible un jour de repos), 4 (tests qui figent : pas de dénominateur calendaire, historique conservé) — **aucun calcul modifié**, voir « Constat » et D2 |
| Rétrocompatibilité des écoles existantes | 2 (défaut base = lundi → samedi, D1), 5 (défaut client identique tant que la réponse n'est pas arrivée) |
| Isolation multi-tenant | 3 (test `MultiTenant` : le garde lit les réglages de l'école courante) |

Points de vigilance connus, non traités par ce plan : (1) les billets d'entrée/sortie et retards restent saisissables un jour de repos (D4) — à durcir si l'école le demande ; (2) aucun calendrier de jours fériés ni de vacances (D6) ; (3) `TeacherAttendance.Date` est un `DateTime` : le garde travaille sur `DateOnly.FromDateTime(...)` (l'école est à UTC+0, aucun décalage de fuseau).
